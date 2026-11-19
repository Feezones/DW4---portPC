using System.Text.Json.Nodes;

namespace DW4.Gameplay.Online;

/// <summary>Resultado da troca nos saves: os dois JSON novos e o número de trocas de cada vaga, ou o motivo da recusa.</summary>
public sealed record TradeCommit(string? Error, string JsonA = "", string JsonB = "", int SeqA = 0, int SeqB = 0, string NameA = "", string NameB = "");

/// <summary>
/// A troca de verdade, feita pelo servidor do DW4 nos saves guardados na conta (saves 100% online desde 2026-11-04), e não no que
/// o cliente diz ter. Só os campos do inventário mudam (Bit, Devices, Disks) e o contador TradeSeq sobe; o resto do JSON fica
/// como está, mesmo campos que este servidor ainda não conhece. O TradeSeq impede que uma gravação velha do cliente (feita antes
/// da troca e chegando depois) desfaça a troca e duplique itens: o servidor recusa save com TradeSeq menor que o da troca.
/// </summary>
public static class TradeLedger
{
    public const string SeqField = "TradeSeq";

    public static TradeCommit Commit(string jsonA, TradeOffer offerA, string jsonB, TradeOffer offerB)
    {
        JsonObject a, b;
        try
        {
            a = JsonNode.Parse(jsonA) as JsonObject ?? throw new FormatException();
            b = JsonNode.Parse(jsonB) as JsonObject ?? throw new FormatException();
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { return new TradeCommit("Save inválido no servidor."); }
        var (invA, eqA) = Read(a);
        var (invB, eqB) = Read(b);
        var why = TradeSession.Check(offerA, invA, eqA, offerB) ?? TradeSession.Check(offerB, invB, eqB, offerA);
        if (why != null) return new TradeCommit(why);
        TradeSession.Move(offerA, offerB, invA);
        TradeSession.Move(offerB, offerA, invB);
        int seqA = Write(a, invA), seqB = Write(b, invB);
        return new TradeCommit(null, a.ToJsonString(), b.ToJsonString(), seqA, seqB, Str(a, "Name"), Str(b, "Name"));
    }

    /// <summary>
    /// Um lado só (baú da guilda, 2026-11-07): sai <paramref name="give"/> e entra <paramref name="get"/> no save, com as mesmas
    /// conferências da troca, e o TradeSeq sobe (gravação velha do cliente não desfaz). JsonA/SeqA/NameA são o resultado.
    /// </summary>
    public static TradeCommit Apply(string json, TradeOffer give, TradeOffer get)
    {
        JsonObject a;
        try { a = JsonNode.Parse(json) as JsonObject ?? throw new FormatException(); }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { return new TradeCommit("Save inválido no servidor."); }
        var (inv, eq) = Read(a);
        if (TradeSession.Check(give, inv, eq, get) is { } why) return new TradeCommit(why);
        TradeSession.Move(give, get, inv);
        int seq = Write(a, inv);
        return new TradeCommit(null, a.ToJsonString(), SeqA: seq, NameA: Str(a, "Name"));
    }

    /// <summary>TradeSeq de um save (0 quando não tem: saves de antes da troca).</summary>
    public static int SeqOf(string json)
    {
        try { return JsonNode.Parse(json) is JsonObject o ? Int(o, SeqField, 0) : 0; }
        catch (System.Text.Json.JsonException) { return 0; }
    }

    static (Inventory, Equipment?) Read(JsonObject o)
    {
        var inv = new Inventory { Bit = Math.Clamp(Int(o, "Bit", 0), 0, Inventory.BitMax) };
        if (o["Devices"] is JsonArray devs)
            foreach (var d in devs.OfType<JsonObject>())
                inv.Add(new ItemRef(Str(d, "Table"), Int(d, "Row", -1)));
        if (o["Disks"] is JsonObject disks)
            foreach (var (k, v) in disks)
                if (int.TryParse(k, out var row) && v is JsonValue jv && jv.TryGetValue(out int n))
                    for (int i = 0; i < Math.Min(n, Inventory.DiskMax); i++) inv.Add(new ItemRef("conItem", row));
        int weapon = Int(o, "WeaponRow", -1);
        Equipment? eq = weapon < 0 ? null : new Equipment(weapon, Int(o, "Armor1Row", -1), Int(o, "Armor2Row", -1))
        {
            WeaponChips = Ints(o, "WeaponChips"), ArmorChips = Ints(o, "ArmorChips"), ReserveWeapons = Ints(o, "ReserveWeapons"),
        };
        return (inv, eq);
    }

    static int Write(JsonObject o, Inventory inv)
    {
        o["Bit"] = inv.Bit;
        var devs = new JsonArray();
        foreach (var d in inv.Devices) devs.Add(new JsonObject { ["Table"] = d.Table, ["Row"] = d.Row });
        o["Devices"] = devs;
        var disks = new JsonObject();
        foreach (var (row, n) in inv.DiskCounts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key)) disks[row.ToString()] = n;
        o["Disks"] = disks;
        int seq = Int(o, SeqField, 0) + 1;
        o[SeqField] = seq;
        return seq;
    }

    static int Int(JsonObject o, string key, int fallback) => o[key] is JsonValue v && v.TryGetValue(out int n) ? n : fallback;
    static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
    static List<int> Ints(JsonObject o, string key) =>
        o[key] is JsonArray arr ? arr.Select(x => x is JsonValue v && v.TryGetValue(out int n) ? n : -1).ToList() : [];
}
