using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>
/// Bloco de sequência do jogador por arma. FATO (beWeaponInfo → beWeaponSeqDataList dos w_*.igb, ndmw #899..#972): cada arma lista
/// 21 pares sequência → estilo (o LABEL do bloco nas sequências do jogador). Ex.: w_sword01 usa "slash" em tudo; w_axe01 usa "axe"
/// só no SQ_ATCK1 e no SQ_ATCK2 e "slash" no resto. Estilos vistos: slash, dual, arm, gun, axe, dagger, maul, saber.
/// HIPÓTESE: sequência fora da lista usa o bloco do SQ_WAIT da arma.
/// </summary>
public sealed class WeaponStyles(IReadOnlyDictionary<string, string> map)
{
    public const string Default = "slash";
    public IReadOnlyDictionary<string, string> Map { get; } = map;

    public string Of(string sequence) =>
        Map.TryGetValue(sequence, out var s) ? s : Map.TryGetValue("SQ_WAIT", out var w) ? w : Default;

    /// <summary>Lê o estilos.json (gerado na pasta de trabalho: {"entrada": n, "estilos": {"SQ_ATCK1": "slash", …}}).</summary>
    public static WeaponStyles FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var map = new Dictionary<string, string>();
        if (doc.RootElement.TryGetProperty("estilos", out var e))
            foreach (var p in e.EnumerateObject()) map[p.Name] = p.Value.GetString() ?? Default;
        return new WeaponStyles(map);
    }

    public static WeaponStyles Fallback { get; } = new(new Dictionary<string, string>());
}
