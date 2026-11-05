using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>Entrada de uma lista de drop do gerador: item, _rank (uso desconhecido) e _rate (chance em 1/10000).</summary>
public sealed record DropEntry(ItemRef Item, int Rank, int Rate);

/// <summary>
/// Drops dos inimigos, lidos por desmontagem estática em 2026-10-24 (docs/gameplay/fase4-levantamento-itens.md).
/// FATO: sorteio em 0x802FCC48 com o MT do combate; quantia de BIT em 0x803B1408; coleta a menos de 64 unidades
/// (0x8045C4D0). As listas vêm dos geradores da fase (<c>dw4 igb-drops</c>), fora do repositório.
/// </summary>
public static class Drops
{
    public const float PickupRadius = 64f;

    // FATO (main.dol 0x804DE0A8, pares tabela → modelo do baú).
    static readonly Dictionary<string, string> ChestOfTable = new()
    {
        ["impItem"] = "g_000key", ["weapon"] = "g_000weapon", ["armor1"] = "g_000armor", ["armor2"] = "g_000armor", ["abChip"] = "g_000access",
    };

    /// <summary>
    /// Modelo do baú do item no chão. FATO: impItem, weapon, armor1/2 e abChip pela tabela do main.dol em 0x804DE0A8; conItem e
    /// insItem pela coluna MODEL da linha (g_000item, g_000boost1, g_000penx, g_000bit, g_000xdata1, g_000xdata2). g_000item quando
    /// não sabe (tabela desconhecida ou sem MODEL).
    /// </summary>
    public static string ChestModel(GameTables? db, ItemRef item)
    {
        if (ChestOfTable.TryGetValue(item.Table, out var m)) return m;
        if (db != null && db.Contains(item.Table) && db[item.Table].Raw(item.Row, "MODEL") is { Length: > 0 } col
            && col.StartsWith("g_000", StringComparison.Ordinal)) return col;
        return "g_000item";
    }

    /// <summary>
    /// Sorteio de um inimigo morto: rnd = MT % 10000; percorre a lista fazendo rnd −= _rate e fica com a primeira entrada em
    /// que rnd ≤ 0. Null quando a soma não alcança (nada cai). Com <paramref name="reroll"/> (gerador+0x24, significado
    /// HIPÓTESE), sorteia de novo até cair algo.
    /// </summary>
    public static DropEntry? Roll(IReadOnlyList<DropEntry> list, IGameRandom rng, bool reroll = false)
    {
        if (list.Count == 0 || list.Sum(e => e.Rate) <= 0) return null;
        while (true)
        {
            int rnd = (int)(rng.Next() % 10000);
            foreach (var e in list)
            {
                rnd -= e.Rate;
                if (rnd <= 0) return e;
            }
            if (!reroll) return null;
        }
    }

    // Faixas de BIT por nível (FATO, 0x803B1408..0x803B1618): até o nível, mínimo e máximo.
    static readonly (int Level, int Lo, int Hi)[] BitBands =
        [(18, 10, 55), (38, 55, 100), (59, 87, 195), (93, 162, 350), (107, 232, 520), (143, 340, 700), (202, 446, 860), (int.MaxValue, 566, 1160)];

    /// <summary>
    /// BIT pego (insItem 0): (lo + MT % (hi − lo))·(BITRITSU + 100)/100, com a faixa pelo nível. HIPÓTESE: nível e BITRITSU
    /// de quem pegou (o código lê o registro GETPLAYER1).
    /// </summary>
    public static int Bit(int level, int bitritsu, IGameRandom rng)
    {
        var (_, lo, hi) = BitBands.First(b => level <= b.Level);
        return (lo + (int)(rng.Next() % (uint)(hi - lo))) * (bitritsu + 100) / 100;
    }

    /// <summary>Lê o JSON de <c>dw4 igb-drops</c>: {"lists": {"nZ1nume1": [{"table", "row", "rank", "rate"}…]}}.</summary>
    public static Dictionary<string, List<DropEntry>> FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, List<DropEntry>>();
        foreach (var l in doc.RootElement.GetProperty("lists").EnumerateObject())
            result[l.Name] = l.Value.EnumerateArray().Select(e => new DropEntry(
                new ItemRef(e.GetProperty("table").GetString() ?? "", e.GetProperty("row").GetInt32()),
                e.GetProperty("rank").GetInt32(), e.GetProperty("rate").GetInt32())).ToList();
        return result;
    }
}
