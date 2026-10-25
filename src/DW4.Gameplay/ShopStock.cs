namespace DW4.Gameplay;

/// <summary>
/// Estoque das lojas, lido por desmontagem estática em 2026-10-25 (docs/gameplay/fase5-lojas.md). FATO (0x80351794): o
/// SUBWND responde GETSHOPDATA montando a lista pelo tipo da janela: 6 WPU → tabela shopWeapon, 7 APU → shopArmor, 8 CHIP →
/// shopChip, 9 JUNK → shopJunk (tabelas do banco da própria fase, ex. #829 da Central Area). A linha da tabela sai do
/// jogador 0 (LEVEL, DIGIMONNO) e do grupo; as colunas "00".."35" são linhas da tabela do item (−1 = vazio).
/// </summary>
public static class ShopStock
{
    /// <summary>Tipos de SUBWND (tabela de nomes em 0x804E16B0, índice = tipo).</summary>
    public const int Disk = 5, Weapon = 6, Armor = 7, Chip = 8, Junk = 9;

    // FATO (0x80351A2C..0x80351BAC): faixa do LEVEL para a shopWeapon: ≤ 9 → 0, ≤ 16 → 1, … ≤ 147 → 23, acima → 24.
    static readonly int[] WeaponBands = [9, 16, 21, 26, 31, 36, 41, 46, 51, 60, 67, 72, 77, 82, 87, 92, 97, 102, 107, 112, 117, 127, 137, 147];
    // FATO (0x80351CD4.., 0x803520DC..): shopArmor e shopChip continuam: ≤ 157 → 24, … ≤ 197 → 28, acima → 29.
    static readonly int[] ArmorChipBands = [.. WeaponBands, 157, 167, 177, 187, 197];
    // FATO (tabela de saltos 0x804E8024): base da shopWeapon por DIGIMONNO (0..15) com um jogador só.
    static readonly int[] WeaponBase = [0, 25, 50, 75, 25, 0, 100, 50, 125, 75, 100, 125, 25, 125, 50, 0];
    /// <summary>FATO (0x803519C0): com mais de um jogador com ISUSE, a base da shopWeapon é 150.</summary>
    public const int PartyWeaponBase = 150;
    // FATO (0x80352418..0x803524EC): shopJunk pela COMMON.COUNTER[1]: ≥ 1000 → 0, ≥ 6000 → 1, … ≥ 3 960 000 → 8; abaixo de 1000, nada.
    static readonly int[] JunkThresholds = [1000, 6000, 26000, 86000, 206000, 456000, 956000, 1956000, 3960000];

    /// <summary>Nome da tabela de estoque do tipo de janela; null para tipos sem estoque lido.</summary>
    public static string? Table(int kind) => kind switch
    {
        Weapon => "shopWeapon",
        Armor => "shopArmor",
        Chip => "shopChip",
        Junk => "shopJunk",
        // HIPÓTESE forte: a janela DISK (classe beNDMWShopCtrlDiskSell, campos _diskIdList, _cursor, _maxSell, _kazu, _tanka) lista
        // a shopDisk do banco da fase (1 linha; no #829: conItem 2, 1, 0, 5, 4, 3, 6, 7, 8, 9). O GETSHOPDATA (0x80351794) não
        // trata o tipo 5, e o código que enche o _diskIdList não foi lido.
        Disk => "shopDisk",
        _ => null,
    };

    /// <summary>Tabela do item que cada coluna aponta (shopJunk tem a sua coluna "NNkind").</summary>
    public static string? ItemTable(int kind) => kind switch
    {
        Weapon => "weapon",
        Armor => "armor1", // FATO (0x803B0DE8): tipo de item 1
        Chip => "abChip",   // FATO (0x803B1014): tipo de item 3
        Disk => "conItem",  // FATO (0x803B10A0): tipo de item 4
        _ => null,
    };

    /// <summary>
    /// Linha da tabela de estoque. <paramref name="level"/> e <paramref name="digimonNo"/> do jogador 0; <paramref name="players"/>
    /// = jogadores com ISUSE; <paramref name="counter1"/> = COMMON.COUNTER[1] (só a sucata). −1 = loja vazia.
    /// </summary>
    public static int Row(int kind, int level, int digimonNo, int players, int counter1) => kind switch
    {
        Weapon => (players > 1 ? PartyWeaponBase : digimonNo is >= 0 and < 16 ? WeaponBase[digimonNo] : 0) + Band(level, WeaponBands),
        Armor or Chip => Band(level, ArmorChipBands),
        Junk => JunkThresholds.Count(t => counter1 >= t) - 1,
        Disk => 0,
        _ => -1,
    };

    static int Band(int level, int[] limits)
    {
        for (int i = 0; i < limits.Length; i++) if (level <= limits[i]) return i;
        return limits.Length;
    }

    /// <summary>
    /// Itens da linha: 36 posições na ordem das colunas "00".."35" (3 páginas de 12), null onde a coluna é −1. FATO (0x80351BE8..
    /// 0x80351CCC): a lista recebe um item ou um vazio por coluna, sem tirar repetidos; a tela mostra "----" no vazio. A sucata usa o par "NNkind"/"NN"; o tipo
    /// segue a numeração do jogo (<see cref="KindTable"/>).
    /// </summary>
    public static List<ItemRef?> Items(int kind, Func<string, int> cell)
    {
        var list = new List<ItemRef?>();
        for (int c = 0; c < 36; c++)
        {
            string col = c.ToString("00");
            int row = cell(col);
            string? table = row < 0 ? null : kind == Junk ? KindTable(cell(col + "kind")) : ItemTable(kind);
            list.Add(table != null ? new ItemRef(table, row) : null);
        }
        return list;
    }

    /// <summary>Tipo de item do jogo → tabela (FATO: 0x803B0CB4 = 0 weapon, 0x803B0DE8 = 1 armor1, 0x803B0F20 = 2 armor2, 0x803B1014 = 3 abChip, 0x803B10A0 = 4 conItem).</summary>
    public static string? KindTable(int itemKind) => itemKind switch
    {
        0 => "weapon",
        1 => "armor1",
        2 => "armor2",
        3 => "abChip",
        4 => "conItem",
        _ => null,
    };
}

/// <summary>
/// Compra e venda nas lojas de equipamento. FATO (0x803A3604..0x803A3678): comprar faz BIT −= HANBAI e põe o item no
/// DEVICEFOLDER. FATO (0x803A0AF8..0x803A0B58): vender faz BIT += KAITORI, com teto de 9 999 999, e tira o item da pasta.
/// FATO (0x803A1338): só dá para vender item não equipado e com KAITORI ≥ 1.
/// HIPÓTESE (as condições da compra não foram lidas): não compra sem BIT suficiente nem com a pasta cheia (36).
/// </summary>
public static class ShopTrade
{
    public const int BitMax = Inventory.BitMax;

    public static bool CanBuy(Inventory inv, int hanbai) => hanbai >= 0 && inv.Bit >= hanbai && inv.Devices.Count < Inventory.DeviceMax;

    /// <summary>Disco (conItem) vai para a DISKFOLDER, até 9 por tipo (FATO 0x803AC2B4); o resto vai para o DEVICEFOLDER.</summary>
    public static bool CanBuy(Inventory inv, ItemRef item, int hanbai) => item.Table == "conItem"
        ? hanbai >= 0 && inv.Bit >= hanbai && inv.Disks(item.Row) < Inventory.DiskMax
        : CanBuy(inv, hanbai);

    public static bool Buy(Inventory inv, ItemRef item, int hanbai)
    {
        if (!CanBuy(inv, item, hanbai)) return false;
        inv.Bit -= hanbai;
        return inv.Add(item);
    }

    public static bool CanSell(int kaitori, bool equipped) => !equipped && kaitori >= 1;

    public static bool Sell(Inventory inv, ItemRef item, int kaitori, bool equipped)
    {
        if (!CanSell(kaitori, equipped) || !inv.Remove(item)) return false;
        inv.Bit = Math.Min(inv.Bit + kaitori, BitMax);
        return true;
    }
}
