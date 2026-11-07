using System.Collections.Concurrent;
using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using LiteNetLib;

namespace DW4.Server;

/// <summary>
/// Troca de itens entre jogadores (protocolo 6, pedido do responsável em 2026-11-07). O servidor guarda a troca
/// (<see cref="TradeSession"/>), repassa as ofertas e, quando os dois confirmam, faz a troca nos saves guardados na conta (não no
/// que o cliente diz ter): lê as duas vagas, confere e troca (<see cref="TradeLedger"/>), grava as duas e só então avisa os dois.
/// A troca entra na fila de gravação das duas contas (depois do save que cada cliente manda ao confirmar). Se a segunda gravação
/// falha, a primeira volta ao que era. Gravações velhas que chegam depois são recusadas pelo TradeSeq (<see cref="OnSave"/>).
/// Só entre jogadores na mesma fase; sair de perto, trocar de fase ou cair cancela (antes de gravar).
/// </summary>
public sealed partial class GameServer
{
    public const string StaleTradeSave = "Este personagem fez uma troca que ainda não chegou neste aparelho. Volte ao menu e entre de novo.";
    static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);

    readonly Dictionary<int, TradeSession> _trades = [];
    readonly Dictionary<int, int> _tradeSlot = [];
    // Pedidos de troca esperando resposta: quem pediu → (alvo, vaga de quem pediu, quando).
    readonly Dictionary<int, (int Target, int Slot, DateTime At)> _asks = [];
    readonly Dictionary<int, (long Second, int Count)> _tradeRate = [];
    // Número de trocas por (conta, vaga) depois da última troca feita: save com número menor é velho.
    readonly ConcurrentDictionary<(long Account, int Slot), int> _tradeSeqs = new();
    DateTime _nextTradeCheck;

    void OnTrade(NetPeer p, TradeCmd x)
    {
        long sec = DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond;
        var (s0, n) = _tradeRate.TryGetValue(p.Id, out var v) && v.Second == sec ? v : (sec, 0);
        _tradeRate[p.Id] = (s0, n + 1);
        if (n >= 20) return;
        int me = p.Id;
        _trades.TryGetValue(me, out var t);
        switch (x.Action)
        {
            case "ask":
                if (t != null) { Notice(me, x.TargetId, "Você já está numa troca."); return; }
                if (!CanTrade(me, x.TargetId, out var why)) { Notice(me, x.TargetId, why); return; }
                if (_trades.ContainsKey(x.TargetId)) { Notice(me, x.TargetId, $"{_world.Name(x.TargetId)} já está numa troca."); return; }
                _asks[me] = (x.TargetId, x.Slot, DateTime.UtcNow);
                SendTo(x.TargetId, Empty(me, "ask"));
                SendTo(me, Empty(x.TargetId, "asked"));
                Log($"[{me}] pediu troca a [{x.TargetId}]");
                return;
            case "accept":
            {
                if (!_asks.TryGetValue(x.TargetId, out var ask) || ask.Target != me || DateTime.UtcNow - ask.At > AskTimeout)
                { Notice(me, x.TargetId, "O pedido de troca não vale mais."); return; }
                _asks.Remove(x.TargetId);
                if (t != null || _trades.ContainsKey(x.TargetId)) { Notice(me, x.TargetId, "Um de vocês já está numa troca."); return; }
                if (!CanTrade(me, x.TargetId, out var why2)) { Notice(me, x.TargetId, why2); Notice(x.TargetId, me, why2); return; }
                var s = new TradeSession(x.TargetId, me);
                _trades[me] = _trades[x.TargetId] = s;
                _tradeSlot[x.TargetId] = ask.Slot;
                _tradeSlot[me] = x.Slot;
                Log($"[{x.TargetId}] e [{me}] abriram uma troca");
                Both(s);
                return;
            }
            case "decline":
                if (_asks.TryGetValue(x.TargetId, out var a2) && a2.Target == me)
                {
                    _asks.Remove(x.TargetId);
                    SendTo(x.TargetId, Empty(me, "cancelled", $"{_world.Name(me)} recusou a troca."));
                }
                return;
            case "cancel":
                if (_asks.Remove(me, out var mine)) SendTo(mine.Target, Empty(me, "cancelled", $"{_world.Name(me)} desistiu da troca."));
                if (t != null) CancelTrade(t, $"{_world.Name(me)} cancelou a troca.");
                return;
        }
        if (t == null) { SendTo(me, Empty(x.TargetId, "cancelled", "A troca já acabou.")); return; }
        SocialResult r = x.Action switch
        {
            "add" => t.AddItem(me, new ItemRef(x.Table, x.Row)),
            "remove" => t.RemoveItem(me, new ItemRef(x.Table, x.Row)),
            "bit" => t.SetBit(me, x.Bit),
            "lock" => t.Lock(me),
            "confirm" => t.Confirm(me),
            _ => SocialResult.Fail("Acao", "Ação de troca desconhecida."),
        };
        if (!r.Ok) { SendTo(me, View(t, me, r.Message)); return; }
        Both(t);
        if (t.State == TradeState.Committing) Commit(t);
    }

    bool CanTrade(int me, int other, out string why)
    {
        why = "";
        if (other == me || _world.Who(other) == null || !_world.SameInstance(me, other)) { why = "Esse jogador não está mais perto."; return false; }
        return true;
    }

    void Commit(TradeSession t)
    {
        var (a, b) = (t.A, t.B);
        var whoA = _world.Who(a)!; var whoB = _world.Who(b)!;
        int slotA = _tradeSlot[a], slotB = _tradeSlot[b];
        var offerA = Copy(t.OfferA); var offerB = Copy(t.OfferB);
        Log($"[{a}] e [{b}] confirmaram a troca; gravando");
        Chain([whoA.AccountId, whoB.AccountId], async () =>
        {
            string? error;
            TradeCommit? done = null;
            try { (error, done) = await CommitAsync(whoA.AccountId, slotA, offerA, whoB.AccountId, slotB, offerB); }
            catch (Exception ex) { Log($"API (troca): {ex.Message}"); error = "Servidor da conta fora do ar. A troca não foi feita."; }
            _main.Enqueue(() => AfterCommit(t, error, done));
        });
    }

    async Task<(string?, TradeCommit?)> CommitAsync(long accA, int slotA, TradeOffer offerA, long accB, int slotB, TradeOffer offerB)
    {
        var listA = await _api.ListCharactersAsync(accA);
        var listB = await _api.ListCharactersAsync(accB);
        var jsonA = listA.FirstOrDefault(c => c.Slot == slotA).Json;
        var jsonB = listB.FirstOrDefault(c => c.Slot == slotB).Json;
        if (jsonA == null || jsonB == null) return ("Personagem não encontrado no servidor. A troca não foi feita.", null);
        var c = TradeLedger.Commit(jsonA, offerA, jsonB, offerB);
        if (c.Error != null) return (c.Error + " A troca não foi feita.", null);
        await _api.SaveCharacterAsync(accA, slotA, Trim(c.NameA), c.JsonA);
        try { await _api.SaveCharacterAsync(accB, slotB, Trim(c.NameB), c.JsonB); }
        catch
        {
            // Tudo ou nada: o primeiro volta ao que era.
            await _api.SaveCharacterAsync(accA, slotA, Trim(c.NameA), jsonA);
            throw;
        }
        _tradeSeqs[(accA, slotA)] = c.SeqA;
        _tradeSeqs[(accB, slotB)] = c.SeqB;
        return (null, c);
    }

    static string Trim(string name) => name.Length > 32 ? name[..32] : name;

    void AfterCommit(TradeSession t, string? error, TradeCommit? done)
    {
        t.Finish(error == null);
        foreach (var id in new[] { t.A, t.B })
        {
            if (_trades.TryGetValue(id, out var cur) && cur == t) { _trades.Remove(id); _tradeSlot.Remove(id); }
            var view = error != null ? View(t, id, error) with { State = "cancelled" }
                : View(t, id, "") with { State = "done", Seq = id == t.A ? done!.SeqA : done!.SeqB };
            SendTo(id, view);
        }
        Log(error == null ? $"[{t.A}] e [{t.B}] trocaram ({t.OfferA.Items.Count}+{t.OfferA.Bit} BIT por {t.OfferB.Items.Count}+{t.OfferB.Bit} BIT)"
            : $"[{t.A}] e [{t.B}]: troca recusada ({error})");
    }

    void CancelTrade(TradeSession t, string why)
    {
        if (!t.Cancel()) return; // já gravando: termina sozinha
        foreach (var id in new[] { t.A, t.B })
        {
            _trades.Remove(id); _tradeSlot.Remove(id);
            SendTo(id, View(t, id, why) with { State = "cancelled" });
        }
    }

    /// <summary>Jogador saiu do servidor: cancela a troca dele e os pedidos dele ou para ele.</summary>
    void TradeGone(int id)
    {
        _tradeRate.Remove(id);
        string name = _world.Name(id);
        _asks.Remove(id);
        foreach (var (from, ask) in _asks.Where(kv => kv.Value.Target == id).ToList()) _asks.Remove(from);
        if (_trades.TryGetValue(id, out var t)) CancelTrade(t, $"{name} saiu do jogo. A troca foi cancelada.");
    }

    // A cada meio segundo: troca entre quem não está mais na mesma fase é cancelada; pedido sem resposta vence.
    void TickTrades(DateTime now)
    {
        if (now < _nextTradeCheck) return;
        _nextTradeCheck = now.AddMilliseconds(500);
        foreach (var t in _trades.Values.Distinct().ToList())
            if (t.State == TradeState.Open && !_world.SameInstance(t.A, t.B))
                CancelTrade(t, "A troca foi cancelada: vocês não estão mais na mesma fase.");
        foreach (var (from, ask) in _asks.Where(kv => now - kv.Value.At > AskTimeout).ToList())
        {
            _asks.Remove(from);
            SendTo(from, Empty(ask.Target, "cancelled", $"{_world.Name(ask.Target)} não respondeu ao pedido de troca."));
        }
    }

    static TradeOffer Copy(TradeOffer o)
    {
        var c = new TradeOffer { Bit = o.Bit, Locked = o.Locked, Confirmed = o.Confirmed };
        c.Items.AddRange(o.Items);
        return c;
    }

    static TradeItem[] Items(TradeOffer o) => o.Items.Select(i => new TradeItem(i.Table, i.Row)).ToArray();

    TradeView View(TradeSession t, int me, string message)
    {
        int other = t.OtherOf(me);
        var mine = t.Of(me)!; var theirs = t.Of(other)!;
        string state = t.State switch { TradeState.Open => "open", TradeState.Committing => "committing", TradeState.Done => "done", _ => "cancelled" };
        return new TradeView(other, _world.Name(other), state, Items(mine), mine.Bit, mine.Locked, mine.Confirmed,
            Items(theirs), theirs.Bit, theirs.Locked, theirs.Confirmed, message);
    }

    TradeView Empty(int other, string state, string message = "") => new(other, _world.Name(other), state, [], 0, false, false, [], 0, false, false, message);

    void Notice(int me, int other, string why) => SendTo(me, Empty(other, "cancelled", why));

    void Both(TradeSession t) { SendTo(t.A, View(t, t.A, "")); SendTo(t.B, View(t, t.B, "")); }

    void SendTo(int id, object msg) { if (_peers.TryGetValue(id, out var p)) Send(p, msg, true); }
}
