using System.Net.Http.Json;
using System.Text.Json;
using DW4.Net;

namespace DW4.Server;

/// <summary>Conferência do jogador na DMO.API (endpoints api/dw4/*, só localhost e com a chave de serviço).</summary>
public interface IAccountApi
{
    /// <summary>Resgata o ticket do launcher; null se não vale.</summary>
    Task<Identity?> RedeemAsync(string ticket, string ip);
    /// <summary>Heartbeat de presença ("no DW4" para os amigos). Devolve a party atual (id) ou null.</summary>
    Task<string?> PresenceAsync(long accountId, bool online);
    /// <summary>Menu do F (api/dw4/social): a ação em nome de <paramref name="me"/> sobre <paramref name="target"/>.</summary>
    Task<SocialReply> SocialAsync(Identity me, string action, Identity target, string inviteId);
}

/// <summary>Resposta de api/dw4/social já do jeito do <see cref="InteractResult"/>.</summary>
public sealed record SocialReply(bool Ok, string Message, string Relation = "", bool HasParty = false, bool AmLeader = false,
    bool TargetInMyParty = false, int PartyOccupied = 0, bool TargetInOtherParty = false, string InviteId = "", bool Accepted = false);

public sealed class DmoApi(Uri baseUri, string serviceKey) : IAccountApi
{
    readonly HttpClient _http = new() { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(10), DefaultRequestHeaders = { { "X-Service-Key", serviceKey } } };

    public async Task<Identity?> RedeemAsync(string ticket, string ip)
    {
        using var resp = await _http.PostAsJsonAsync("api/dw4/ticket/redeem", new { ticket, ip });
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"api/dw4/ticket/redeem respondeu {(int)resp.StatusCode}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        if (!r.TryGetProperty("active", out var a) || !a.GetBoolean()) return null;
        string tamer = r.TryGetProperty("tamer", out var t) && t.ValueKind == JsonValueKind.Object ? t.GetProperty("name").GetString() ?? "" : "";
        if (tamer.Length == 0) tamer = r.GetProperty("username").GetString() ?? "?";
        return new Identity(r.GetProperty("accountId").GetInt64(), tamer, PartyId(r));
    }

    public async Task<string?> PresenceAsync(long accountId, bool online)
    {
        using var resp = await _http.PostAsJsonAsync("api/dw4/presence", new { accountId, online });
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"api/dw4/presence respondeu {(int)resp.StatusCode}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return PartyId(doc.RootElement);
    }

    public async Task<SocialReply> SocialAsync(Identity me, string action, Identity target, string inviteId)
    {
        using var resp = await _http.PostAsJsonAsync("api/dw4/social",
            new { accountId = me.AccountId, action, name = target.Tamer, targetAccountId = target.AccountId, inviteId });
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"api/dw4/social respondeu {(int)resp.StatusCode}");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var r = doc.RootElement;
        bool ok = r.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        string message = r.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
        if (!ok || !r.TryGetProperty("data", out var d) || d.ValueKind != JsonValueKind.Object) return new SocialReply(ok, message);
        if (action == "relation")
        {
            string relation = d.TryGetProperty("relation", out var rel) ? rel.GetString() ?? "none" : "none";
            bool other = d.TryGetProperty("targetInParty", out var tip) && tip.ValueKind == JsonValueKind.True;
            if (!d.TryGetProperty("myParty", out var party) || party.ValueKind != JsonValueKind.Object)
                return new SocialReply(true, message, relation, TargetInOtherParty: other);
            var members = party.TryGetProperty("members", out var ms) && ms.ValueKind == JsonValueKind.Array
                ? ms.EnumerateArray().Select(x => x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").ToList() : [];
            int pending = party.TryGetProperty("pendingInvites", out var pi) && pi.ValueKind == JsonValueKind.Array ? pi.GetArrayLength() : 0;
            string leader = party.TryGetProperty("leader", out var l) ? l.GetString() ?? "" : "";
            var cmp = StringComparer.OrdinalIgnoreCase;
            return new SocialReply(true, message, relation, true, cmp.Equals(leader, me.Tamer), members.Contains(target.Tamer, cmp),
                members.Count + pending, other);
        }
        return new SocialReply(true, message,
            InviteId: d.TryGetProperty("inviteId", out var iv) && iv.ValueKind == JsonValueKind.String ? iv.GetString() ?? "" : "",
            Accepted: d.TryGetProperty("accepted", out var ac) && ac.ValueKind == JsonValueKind.True);
    }

    static string? PartyId(JsonElement r) =>
        r.TryGetProperty("party", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("id", out var id) ? id.GetString() : null;
}

/// <summary>
/// Teste local sem a DMO.API (DW4_DEV=1): o ticket "dev:Nome" vira a conta Nome. Amizade e party ficam em memória enquanto o
/// servidor roda: pedido de amizade fica pendente até o aceite (o menu do F só libera o convite de grupo para amigos); convidar e
/// aceitar juntam os dois na party, até 4; sair do jogo tira da party. Para testar o quadro e o combate em grupo. Nunca ligar em
/// produção.
/// </summary>
public sealed class DevApi : IAccountApi
{
    readonly object _lock = new();
    readonly Dictionary<long, string> _partyOf = [];
    readonly Dictionary<string, long> _leaderOf = [];
    readonly HashSet<(long From, long To)> _requests = [];
    readonly HashSet<(long, long)> _friends = [];
    int _next;

    static (long, long) Pair(Identity a, Identity b) => a.AccountId < b.AccountId ? (a.AccountId, b.AccountId) : (b.AccountId, a.AccountId);

    public Task<Identity?> RedeemAsync(string ticket, string ip) =>
        Task.FromResult(ticket.StartsWith("dev:", StringComparison.Ordinal) && ticket.Length > 4
            ? new Identity((long)(uint)ticket[4..].ToLowerInvariant().GetHashCode() + 1, ticket[4..], null) : null);

    public Task<string?> PresenceAsync(long accountId, bool online)
    {
        lock (_lock)
        {
            if (!online) Leave(accountId);
            return Task.FromResult(_partyOf.GetValueOrDefault(accountId));
        }
    }

    public Task<SocialReply> SocialAsync(Identity me, string action, Identity target, string inviteId)
    {
        lock (_lock)
        {
            var mine = _partyOf.GetValueOrDefault(me.AccountId);
            var theirs = _partyOf.GetValueOrDefault(target.AccountId);
            switch (action)
            {
                case "party-invite":
                    if (theirs != null) return Task.FromResult(new SocialReply(false, "Esse jogador já está num grupo."));
                    if (mine != null && _leaderOf[mine] != me.AccountId) return Task.FromResult(new SocialReply(false, "Só o líder convida."));
                    if (mine != null && _partyOf.Values.Count(v => v == mine) >= 4) return Task.FromResult(new SocialReply(false, "O grupo está cheio."));
                    return Task.FromResult(new SocialReply(true, "", InviteId: $"dev-{target.AccountId}"));
                case "party-accept":
                    // Quem aceita é "me"; quem convidou é "target".
                    if (mine != null) return Task.FromResult(new SocialReply(false, "Você já está num grupo."));
                    if (theirs == null)
                    {
                        theirs = $"dev-party-{++_next}";
                        _partyOf[target.AccountId] = theirs;
                        _leaderOf[theirs] = target.AccountId;
                    }
                    _partyOf[me.AccountId] = theirs;
                    return Task.FromResult(new SocialReply(true, "", Accepted: true));
                case "friend-request":
                    if (_friends.Contains(Pair(me, target))) return Task.FromResult(new SocialReply(false, $"{target.Tamer} já é seu amigo."));
                    // O outro já tinha pedido: vira amizade na hora (como no launcher), sem pergunta para ele.
                    if (_requests.Remove((target.AccountId, me.AccountId)))
                    {
                        _friends.Add(Pair(me, target));
                        return Task.FromResult(new SocialReply(true, "", Accepted: true));
                    }
                    _requests.Add((me.AccountId, target.AccountId));
                    return Task.FromResult(new SocialReply(true, ""));
                case "friend-accept":
                    // Quem aceita é "me"; quem pediu é "target".
                    if (!_requests.Remove((target.AccountId, me.AccountId)) && !_friends.Contains(Pair(me, target)))
                        return Task.FromResult(new SocialReply(false, "Esse pedido de amizade não existe mais."));
                    _friends.Add(Pair(me, target));
                    return Task.FromResult(new SocialReply(true, "", Accepted: true));
                case "friend-decline":
                    _requests.Remove((target.AccountId, me.AccountId));
                    return Task.FromResult(new SocialReply(true, ""));
                case "relation":
                    string relation = _friends.Contains(Pair(me, target)) ? "friend"
                        : _requests.Contains((me.AccountId, target.AccountId)) ? "requestSent"
                        : _requests.Contains((target.AccountId, me.AccountId)) ? "requestReceived" : "none";
                    return Task.FromResult(new SocialReply(true, "", relation, mine != null, mine != null && _leaderOf[mine] == me.AccountId,
                        mine != null && mine == theirs, mine == null ? 0 : _partyOf.Values.Count(v => v == mine), theirs != null && theirs != mine));
                default:
                    return Task.FromResult(new SocialReply(true, ""));
            }
        }
    }

    // Saiu do jogo: sai da party; sobrou 1, a party acaba (regra do launcher). O líder que sai passa para o próximo.
    void Leave(long accountId)
    {
        if (!_partyOf.Remove(accountId, out var party)) return;
        var left = _partyOf.Where(kv => kv.Value == party).Select(kv => kv.Key).ToList();
        if (left.Count <= 1) { foreach (var a in left) _partyOf.Remove(a); _leaderOf.Remove(party); }
        else if (_leaderOf[party] == accountId) _leaderOf[party] = left[0];
    }
}
