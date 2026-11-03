using System.Collections.Concurrent;
using System.Diagnostics;
using DW4.Net;
using LiteNetLib;

namespace DW4.Server;

/// <summary>
/// Laço do servidor: aceita conexões com a chave do DW4, confere o ticket na DMO.API, repassa posições pela <see cref="World"/>
/// e manda o heartbeat de presença. Tudo roda numa thread; as respostas da API voltam por uma fila.
/// </summary>
public sealed class GameServer
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
            Reject(oldPeer, "Sua conta entrou em outro lugar.");
        }
        Log($"[{p.Id}] entrou: {who.Tamer} (conta {who.AccountId}) com {h.Character}");
        Send(p, new Welcome(p.Id, who.Tamer), true);
        _ = SafeAsync(async () => { var party = await _api.PresenceAsync(who.AccountId, true); _main.Enqueue(() => Send(_world.SetParty(p.Id, party))); });
    }

    static readonly HashSet<string> Actions =
        ["relation", "friend-request", "party-invite", "friend-accept", "friend-decline", "party-accept", "party-decline"];
    readonly Dictionary<int, DateTime> _lastInteract = [];

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
        bool answer = x.Action.EndsWith("-accept") || x.Action.EndsWith("-decline");
        if (_world.Who(x.TargetId) is not { } target || x.TargetId == p.Id || (!answer && !_world.SameInstance(p.Id, x.TargetId)))
        {
            Send(p, new InteractResult(x.TargetId, x.Action, false, "Esse jogador não está mais perto."), true);
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
                _ = SafeAsync(async () => { var party = await _api.PresenceAsync(acc, true); _main.Enqueue(() => Send(_world.SetParty(id, party))); });
    }

    void Drop(int id)
    {
        _peers.Remove(id);
        _connectedAt.Remove(id);
        _rate.Remove(id);
        _lastInteract.Remove(id);
        _coopRate.Remove(id);
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
            _ = SafeAsync(async () => { var party = await _api.PresenceAsync(acc, true); _main.Enqueue(() => Send(_world.SetParty(id, party))); });
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
