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
}

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

    static string? PartyId(JsonElement r) =>
        r.TryGetProperty("party", out var p) && p.ValueKind == JsonValueKind.Object && p.TryGetProperty("id", out var id) ? id.GetString() : null;
}

/// <summary>
/// Teste local sem a DMO.API (DW4_DEV=1): o ticket "dev:Nome" vira a conta Nome, sem party. Nunca ligar em produção.
/// </summary>
public sealed class DevApi : IAccountApi
{
    public Task<Identity?> RedeemAsync(string ticket, string ip) =>
        Task.FromResult(ticket.StartsWith("dev:", StringComparison.Ordinal) && ticket.Length > 4
            ? new Identity((long)(uint)ticket[4..].ToLowerInvariant().GetHashCode() + 1, ticket[4..], null) : null);

    public Task<string?> PresenceAsync(long accountId, bool online) => Task.FromResult<string?>(null);
}
