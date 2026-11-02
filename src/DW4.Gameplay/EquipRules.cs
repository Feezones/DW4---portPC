namespace DW4.Gameplay;

/// <summary>Os três lugares da tela Equipment (textos mswkPRG0031..0033: Weapon Slot, Armor Slot, Sub Slot).</summary>
public enum EquipSlot { Weapon, Armor, Sub }

/// <summary>
/// Regras do menu de status (telas Equipment e Folder), separadas da interface para serem testadas.
/// FATO (textos do txt_status.lua, I&lt;29..112&gt;): Weapon Slot com 3 soquetes ("Select the socket", "Exchange Weapon Unit"),
/// Armor Slot (∋, armor1) e Sub Slot (∨, armor2); chips nos "Mods" ("Equip/Remove Ability Chip"); pastas Device, Key, Disk e BITS
/// com Sort, Give e Break; "Equipped Item cannot be damaged/sent", "This Item cannot be damaged/sent/equipped".
/// FATO (tabelas): weapon.ISHAKAI (破壊: pode quebrar), ISJYOUDO (譲渡: pode dar), ISPOCKET; abChip.WU (usa na arma) e BLUEAB
/// (usa na armadura ∋); as regras de equipar de <see cref="GameTables.CanEquipWeapon"/> e <see cref="GameTables.CanEquipArmor"/>.
/// HIPÓTESES / DECISÕES DO PORT (docs/gameplay/menu-status-tab.md): número de pockets (o jogo calcula "ポケットのかず:" no código,
/// ainda não lido): 2 nas armas com ISPOCKET e 2 em toda armadura ∋, nenhum na ∨; o soquete 0 nunca fica vazio; ordem do Sort.
/// </summary>
public static class EquipRules
{
    public const int Sockets = 3;
    /// <summary>DECISÃO DO PORT (regra do jogo não lida): pockets por peça.</summary>
    public const int PocketsPerItem = 2;

    // Índices dos textos do menu (beUnicodeObjList do area #9; mesmos nomes mswkPRG das outras fases).
    public const int TextCannotEquip = 93, TextEquippedNoBreak = 107, TextNoBreak = 108, TextEquippedNoGive = 109, TextNoGive = 110,
        TextNoOneToGive = 111;

    public static string TableOf(EquipSlot slot) => slot switch { EquipSlot.Weapon => "weapon", EquipSlot.Armor => "armor1", _ => "armor2" };

    /// <summary>Pockets de chips de uma peça (0 sem peça).</summary>
    public static int Pockets(GameTables db, string table, int row) => row < 0 ? 0 : table switch
    {
        "weapon" => db["weapon"].Int(row, "ISPOCKET") != 0 ? PocketsPerItem : 0,
        "armor1" => PocketsPerItem,
        _ => 0,
    };

    /// <summary>POWERSOCKET[0..2] (−1 = vazio).</summary>
    public static int[] SocketRows(Equipment eq) =>
        Enumerable.Range(0, Sockets).Select(i => i == 0 ? eq.WeaponRow : i - 1 < eq.ReserveWeapons.Count ? eq.ReserveWeapons[i - 1] : -1).ToArray();

    /// <summary>Quantas cópias do item o equipamento usa (soquetes, armaduras e chips).</summary>
    public static int InUse(Equipment? eq, ItemRef item)
    {
        if (eq == null) return 0;
        return item.Table switch
        {
            "weapon" => SocketRows(eq).Count(r => r == item.Row),
            "armor1" => eq.Armor1Row == item.Row ? 1 : 0,
            "armor2" => eq.Armor2Row == item.Row ? 1 : 0,
            "abChip" => eq.WeaponChips.Count(r => r == item.Row) + eq.ArmorChips.Count(r => r == item.Row),
            _ => 0,
        };
    }

    /// <summary>Marca (E) por posição da DEVICEFOLDER: as primeiras cópias de cada item, até o número em uso.</summary>
    public static bool[] EquippedMarks(Inventory inv, Equipment? eq)
    {
        var left = new Dictionary<ItemRef, int>();
        var marks = new bool[inv.Devices.Count];
        for (int i = 0; i < marks.Length; i++)
        {
            var d = inv.Devices[i];
            if (!left.TryGetValue(d, out int n)) n = InUse(eq, d);
            marks[i] = n > 0;
            left[d] = n - 1;
        }
        return marks;
    }

    /// <summary>Cópias na pasta que não estão equipadas.</summary>
    public static int Free(Inventory inv, Equipment? eq, ItemRef item) => inv.Devices.Count(d => d == item) - InUse(eq, item);

    /// <summary>Chip cabe na arma (WU) ou na armadura ∋ (BLUEAB)?</summary>
    public static bool ChipFits(GameTables db, int chipRow, bool weapon) => db["abChip"].Int(chipRow, weapon ? "WU" : "BLUEAB") != 0;

    /// <summary>Pode equipar? (arma: SOUBI e skill; armadura: nível; chip: WU/BLUEAB.)</summary>
    public static bool CanEquip(GameTables db, ItemRef item, int digimonNo, int level, IReadOnlyList<int> skill, bool weaponPocket = true) => item.Table switch
    {
        "weapon" => db.CanEquipWeapon(item.Row, digimonNo, skill),
        "armor1" or "armor2" => db.CanEquipArmor(item.Table, item.Row, level),
        "abChip" => ChipFits(db, item.Row, weaponPocket),
        _ => false,
    };

    /// <summary>Candidatas a um lugar: as peças da tabela dele com cópia livre, uma linha por item (ordem da pasta).</summary>
    public static List<ItemRef> Candidates(Inventory inv, Equipment? eq, string table) =>
        inv.Devices.Where(d => d.Table == table).Distinct().Where(d => Free(inv, eq, d) > 0).ToList();

    /// <summary>Chips livres que cabem na arma ou na armadura.</summary>
    public static List<ItemRef> ChipCandidates(GameTables db, Inventory inv, Equipment? eq, bool weapon) =>
        Candidates(inv, eq, "abChip").Where(c => ChipFits(db, c.Row, weapon)).ToList();

    static IReadOnlyList<int> Trim(IReadOnlyList<int> chips, int pockets) => chips.Count <= pockets ? chips : chips.Take(pockets).ToList();

    /// <summary>
    /// Arma no soquete <paramref name="socket"/> (−1 tira). O soquete 0 é a arma ativa (RECALC) e não fica vazio (DECISÃO DO PORT):
    /// tirar dele devolve null. Os chips da arma ficam nos pockets enquanto couberem na arma nova.
    /// </summary>
    public static Equipment? SetSocket(GameTables db, Equipment eq, int socket, int row)
    {
        if (socket is < 0 or >= Sockets) return null;
        if (socket == 0)
            return row < 0 ? null : eq with { WeaponRow = row, WeaponChips = Trim(eq.WeaponChips, Pockets(db, "weapon", row)) };
        var rows = SocketRows(eq);
        rows[socket] = row;
        return eq with { ReserveWeapons = rows.Skip(1).ToList() };
    }

    /// <summary>"Exchange Weapon Unit": a arma do soquete vira a ativa e a ativa vai para o lugar dela.</summary>
    public static Equipment? Exchange(GameTables db, Equipment eq, int socket)
    {
        var rows = SocketRows(eq);
        if (socket is <= 0 or >= Sockets || rows[socket] < 0) return null;
        (rows[0], rows[socket]) = (rows[socket], rows[0]);
        return eq with { WeaponRow = rows[0], ReserveWeapons = rows.Skip(1).ToList(), WeaponChips = Trim(eq.WeaponChips, Pockets(db, "weapon", rows[0])) };
    }

    /// <summary>Armadura ∋ ou ∨ (−1 tira). Sem a ∋, os chips dela saem.</summary>
    public static Equipment SetArmor(GameTables db, Equipment eq, EquipSlot slot, int row) => slot == EquipSlot.Armor
        ? eq with { Armor1Row = row, ArmorChips = Trim(eq.ArmorChips, Pockets(db, "armor1", row)) }
        : eq with { Armor2Row = row };

    /// <summary>Chip no pocket (−1 tira). null se o pocket não existe ou o chip não cabe ali.</summary>
    public static Equipment? SetChip(GameTables db, Equipment eq, bool weapon, int pocket, int chipRow)
    {
        int pockets = weapon ? Pockets(db, "weapon", eq.WeaponRow) : Pockets(db, "armor1", eq.Armor1Row);
        if (pocket < 0 || pocket >= pockets || (chipRow >= 0 && !ChipFits(db, chipRow, weapon))) return null;
        var chips = (weapon ? eq.WeaponChips : eq.ArmorChips).ToList();
        if (chipRow < 0) { if (pocket < chips.Count) chips.RemoveAt(pocket); }
        else if (pocket < chips.Count) chips[pocket] = chipRow;
        else chips.Add(chipRow);
        return weapon ? eq with { WeaponChips = chips } : eq with { ArmorChips = chips };
    }

    /// <summary>Por que não dá para quebrar (índice do texto) ou null. Itens de evento (Key Folder) não quebram.</summary>
    public static int? CannotBreak(GameTables db, Inventory inv, Equipment? eq, ItemRef item)
    {
        if (item.Table == "impItem") return TextNoBreak;
        if (item.Table == "weapon" && db["weapon"].Int(item.Row, "ISHAKAI") == 0) return TextNoBreak;
        if (item.Table != "conItem" && Free(inv, eq, item) <= 0) return TextEquippedNoBreak;
        return null;
    }

    /// <summary>
    /// Por que não dá para dar (índice do texto) ou null. Sem outro Digimon no grupo, "No one to give Item to." (o grupo online é a
    /// Fase 6: até lá o Give sempre recusa).
    /// </summary>
    public static int? CannotGive(GameTables db, Inventory inv, Equipment? eq, ItemRef item, int partyOthers)
    {
        if (item.Table == "impItem") return TextNoGive;
        if (db.Contains(item.Table) && db[item.Table].Has("ISJYOUDO") && db[item.Table].Int(item.Row, "ISJYOUDO") == 0) return TextNoGive;
        if (item.Table != "conItem" && Free(inv, eq, item) <= 0) return TextEquippedNoGive;
        return partyOthers <= 0 ? TextNoOneToGive : null;
    }

    /// <summary>Ordem do "Sort" (HIPÓTESE: por tipo ∈ ∋ ∨ ∧ e pela linha da tabela, que já segue a família e o grau α..ε).</summary>
    public static int SortKey(ItemRef item) => item.Table switch { "weapon" => 0, "armor1" => 1, "armor2" => 2, "abChip" => 3, _ => 4 } * 100000 + item.Row;
}
