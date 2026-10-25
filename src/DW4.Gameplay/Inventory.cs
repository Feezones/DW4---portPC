namespace DW4.Gameplay;

/// <summary>Um item de uma tabela do jogo: nome da tabela (weapon, armor1, armor2, abChip, conItem, insItem, impItem) e linha.</summary>
public readonly record struct ItemRef(string Table, int Row);

/// <summary>
/// Inventário de um jogador (GAMERAM/GETPLAYER), lido por desmontagem estática em 2026-10-24
/// (docs/gameplay/fase4-levantamento-itens.md). FATO: DEVICEFOLDER guarda armas, armaduras e chips até 36 itens
/// (0x803AC294); DISKFOLDER conta os discos (conItem) por tipo, até 9 (0x803AC2B4); itens importantes (impItem) ligam um
/// bit da FLAGFOLDER; o item cheio não é pego. Um inventário por jogador, para a party online.
/// </summary>
public sealed class Inventory
{
    public const int DeviceMax = 36, DiskMax = 9;

    readonly List<ItemRef> _devices = [];
    readonly Dictionary<int, int> _disks = [];
    readonly HashSet<int> _flags = [];

    public IReadOnlyList<ItemRef> Devices => _devices;
    public int Bit { get; set; }
    public int Disks(int conItemRow) => _disks.TryGetValue(conItemRow, out var n) ? n : 0;
    public bool HasFlag(int impItemRow) => _flags.Contains(impItemRow);

    /// <summary>
    /// Guarda um item pego (0x803AC1A0). Devolve false quando a pasta está cheia (o item fica no chão). insItem não é
    /// guardado: é efeito imediato (ver <see cref="Instant"/>).
    /// </summary>
    public bool Add(ItemRef item)
    {
        switch (item.Table)
        {
            case "weapon" or "armor1" or "armor2" or "abChip":
                if (_devices.Count >= DeviceMax) return false;
                _devices.Add(item);
                return true;
            case "conItem":
                if (Disks(item.Row) >= DiskMax) return false;
                _disks[item.Row] = Disks(item.Row) + 1;
                return true;
            case "impItem":
                _flags.Add(item.Row);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Item guardado? (DEVICEFOLDER para armas, armaduras e chips; DISKFOLDER &gt; 0; FLAGFOLDER para impItem.)</summary>
    public bool Has(ItemRef item) => item.Table switch
    {
        "conItem" => Disks(item.Row) > 0,
        "impItem" => _flags.Contains(item.Row),
        _ => _devices.Contains(item),
    };

    /// <summary>Tira um item (o primeiro igual na pasta; um disco; o bit do impItem). false se não havia.</summary>
    public bool Remove(ItemRef item)
    {
        switch (item.Table)
        {
            case "conItem":
                if (Disks(item.Row) <= 0) return false;
                _disks[item.Row]--;
                return true;
            case "impItem":
                return _flags.Remove(item.Row);
            default:
                return _devices.Remove(item);
        }
    }

    /// <summary>
    /// Disco usado no painel (0x80349B0C): exige quantidade &gt; 0, gasta 1 e devolve a cura de HP e MP da conItem (o jogo
    /// soma com limite no máximo, como o ADDHP/ADDMP em 0x8036F220). Null sem disco. A recarga do slot não está aqui
    /// (unidade ainda não confirmada).
    /// </summary>
    public (int Hp, int Mp)? UseDisk(GameTables db, int conItemRow)
    {
        if (Disks(conItemRow) <= 0) return null;
        _disks[conItemRow]--;
        var t = db["conItem"];
        return (t.Int(conItemRow, "HP"), t.Int(conItemRow, "MP"));
    }

    /// <summary>
    /// insItem pego: efeito imediato. Linha 0 = BIT (a quantia vem do gerador; regra ainda não lida); as outras levam UP no
    /// atributo NOURYOKU. HIPÓTESE: os plugs somam no UPCNT permanente que o RECALC lê.
    /// </summary>
    public static (string Nouryoku, int Up) Instant(GameTables db, int insItemRow)
    {
        var t = db["insItem"];
        return (t.Text(insItemRow, "NOURYOKU"), t.Int(insItemRow, "UP"));
    }
}
