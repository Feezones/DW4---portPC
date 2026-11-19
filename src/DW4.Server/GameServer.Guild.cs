using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using LiteNetLib;

namespace DW4.Server;

/// <summary>
/// Guilda do DW4 (protocolo 8, pedido do responsável em 2026-11-07: guilda própria do DW4, por conta do launcher). As regras e o
/// banco são da DMO.API (api/dw4/guild, Dw4GuildService); aqui fica o que é do jogo ao vivo:
///  · a guilda de quem está online (cache por guilda) e a janela de cada um (<see cref="GuildView"/>, com quem está online e onde);
///  · a tag na placa (World.SetGuild → PlayerTag / Spawn.Guild) e o chat da guilda (World.Chat, canal Guild);
///  · o convite (vive aqui, 2 min, como o pedido de troca) e o "entrar", conferido de novo na API;
///  · o baú: guardar tira do save guardado na conta e põe no baú; tirar faz o contrário (TradeLedger.Apply, com o TradeSeq da troca,
///    na fila de gravação da conta); se o segundo passo falha, o primeiro volta;
///  · o XP da guilda: EXP ganha em inimigos (1 para cada 20) e chefes (100), em dobro com colega de guilda no mesmo grupo de
///    combate; até 3000 por conta por hora; juntado e mandado à API a cada 30 s.
/// </summary>
public sealed partial class GameServer
{
    static readonly TimeSpan GuildInviteTimeout = TimeSpan.FromMinutes(2);
    static readonly TimeSpan GuildXpFlushEvery = TimeSpan.FromSeconds(30);
    const long GuildXpPerHour = 3000;
    const int BossGuildXp = 100;

    readonly Dictionary<long, GuildData> _guildCache = [];
    readonly Dictionary<int, (int From, long FromAccount, long GuildId, DateTime At)> _guildInvites = [];
    readonly Dictionary<long, (long Pending, long Hour, long HourTotal)> _guildXp = [];
    readonly Dictionary<int, (long Second, int Count)> _guildRate = [];
    DateTime _nextGuildFlush = DateTime.UtcNow + GuildXpFlushEvery;

    // ---------- entrar e sair ----------

    /// <summary>Entrou com personagem: pergunta a guilda da conta, põe a tag e manda a janela e o aviso do dia.</summary>
    void GuildOnJoin(int id, Identity who, string character)
    {
        if (character.Length == 0) return; // conexão do menu
        CallGuild(new GuildRequest(who.AccountId, "get", character), r =>
        {
            if (!_peers.ContainsKey(id)) return;
            UseGuild(id, r.Guild, "");
            if (r.Guild is { Notice.Length: > 0 } g && _peers.TryGetValue(id, out var p))
                Send(p, new ChatFrom(ChatTo.Guild, 0, "Aviso da guilda", "", g.Notice), true);
        });
    }

    void GuildOnLeave(int id, long guildId)
    {
        _guildInvites.Remove(id);
        _guildRate.Remove(id);
        foreach (var (to, inv) in _guildInvites.Where(kv => kv.Value.From == id).ToList()) _guildInvites.Remove(to);
        if (guildId == 0) return;
        if (_world.GuildOnline(guildId).Count == 0) _guildCache.Remove(guildId);
        else PushGuild(guildId); // saiu da lista de online
    }

    // ---------- comandos ----------

    void OnGuild(NetPeer p, GuildCmd x)
    {
        long sec = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
        var (s0, n) = _guildRate.TryGetValue(p.Id, out var v) && v.Second == sec ? v : (sec, 0);
        _guildRate[p.Id] = (s0, n + 1);
        if (n >= 10) return;
        int me = p.Id;
        var who = _world.Who(me)!;
        string name = _world.Name(me);
        var g = MyGuild(me);
        switch (x.Action)
        {
            case "info":
                if (g != null) SendTo(me, ViewFor(me, g, ""));
                else CallGuild(new GuildRequest(who.AccountId, "get", name), r => UseGuild(me, r.Guild, ""));
                return;
            case "create":
                if (g != null) { Reply(me, "Você já está em uma guilda."); return; }
                CallGuild(new GuildRequest(who.AccountId, "create", name, GuildName: x.Text, Tag: x.Text2), r =>
                {
                    if (!r.Ok) { Reply(me, r.Message); return; }
                    Log($"[{me}] {who.Tamer} criou a guilda {r.Guild?.Name} [{r.Guild?.Tag}]");
                    UseGuild(me, r.Guild, r.Message);
                });
                return;
            case "invite":
                GuildInvite(me, who, g, x);
                return;
            case "accept":
            {
                if (!_guildInvites.Remove(me, out var inv) || inv.From != x.TargetId || DateTime.UtcNow - inv.At > GuildInviteTimeout)
                { Reply(me, "O convite para a guilda não vale mais."); return; }
                CallGuild(new GuildRequest(who.AccountId, "join", name, GuildId: inv.GuildId, InviterAccountId: inv.FromAccount), r =>
                {
                    if (!r.Ok) { Reply(me, r.Message); return; }
                    Log($"[{me}] {who.Tamer} entrou na guilda {r.Guild?.Name}");
                    UseGuild(me, r.Guild, r.Message);
                    GuildChat(r.Guild?.Id ?? 0, $"{name} entrou na guilda.", except: me);
                });
                return;
            }
            case "decline":
                if (_guildInvites.Remove(me, out var d) && _peers.TryGetValue(d.From, out var dp))
                    Send(dp, new ChatNotice($"{name} recusou o convite para a guilda."), true);
                return;
            case "log":
                if (g == null) { Reply(me, "Você não está em uma guilda."); return; }
                CallGuild(new GuildRequest(who.AccountId, "log", Take: Protocol.MaxGuildLog), r =>
                {
                    if (r.Log != null && _peers.TryGetValue(me, out var lp)) Send(lp, new GuildLog(r.Log.Select(FormatLog).ToArray()), true);
                });
                return;
            case "xp" or "boss":
                GuildXp(me, who, x.Action == "boss" ? BossGuildXp : Math.Max(1, Math.Clamp(x.Num, 0, 100_000) / 20), x.Num > 0 || x.Action == "boss");
                return;
            case "deposit" or "withdraw":
                GuildBank(me, who, name, g, x);
                return;
        }
        if (g == null) { Reply(me, "Você não está em uma guilda."); return; }
        long guildId = g.Id;
        var target = x.Account != 0 ? g.Member(x.Account) : null;
        GuildRequest? req = x.Action switch
        {
            "leave" => new(who.AccountId, "leave", name),
            "kick" => new(who.AccountId, "kick", name, TargetAccountId: x.Account),
            "rank" => new(who.AccountId, "rank", name, TargetAccountId: x.Account, Rank: x.Num),
            "transfer" => new(who.AccountId, "transfer", name, TargetAccountId: x.Account),
            "notice" => new(who.AccountId, "notice", name, Text: x.Text),
            _ => null,
        };
        if (req == null) { Log($"[{me}] ação de guilda {x.Action} desconhecida"); return; }
        CallGuild(req, r =>
        {
            if (!r.Ok) { Reply(me, r.Message); return; }
            Log($"[{me}] {who.Tamer}: guilda {x.Action} {target?.Name}");
            switch (x.Action)
            {
                case "leave":
                    UseGuild(me, null, r.Message);
                    GuildChat(guildId, $"{name} saiu da guilda.");
                    RefreshGuild(guildId);
                    return;
                case "kick":
                    UseGuild(me, r.Guild, r.Message);
                    if (OnlineAccount(x.Account) is int kicked)
                    {
                        UseGuild(kicked, null, $"Você foi tirado da guilda {g.Name}.");
                        if (_peers.TryGetValue(kicked, out var kp)) Send(kp, new ChatNotice($"Você foi tirado da guilda {g.Name}."), true);
                    }
                    GuildChat(guildId, $"{target?.Name ?? "Um membro"} saiu da guilda.");
                    return;
                case "notice":
                    UseGuild(me, r.Guild, r.Message);
                    if (r.Guild is { Notice.Length: > 0 } ng) GuildChat(guildId, ng.Notice, sender: "Aviso da guilda");
                    return;
                default:
                    UseGuild(me, r.Guild, r.Message);
                    if (r.Message.Length > 0) GuildChat(guildId, r.Message, except: me);
                    return;
            }
        });
    }

    void GuildInvite(int me, Identity who, GuildData? g, GuildCmd x)
    {
        if (g == null) { Reply(me, "Crie uma guilda ou entre em uma primeiro."); return; }
        if (g.Member(who.AccountId) is not { Rank: <= 2 }) { Reply(me, "Só o Mestre e os Sub-mestres convidam."); return; }
        // Pelo nome (janela da guilda) ou pelo id (menu de interagir, Text vazio: o id 0 é um jogador válido).
        bool byName = x.Text.Trim().Length > 0;
        int target = byName ? _world.FindByName(x.Text.Trim()) : x.TargetId;
        var tw = target >= 0 ? _world.Who(target) : null;
        if (tw == null) { Reply(me, byName ? $"{x.Text.Trim()} não está online." : "Esse jogador não está mais online."); return; }
        if (target == me) { Reply(me, "Você já está na guilda."); return; }
        if (_world.GuildOf(target) != 0) { Reply(me, _world.GuildOf(target) == g.Id ? "Esse jogador já está na sua guilda." : "Esse jogador já está em outra guilda."); return; }
        string targetName = _world.Name(target);
        CallGuild(new GuildRequest(who.AccountId, "can-invite", TargetAccountId: tw.AccountId), r =>
        {
            if (!r.Ok) { Reply(me, r.Message); return; }
            if (!_peers.TryGetValue(target, out var tp)) { Reply(me, $"{targetName} não está mais online."); return; }
            _guildInvites[target] = (me, who.AccountId, g.Id, DateTime.UtcNow);
            Send(tp, new SocialPrompt("guild", me, _world.Name(me), $"{g.Name} [{g.Tag}]"), true);
            Reply(me, $"Convite para a guilda enviado para {targetName}.");
        });
    }

    // ---------- baú ----------

    void GuildBank(int me, Identity who, string name, GuildData? g, GuildCmd x)
    {
        bool deposit = x.Action == "deposit", bit = x.Table == "bit";
        void Done(bool ok, string msg, int seq = 0) =>
            SendTo(me, new GuildBankDone(ok, x.Action, x.Table, x.Row, ok && !bit ? x.Num : 0, ok && bit ? x.Num : 0, seq, msg));
        if (g == null) { Done(false, "Você não está em uma guilda."); return; }
        if (x.Num <= 0 || (!bit && (x.Num > 99 || !TradeSession.Tradable(new ItemRef(x.Table, x.Row)))) || (bit && x.Num > Inventory.BitMax))
        { Done(false, "Pedido inválido."); return; }
        var offer = new TradeOffer();
        if (bit) offer.Bit = x.Num; else for (int i = 0; i < x.Num; i++) offer.Items.Add(new ItemRef(x.Table, x.Row));
        var empty = new TradeOffer();
        var items = bit ? [] : new List<GuildBankEntry> { new(x.Table, x.Row, x.Num) };
        long acc = who.AccountId;
        int slot = x.Slot;
        long guildId = g.Id;
        Chain([acc], async () =>
        {
            string? error = null;
            int seq = 0;
            GuildData? after = null;
            try
            {
                var json = (await _api.ListCharactersAsync(acc)).FirstOrDefault(c => c.Slot == slot).Json;
                if (json == null) error = "Personagem não encontrado no servidor.";
                else if (deposit)
                {
                    // 1º sai do save; 2º entra no baú. Baú cheio (ou API fora): o save volta ao que era.
                    var c = TradeLedger.Apply(json, offer, empty);
                    if (c.Error != null) error = c.Error;
                    else
                    {
                        _tradeSeqs.TryGetValue((acc, slot), out var before);
                        await _api.SaveCharacterAsync(acc, slot, Trim(c.NameA), c.JsonA);
                        _tradeSeqs[(acc, slot)] = c.SeqA;
                        GuildReply r;
                        try { r = await _api.GuildAsync(new GuildRequest(acc, "deposit", name, Items: items, Bit: offer.Bit)); }
                        catch (Exception ex) { Log($"API (baú): {ex.Message}"); r = new GuildReply(false, "Servidor da conta fora do ar.", null); }
                        if (!r.Ok)
                        {
                            await _api.SaveCharacterAsync(acc, slot, Trim(c.NameA), json);
                            if (before > 0) _tradeSeqs[(acc, slot)] = before; else _tradeSeqs.TryRemove((acc, slot), out _);
                            error = r.Message;
                        }
                        else (seq, after) = (c.SeqA, r.Guild);
                    }
                }
                else
                {
                    // 1º confere se cabe; 2º sai do baú; 3º entra no save. Save falhou: devolve ao baú.
                    var c = TradeLedger.Apply(json, empty, offer);
                    if (c.Error != null) error = c.Error;
                    else
                    {
                        var r = await _api.GuildAsync(new GuildRequest(acc, "withdraw", name, Items: items, Bit: offer.Bit));
                        if (!r.Ok) error = r.Message;
                        else
                        {
                            try { await _api.SaveCharacterAsync(acc, slot, Trim(c.NameA), c.JsonA); }
                            catch
                            {
                                await _api.GuildAsync(new GuildRequest(acc, "refund", name, Items: items, Bit: offer.Bit));
                                throw;
                            }
                            _tradeSeqs[(acc, slot)] = c.SeqA;
                            (seq, after) = (c.SeqA, r.Guild);
                        }
                    }
                }
            }
            catch (Exception ex) { Log($"API (baú {who.Tamer}): {ex.Message}"); error = "Servidor da conta fora do ar. Nada mudou no baú."; }
            _main.Enqueue(() =>
            {
                if (error != null) { Done(false, error); return; }
                Log($"[{me}] {who.Tamer}: baú {x.Action} {x.Table}:{x.Row} x{x.Num}");
                Done(true, "", seq);
                if (after != null) { _guildCache[after.Id] = after; PushGuild(after.Id); }
            });
        });
    }

    // ---------- XP ----------

    void GuildXp(int me, Identity who, long amount, bool valid)
    {
        if (!valid || _world.GuildOf(me) == 0) return;
        if (_world.GuildmateInGroup(me)) amount *= 2;
        long hour = DateTime.UtcNow.Ticks / TimeSpan.TicksPerHour;
        var (pending, h, total) = _guildXp.TryGetValue(who.AccountId, out var v) ? v : (0, hour, 0);
        if (h != hour) (h, total) = (hour, 0);
        amount = Math.Min(amount, GuildXpPerHour - total);
        if (amount <= 0) return;
        _guildXp[who.AccountId] = (pending + amount, h, total + amount);
    }

    void TickGuilds(DateTime now)
    {
        if (now < _nextGuildFlush) return;
        _nextGuildFlush = now + GuildXpFlushEvery;
        foreach (var (acc, v) in _guildXp.Where(kv => kv.Value.Pending > 0).ToList())
        {
            _guildXp[acc] = v with { Pending = 0 };
            int? id = OnlineAccount(acc);
            CallGuild(new GuildRequest(acc, "xp", Amount: v.Pending), r =>
            {
                if (!r.Ok || r.Guild == null) return;
                _guildCache[r.Guild.Id] = r.Guild;
                if (r.Message.StartsWith("Nível", StringComparison.Ordinal))
                {
                    GuildChat(r.Guild.Id, $"A guilda subiu para o nível {r.Guild.Level}! Bônus de {r.Guild.Bonus}% de EXP e BIT, {r.Guild.MaxMembers} vagas.");
                    PushGuild(r.Guild.Id);
                }
                else if (id is int pid) SendTo(pid, ViewFor(pid, r.Guild, ""));
            });
        }
        foreach (var (to, inv) in _guildInvites.Where(kv => now - kv.Value.At > GuildInviteTimeout).ToList()) _guildInvites.Remove(to);
    }

    // ---------- apoio ----------

    GuildData? MyGuild(int id) => _world.GuildOf(id) is var g && g != 0 && _guildCache.TryGetValue(g, out var d) ? d : null;

    int? OnlineAccount(long account)
    {
        foreach (var (id, acc) in _world.Accounts) if (acc == account) return id;
        return null;
    }

    /// <summary>A guilda (ou nenhuma) passa a valer para o jogador: tag, cache, janela dele (com a mensagem) e dos colegas online.</summary>
    void UseGuild(int id, GuildData? g, string message)
    {
        long old = _world.GuildOf(id);
        Send(_world.SetGuild(id, g?.Id ?? 0, g?.Tag ?? ""));
        if (g == null)
        {
            SendTo(id, GuildView.None(message));
            if (old != 0 && _world.GuildOnline(old).Count == 0) _guildCache.Remove(old);
            return;
        }
        _guildCache[g.Id] = g;
        SendTo(id, ViewFor(id, g, message));
        foreach (var other in _world.GuildOnline(g.Id)) if (other != id) SendTo(other, ViewFor(other, g, ""));
    }

    /// <summary>Manda a janela atual para todos os membros online.</summary>
    void PushGuild(long guildId)
    {
        if (!_guildCache.TryGetValue(guildId, out var g)) return;
        foreach (var id in _world.GuildOnline(guildId)) SendTo(id, ViewFor(id, g, ""));
    }

    /// <summary>Pergunta a guilda de novo pela conta de algum membro online (depois de alguém sair).</summary>
    void RefreshGuild(long guildId)
    {
        var online = _world.GuildOnline(guildId);
        if (online.Count == 0) { _guildCache.Remove(guildId); return; }
        var acc = _world.Who(online[0])!.AccountId;
        CallGuild(new GuildRequest(acc, "get"), r =>
        {
            if (r.Guild == null) { foreach (var id in _world.GuildOnline(guildId)) UseGuild(id, null, ""); return; }
            _guildCache[r.Guild.Id] = r.Guild;
            PushGuild(r.Guild.Id);
        });
    }

    GuildView ViewFor(int id, GuildData g, string message)
    {
        var online = _world.GuildOnline(g.Id).Select(pid => (pid, acc: _world.Who(pid)!.AccountId)).ToDictionary(x => x.acc, x => x.pid);
        var members = g.Members.Take(Protocol.MaxGuildMembers).Select(m =>
        {
            int pid = online.TryGetValue(m.AccountId, out var on) ? on : -1;
            var (stage, level) = pid >= 0 ? _world.Where(pid) : ("", 0);
            string name = pid >= 0 && _world.Name(pid) is { Length: > 0 } live ? live : m.Name;
            return new GuildMemberEntry(m.AccountId, name, m.Rank, m.Contribution, pid, stage, level,
                m.LastSeen == DateTime.MinValue ? 0 : new DateTimeOffset(DateTime.SpecifyKind(m.LastSeen, DateTimeKind.Utc)).ToUnixTimeSeconds());
        }).ToArray();
        int rank = g.Member(_world.Who(id)?.AccountId ?? 0)?.Rank ?? 0;
        return new GuildView(g.Id, g.Name, g.Tag, g.Level, g.Xp, g.LevelXp, g.NextXp, g.MaxMembers, g.Bonus, g.Notice, g.BankBit, g.LogoVersion,
            rank, members, g.Bank.Take(Protocol.MaxGuildBank).ToArray(), message);
    }

    /// <summary>Linha no chat da guilda para os membros online (avisos do servidor: entrou, saiu, nível).</summary>
    void GuildChat(long guildId, string text, int except = -1, string sender = "Guilda")
    {
        if (guildId == 0) return;
        var msg = new ChatFrom(ChatTo.Guild, 0, sender, "", text.Length > Protocol.MaxChat ? text[..Protocol.MaxChat] : text);
        foreach (var id in _world.GuildOnline(guildId)) if (id != except) SendTo(id, msg);
    }

    /// <summary>Resposta a quem pediu: a janela atual com a mensagem (o jogo mostra como aviso).</summary>
    void Reply(int id, string message)
    {
        var g = MyGuild(id);
        SendTo(id, g == null ? GuildView.None(message) : ViewFor(id, g, message));
    }

    /// <summary>Chama a API fora do laço e volta ao laço com a resposta (falha de rede vira uma resposta com o motivo).</summary>
    void CallGuild(GuildRequest req, Action<GuildReply> then) =>
        _ = Task.Run(async () =>
        {
            GuildReply r;
            try { r = await _api.GuildAsync(req); }
            catch (Exception ex) { Log("API (guilda): " + ex.Message); r = new GuildReply(false, "Servidor da conta fora do ar. Tente de novo em instantes.", null); }
            _main.Enqueue(() => then(r));
        });

    /// <summary>
    /// Linha do histórico para a janela: "segundos-unix TAB texto". Itens vão como {item:tabela:linha} (o servidor não tem as
    /// tabelas do jogo; o cliente troca pelo nome).
    /// </summary>
    public static string FormatLog(GuildLogData l)
    {
        string item = l.Table != null && l.Row != null ? $"{{item:{l.Table}:{l.Row}}}" : "";
        string what = l.Kind switch
        {
            "create" => $"{l.Name} criou a guilda.",
            "join" => $"{l.Name} entrou na guilda.",
            "leave" => $"{l.Name} saiu da guilda.",
            "kick" => $"{l.Name} tirou {l.Text} da guilda.",
            "rank" => $"{l.Name} pôs {l.Text} como {RankName(l.Count)}.",
            "master" => $"{l.Name} passou o cargo de Mestre para {l.Text}.",
            "notice" => string.IsNullOrEmpty(l.Text) ? $"{l.Name} apagou o aviso." : $"{l.Name} mudou o aviso.",
            "level" => $"A guilda subiu para o nível {l.Count}!",
            "deposit" when l.Bit > 0 => $"{l.Name} guardou {l.Bit} BIT no baú.",
            "deposit" => $"{l.Name} guardou {(l.Count > 1 ? l.Count + "x " : "")}{item} no baú.",
            "withdraw" when l.Bit > 0 => $"{l.Name} tirou {l.Bit} BIT do baú.",
            "withdraw" => $"{l.Name} tirou {(l.Count > 1 ? l.Count + "x " : "")}{item} do baú.",
            _ => $"{l.Name}: {l.Kind}",
        };
        long at = l.At == DateTime.MinValue ? 0 : new DateTimeOffset(DateTime.SpecifyKind(l.At, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return $"{at}\t{what}";
    }

    public static string RankName(int rank) => rank switch { 1 => "Mestre", 2 => "Sub-mestre", 3 => "Membro", _ => "Novato" };
}
