using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Fortalecer chip com X-data (xdatashop.lua → SUBWND GENERATE "XDATA/I&lt;jogador&gt;"; regra em <see cref="Inventory.UpgradeChip"/>).
/// DECISÃO DA FATIA: janela da Godot com os chips da pasta, o custo (NEXTX) e o X-data do jogador, no lugar da j_shwin.
/// Chip equipado continua equipado com a linha nova (o jogo mexe no próprio objeto do item, 0x803A61AC).
/// </summary>
public partial class Fatia
{
    bool DeliverToXData(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "XDATA") return false;
        OpenXData();
        return true;
    }

    void OpenXData(string status = "")
    {
        if (_db == null || !_db.Contains("abChip")) { ScriptLog("SUBWND XDATA: sem a tabela abChip"); return; }
        var inv = _inventory;
        var chips = Enumerable.Range(0, inv.Devices.Count).Where(i => inv.Devices[i].Table == "abChip").ToList();
        var t = _db["abChip"];
        var options = new List<(string, System.Action)>();
        foreach (int i in chips)
        {
            int row = inv.Devices[i].Row;
            int next = t.Int(row, "NEXTX");
            int index = i;
            options.Add(($"{t.Text(row, "NAME")}   {(next < 0 ? "—" : $"{next} ¶")}", () => UpgradeChip(index)));
        }
        options.Add(("Sair", CloseSaveWindow));
        string head = $"{TextKey("mswkPRG1151", "Increase Ability Chip Parameter.")}\n¶ Data: {inv.XData}"
            + (chips.Count == 0 ? $"\n{TextKey("mswkPRG1169", "No Ability Chip")}" : $"\n{TextKey("mswkPRG1168", "Select the Ability Chip you want to increase.")}")
            + (status.Length > 0 ? $"\n\n{status}" : "");
        SaveWindow(head, options.ToArray());
    }

    void UpgradeChip(int index)
    {
        var inv = _inventory;
        var before = inv.Devices[index];
        int next = _db!["abChip"].Int(before.Row, "NEXTX");
        if (next < 0) { OpenXData(TextKey("mswkPRG1170", "This Ability Chip cannot be increased.")); return; }
        if (inv.XData < next) { OpenXData($"¶ Data insuficiente ({inv.XData}/{next})."); return; }
        if (!inv.UpgradeChip(index, next)) { OpenXData("Não deu para fortalecer."); return; }
        var after = inv.Devices[index];
        if (_equip != null)
        {
            static IReadOnlyList<int> Swap(IReadOnlyList<int> rows, int from, int to)
            {
                var list = rows.ToList();
                int k = list.IndexOf(from);
                if (k >= 0) list[k] = to;
                return list;
            }
            _equip = _equip with { WeaponChips = Swap(_equip.WeaponChips, before.Row, after.Row), ArmorChips = Swap(_equip.ArmorChips, before.Row, after.Row) };
        }
        Recalc();
        OpenXData($"{_db["abChip"].Text(after.Row, "NAME")}!");
    }
}
