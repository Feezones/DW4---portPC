using System.Collections.Concurrent;
using System.Diagnostics;
using DW4.Net;
using LiteNetLib;

namespace DW4.Server;

/// <summary>
/// Laço do servidor: aceita conexões com a chave do DW4, confere o ticket na DMO.API, repassa posições pela <see cref="World"/>
/// e manda o heartbeat de presença. Tudo roda numa thread; as respostas da API voltam por uma fila.
/// </summary>
public sealed partial class GameServer
{
    static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);
    const int MaxStatesPerSecond = 40;

    readonly IAccountApi _api;
    readonly World _world = new();
    readonly NetManager _net;
    readonly ConcurrentQueue<Action> _main = new();
    readonly Dictionary<int, NetPeer> _peers = [];
    readonly Dictionary<int, DateTime> _connectedAt = [];
    readonly HashSet<int> _authenticating = [];
    readonly Dictionary<int, (long Second, int Count)> _rate = [];
    DateTime _nextHeartbeat = DateTime.UtcNow + HeartbeatEvery;

    public GameServer(IAccountApi api)
    {
        _api = api;
        var l = new EventBasedNetListener();
        _net = new NetManager(l) { DisconnectTimeout = 15000, AutoRecycle = true };
        l.ConnectionRequestEvent += r => r.AcceptIfKey(Protocol.ConnectionKey);
        l.PeerConnectedEvent += p => { _peers[p.Id] = p; _connectedAt[p.Id] = DateTime.UtcNow; };
        l.PeerDisconnectedEvent += (p, _) => Drop(p.Id);
        l.NetworkReceiveEvent += (p, reader, _, _) =>
        {
            var data = reader.GetRemainingBytes();
            try { OnPacket(p, Codec.Read(data)); }
            catch (FormatException ex) { Log($"[{p.Id}] pacote inválido ({ex.Message}); desconectado"); p.Disconnect(); }
        };
    }

    public int Port => _net.LocalPort;

    public bool Start(int port) => _net.Start(port);

    /// <summary>Uma volta do laço (chamada a cada ~15 ms).</summary>
    public void Tick()
    {
        _net.PollEvents();
        while (_main.TryDequeue(out var a)) a();
        var now = DateTime.UtcNow;
        foreach (var (id, at) in _connectedAt.ToList())
            if (!_world.IsOnline(id) && !_authenticating.Contains(id) && now - at > HelloTimeout && _peers.TryGetValue(id, out var p))
            {
                Log($"[{id}] sem Hello em {HelloTimeout.TotalSeconds} s; desconectado");
                p.Disconnect();
            }
        if (now >= _nextHeartbeat) { _nextHeartbeat = now + HeartbeatEvery; Heartbeat(); }
        TickTrades(now);
    }

    public void Stop()
    {
        foreach (var (_, acc) in _world.Accounts.ToList()) _ = SafeAsync(() => _api.PresenceAsync(acc, false));
        _net.Stop();
    }

    void OnPacket(NetPeer p, object msg)
    {
        switch (msg)
        {
            case Hello h when !_world.IsOnline(p.Id) && !_authenticating.Contains(p.Id):
                if (h.Version != Protocol.Version) { Reject(p, "Versão do jogo diferente da do servidor. Atualize pelo launcher."); return; }
                _authenticating.Add(p.Id);
                var ip = p.Address.ToString();
                _ = Task.Run(async () =>
                {
                    Identity? who = null; string? error = null;
                    try { who = await _api.RedeemAsync(h.Ticket, ip); }
                    catch (Exception ex) { error = ex.Message; }
                    _main.Enqueue(() => Admit(p, h, who, error));
                });
                break;
            case CoopRelay x when _world.IsOnline(p.Id):
                if (!AllowCoop(p.Id)) return;
                Send(_world.Relay(p.Id, x.Payload, x.Reliable));
                break;
            case Interact x when _world.IsOnline(p.Id):
                OnInteract(p, x);
                break;
            case SaveList or SaveWrite or SaveDelete when _world.IsOnline(p.Id):
                OnSave(p, msg);
                break;
            case ChatSend x when _world.IsOnline(p.Id):
                OnChat(p, x);
                break;
            case TradeCmd x when _world.IsOnline(p.Id):
                OnTrade(p, x);
                break;
            case PlayerState s when _world.IsOnline(p.Id):
                if (!Allow(p.Id)) return;
                Send(_world.Update(p.Id, s));
                break;
            default:
                Log($"[{p.Id}] {msg.GetType().Name} fora de hora; desconectado");
                p.Disconnect();
                break;
        }
    }

    void Admit(NetPeer p, Hello h, Identity? who, string? error)
    {
        _authenticating.Remove(p.Id);
        if (!_peers.ContainsKey(p.Id)) return; // caiu enquanto conferia
        if (who == null)
        {
            Log($"[{p.Id}] entrada recusada ({error ?? "ticket inválido"})");
            Reject(p, error == null ? "Sessão do launcher inválida ou vencida. Feche o jogo e clique em Jogar de novo." : "Servidor da conta fora do ar. Tente de novo em instantes.");
            return;
        }
        if (_world.Join(p.Id, who, h.Character) is int old && _peers.TryGetValue(old, out var oldPeer))
        {
            Log($"[{old}] mesma conta entrou de novo; sessão antiga derrubada");
            Send(_world.Leave(old));
            Reject(oldPeer, Protocol.KickedReason);
        }
        Log($"[{p.Id}] entrou: {who.Tamer} (conta {who.AccountId}) com {h.Character}");
        Send(p, new Welcome(p.Id, who.Tamer), true);
        RefreshParty(p.Id, who.AccountId);
    }

    static readonly HashSet<string> Actions =
        ["relation", "friend-request", "party-invite", "friend-accept", "friend-decline", "party-accept", "party-decline",
         "party-leave", "party-kick", "party-promote"];

    /// <summary>Pergunta a party da conta à DMO.API (presença) e atualiza o mundo: grupo de combate e quadro com o líder.</summary>
    void RefreshParty(int id, long accountId) =>
        _ = SafeAsync(async () =>
        {
            var party = await _api.PresenceAsync(accountId, true);
            _main.Enqueue(() => Send(_world.SetParty(id, party?.Id, party?.LeaderAccountId ?? 0)));
        });

    /// <summary>
    /// Gestão do grupo pelo quadro da party (protocolo 7): confere aqui antes de ir à DMO.API (que confere de novo). Sair = você
    /// numa party; expulsar e passar a liderança = você é o líder e o alvo está na sua party (em qualquer fase). Devolve o motivo
    /// da recusa, ou null.
    /// </summary>
    public static string? PartyManageCheck(World world, int me, int target, string action)
    {
        var who = world.Who(me);
        if (who?.PartyId is not { Length: > 0 } party) return "Você não está em um grupo.";
        if (action == "party-leave") return target == me ? null : "Pedido inválido.";
        if (target == me) return "Você não pode fazer isso consigo mesmo.";
        if (!world.IsLeader(me)) return "Só o líder do grupo pode fazer isso.";
        if (world.Who(target)?.PartyId != party) return "Esse jogador não está no seu grupo.";
        return null;
    }
    readonly Dictionary<int, DateTime> _lastInteract = [];
    // Contra spam no chat: 5 mensagens a cada 5 s por conta (a mesma regra do jogo, ChatFlood).
    readonly DW4.Gameplay.ChatFlood _chatFlood = new();

    /// <summary>
    /// Chat (0.6.8): limpa o texto (uma linha, até 120 letras), segura o spam por conta e entrega pela <see cref="World"/>. O texto
    /// não vai para o log.
    /// </summary>
    void OnChat(NetPeer p, ChatSend x)
    {
        var who = _world.Who(p.Id)!;
        var text = DW4.Gameplay.ChatLog.Clean(x.Text);
        if (text.Length == 0) return;
        if (!_chatFlood.Allow(who.AccountId.ToString(), DateTime.UtcNow))
        {
            Send(p, new ChatNotice("Muitas mensagens seguidas. Espere um pouco."), true);
            return;
        }
        Send(_world.Chat(p.Id, x.Channel, x.To.Trim(), text, out var notice));
        if (notice != null) Send(p, new ChatNotice(notice), true);
    }

    /// <summary>
    /// Menu do F: a ação vai para a DMO.API em nome da conta deste jogador (as regras de amigos e party são as do launcher).
    /// Pedido de amizade e convite de grupo aceitos viram uma pergunta (<see cref="SocialPrompt"/>) para o alvo, se ele está
    /// no DW4; aceitar o convite atualiza a party dos dois na hora (sem esperar o heartbeat).
    /// </summary>
    void OnInteract(NetPeer p, Interact x)
    {
        var now = DateTime.UtcNow;
        if (_lastInteract.TryGetValue(p.Id, out var last) && now - last < TimeSpan.FromMilliseconds(400)) return;
        _lastInteract[p.Id] = now;
        if (!Actions.Contains(x.Action)) { Log($"[{p.Id}] ação {x.Action} desconhecida"); return; }
        var me = _world.Who(p.Id)!;
        if (x.Action is "party-leave" or "party-kick" or "party-promote")
        {
            if (PartyManageCheck(_world, p.Id, x.TargetId, x.Action) is { } refused)
            {
                Send(p, new InteractResult(x.TargetId, x.Action, false, refused), true);
                return;
            }
            var tw = _world.Who(x.TargetId)!;
            var members = _world.PartyMembers(me.PartyId);
            _ = Task.Run(async () =>
            {
                SocialReply r;
                try { r = await _api.SocialAsync(me, x.Action, tw, ""); }
                catch (Exception ex) { Log("API: " + ex.Message); r = new SocialReply(false, "Servidor da conta fora do ar. Tente de novo em instantes."); }
                _main.Enqueue(() => AfterPartyManage(p, x, me, tw, members, r));
            });
            return;
        }
        bool answer = x.Action.EndsWith("-accept") || x.Action.EndsWith("-decline");
        if (_world.Who(x.TargetId) is not { } target || x.TargetId == p.Id || (!answer && !_world.SameInstance(p.Id, x.TargetId)))
        {
            Send(p, new InteractResult(x.TargetId, x.Action, false, "Esse jogador não está mais perto."), true);
            return;
        }
        // Amigos e party da API são do Tamer do DMO: conta sem Tamer não é achada pelo nome (a API diria "Jogador rd… não
        // encontrado", com o usuário gerado no lugar do nome). Avisa antes, com o motivo de verdade.
        if (x.Action is not ("party-accept" or "party-decline") && NoTamerNotice(me, target, _world.Name(x.TargetId)) is { } why)
        {
            Send(p, new InteractResult(x.TargetId, x.Action, false, why), true);
            return;
        }
        _ = Task.Run(async () =>
        {
            SocialReply r;
            try { r = await _api.SocialAsync(me, x.Action, target, x.InviteId); }
            catch (Exception ex) { Log("API: " + ex.Message); r = new SocialReply(false, "Servidor da conta fora do ar. Tente de novo em instantes."); }
            _main.Enqueue(() => AfterInteract(p, x, me, target, r));
        });
    }

    public static string? NoTamerNotice(Identity me, Identity target, string targetName) =>
        !me.HasTamer ? "Sua conta ainda não tem um Tamer no RE:DIGITAL ONLINE, por isso amigos e grupo não estão disponíveis."
        : !target.HasTamer ? $"{targetName} ainda não tem um Tamer no RE:DIGITAL ONLINE, por isso não dá para adicionar nem chamar para o grupo."
        : null;

    void AfterInteract(NetPeer p, Interact x, Identity me, Identity target, SocialReply r)
    {
        if (!_peers.ContainsKey(p.Id)) return;
        Send(p, new InteractResult(x.TargetId, x.Action, r.Ok, r.Message, r.Relation, r.HasParty, r.AmLeader, r.TargetInMyParty,
            r.PartyOccupied, r.TargetInOtherParty), true);
        if (!r.Ok) return;
        Log($"[{p.Id}] {me.Tamer}: {x.Action} {target.Tamer}");
        _peers.TryGetValue(x.TargetId, out var tp);
        if (x.Action == "friend-request" && !r.Accepted && tp != null) Send(tp, new SocialPrompt("friend", p.Id, me.Tamer), true);
        if (x.Action == "party-invite" && tp != null) Send(tp, new SocialPrompt("party", p.Id, me.Tamer, r.InviteId), true);
        if (x.Action is "party-accept")
            foreach (var (id, acc) in new[] { (p.Id, me.AccountId), (x.TargetId, target.AccountId) })
                RefreshParty(id, acc);
    }

    // Depois de sair/expulsar/passar a liderança: todos que estavam no grupo perguntam a party de novo (quem saiu fica sem quadro,
    // o líder novo aparece com a coroa) e os outros recebem um aviso no chat.
    void AfterPartyManage(NetPeer p, Interact x, Identity me, Identity target, List<(int Id, long AccountId)> members, SocialReply r)
    {
        if (_peers.ContainsKey(p.Id)) Send(p, new InteractResult(x.TargetId, x.Action, r.Ok, r.Message), true);
        if (!r.Ok) return;
        Log($"[{p.Id}] {me.Tamer}: {x.Action} {target.Tamer}");
        foreach (var (id, acc) in members) RefreshParty(id, acc);
        string myName = _world.Name(p.Id), targetName = _world.Name(x.TargetId);
        string? toTarget = x.Action switch
        {
            "party-kick" => $"{myName} tirou você do grupo.",
            "party-promote" => $"{myName} passou a liderança do grupo para você.",
            _ => null,
        };
        if (toTarget != null && _peers.TryGetValue(x.TargetId, out var tp)) Send(tp, new ChatNotice(toTarget), true);
        string toOthers = x.Action switch
        {
            "party-kick" => $"{targetName} saiu do grupo.",
            "party-promote" => $"{targetName} agora é o líder do grupo.",
            _ => $"{myName} saiu do grupo.",
        };
        foreach (var (id, _) in members)
            if (id != p.Id && id != x.TargetId && _peers.TryGetValue(id, out var op)) Send(op, new ChatNotice(toOthers), true);
    }

    // ---------- vagas de personagem (saves no servidor) ----------

    // Pedidos de save por conta, um depois do outro: duas gravações seguidas (troca de fase e F5) chegam à API na ordem em que o
    // jogo mandou, então a mais nova nunca é sobrescrita pela mais velha.
    readonly Dictionary<long, Task> _saveChain = [];
    readonly Dictionary<int, (long Window, int Count)> _saveRate = [];
    const int MaxSavesPer10s = 20;

    /// <summary>
    /// Lista, grava ou apaga uma vaga da conta deste jogador (a conta é a do ticket; o cliente nunca escolhe a conta). O save é
    /// conferido (JSON de objeto, até <see cref="Protocol.MaxSaveJson"/>) antes de ir para a DMO.API.
    /// </summary>
    void OnSave(NetPeer p, object msg)
    {
        long window = Stopwatch.GetTimestamp() / Stopwatch.Frequency / 10;
        var (w, n) = _saveRate.TryGetValue(p.Id, out var v) && v.Window == window ? v : (window, 0);
        _saveRate[p.Id] = (w, n + 1);
        int reqId = msg switch { SaveList x => x.ReqId, SaveWrite x => x.ReqId, SaveDelete x => x.ReqId, _ => 0 };
        if (n >= MaxSavesPer10s) { Send(p, new SaveReply(reqId, false, "Muitas gravações seguidas. Tente de novo em instantes.", []), true); return; }
        var who = _world.Who(p.Id)!;
        Func<Task<SaveReply>> work;
        switch (msg)
        {
            case SaveList:
                work = async () =>
                {
                    var list = await _api.ListCharactersAsync(who.AccountId);
                    return new SaveReply(reqId, true, "", list.Where(c => c.Slot is >= 0 and < Protocol.Slots)
                        .Select(c => new SaveEntry(c.Slot, SaveData.Pack(c.Json))).ToArray());
                };
                break;
            case SaveWrite x:
                string json, name;
                try { (json, name) = CheckSave(x.Data); }
                catch (FormatException ex)
                {
                    Log($"[{p.Id}] save recusado ({ex.Message})");
                    Send(p, new SaveReply(reqId, false, "Save inválido.", []), true);
                    return;
                }
                int seq = DW4.Gameplay.Online.TradeLedger.SeqOf(json);
                work = async () =>
                {
                    // Gravação de antes de uma troca chegando depois dela: desfaria a troca (item duplicado). Conferido aqui, na
                    // fila da conta, porque a troca também grava nessa fila.
                    if (_tradeSeqs.TryGetValue((who.AccountId, x.Slot), out var min) && seq < min)
                        return new SaveReply(reqId, false, StaleTradeSave, []);
                    await _api.SaveCharacterAsync(who.AccountId, x.Slot, name, json);
                    return new SaveReply(reqId, true, "", []);
                };
                break;
            case SaveDelete x:
                work = async () =>
                {
                    await _api.DeleteCharacterAsync(who.AccountId, x.Slot);
                    _tradeSeqs.TryRemove((who.AccountId, x.Slot), out _); // personagem novo nessa vaga começa do 0
                    return new SaveReply(reqId, true, "", []);
                };
                break;
            default: return;
        }
        Chain([who.AccountId], async () =>
        {
            SaveReply reply;
            try { reply = await work(); }
            catch (Exception ex) { Log($"API (save {who.Tamer}): {ex.Message}"); reply = new SaveReply(reqId, false, "Servidor da conta fora do ar. Tente de novo em instantes.", []); }
            // A resposta vai para quem pediu, se ainda está conectado (a gravação vale mesmo se ele já saiu).
            _main.Enqueue(() => { if (_peers.TryGetValue(p.Id, out var peer)) Send(peer, reply, true); });
        });
    }

    /// <summary>
    /// Põe o trabalho na fila de gravação das contas (depois de tudo o que já estava nelas). A troca entra nas filas das duas contas
    /// ao mesmo tempo, então lê os saves só depois das gravações que os jogadores mandaram antes de confirmar.
    /// </summary>
    void Chain(long[] accounts, Func<Task> work)
    {
        var prev = Task.WhenAll(accounts.Select(a => _saveChain.TryGetValue(a, out var t) ? t : Task.CompletedTask));
        var next = prev.ContinueWith(_ => work(), TaskScheduler.Default).Unwrap();
        foreach (var a in accounts) _saveChain[a] = next;
        _ = next.ContinueWith(_ => _main.Enqueue(() =>
        {
            foreach (var a in accounts) if (_saveChain.TryGetValue(a, out var cur) && cur == next) _saveChain.Remove(a);
        }));
    }

    /// <summary>Descomprime e confere o save; devolve o JSON e o nome do Digimon (para a API).</summary>
    public static (string Json, string Name) CheckSave(byte[] data)
    {
        var json = SaveData.Unpack(data);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new FormatException("save não é um objeto");
            var name = doc.RootElement.TryGetProperty("Name", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String ? n.GetString() ?? "" : "";
            return (json, name.Length > 32 ? name[..32] : name);
        }
        catch (System.Text.Json.JsonException) { throw new FormatException("save não é JSON"); }
    }

    void Drop(int id)
    {
        _peers.Remove(id);
        _connectedAt.Remove(id);
        _rate.Remove(id);
        _lastInteract.Remove(id);
        _coopRate.Remove(id);
        _saveRate.Remove(id);
        TradeGone(id);
        if (_world.Who(id) is { } who)
        {
            Log($"[{id}] saiu: {who.Tamer}");
            Send(_world.Leave(id));
            _ = SafeAsync(() => _api.PresenceAsync(who.AccountId, false));
        }
    }

    void Heartbeat()
    {
        foreach (var (id, acc) in _world.Accounts.ToList())
            RefreshParty(id, acc);
    }

    // Combate em grupo: até 120 pacotes por segundo por jogador (sincronia a 10 Hz + golpes e sequências de vários inimigos).
    readonly Dictionary<int, (long Second, int Count)> _coopRate = [];
    const int MaxCoopPerSecond = 120;

    bool AllowCoop(int id)
    {
        long sec = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        var (s, n) = _coopRate.TryGetValue(id, out var v) ? v : (sec, 0);
        if (s != sec) (s, n) = (sec, 0);
        _coopRate[id] = (s, n + 1);
        return n < MaxCoopPerSecond;
    }

    bool Allow(int id)
    {
        long sec = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        var (s, n) = _rate.TryGetValue(id, out var v) ? v : (sec, 0);
        if (s != sec) (s, n) = (sec, 0);
        _rate[id] = (s, n + 1);
        return n < MaxStatesPerSecond;
    }

    void Send(List<Outgoing> list)
    {
        foreach (var o in list) if (_peers.TryGetValue(o.To, out var p)) Send(p, o.Message, o.Reliable);
    }

    // Sem garantia: o estado do jogador vai em sequência (o mais novo vale); a sincronia do grupo vai solta, para não brigar com
    // o estado no mesmo canal (Sequenced descarta o que chega depois de um pacote mais novo do canal, de qualquer tipo).
    static void Send(NetPeer p, object msg, bool reliable) =>
        p.Send(Codec.Write(msg), reliable ? DeliveryMethod.ReliableOrdered : msg is CoopFrom ? DeliveryMethod.Unreliable : DeliveryMethod.Sequenced);

    /// <summary>Recusa: o motivo vai junto do pedido de desconexão (um pacote mandado logo antes se perderia).</summary>
    void Reject(NetPeer p, string reason) => _net.DisconnectPeer(p, Codec.Write(new Reject(reason)));

    async Task SafeAsync(Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { Log("API: " + ex.Message); }
    }

    static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {s}");
}
