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
        OpenXDataMenu();
        return true;
    }

    // Primeira tela do X-data: as escolhas do jogo (mswkPRG1150..1152).
    void OpenXDataMenu() => SaveWindow($"{TextKey("mswkPRG1100", "¶ Data Keeper")}\n¶ Data: {_inventory.XData}",
    [
        (TextKey("mswkPRG1150", "Raise Digimon parameter."), () => OpenXDataStatus()),
        (TextKey("mswkPRG1151", "Raise Ability Chip parameter."), () => OpenXDataChips()),
        (TextKey("mswkPRG1152", "Return to Main Lobby."), CloseSaveWindow),
    ]);

    // Atributos do Digimon com o valor atual e o custo (XDataStatus).
    void OpenXDataStatus(string status = "")
    {
        if (_pf == null) { CloseSaveWindow(); return; }
        int[] values = [_pf.Tsuyosa, _pf.Mamori, _pf.Kashikosa, _pf.Seishin, _pf.Subayasa, _pf.MaxHp, _pf.MaxMp];
        string[] names = [TextKey("mswkPRG1103", "Strength"), TextKey("mswkPRG1104", "Defense"), TextKey("mswkPRG1105", "Wisdom"),
            TextKey("mswkPRG1106", "Spirit"), TextKey("mswkPRG1107", "Speed"), TextKey("mswkPRG1108", "Max HP"), TextKey("mswkPRG1109", "Max MP")];
        var options = new List<(string, System.Action)>();
        for (int i = 0; i < values.Length; i++)
        {
            int p = i;
            options.Add(($"{names[i]}  {values[i]}   {XDataStatus.Cost(values[i])} ¶", () => RaiseStatus(p, values[p])));
        }
        options.Add(("Voltar", () => OpenXDataMenu()));
        SaveWindow($"{TextKey("mswkPRG1153", "Select the parameter you want to raise.")}\n¶ Data: {_inventory.XData}" + (status.Length > 0 ? $"\n\n{status}" : ""), options.ToArray());
    }

    void RaiseStatus(int param, int value)
    {
        var forms = Ram.Players[0].Forms;
        if (forms.Current < 0) { CloseSaveWindow(); return; }
        if (!XDataStatus.Raise(_inventory, forms[forms.Current].UpCount, param, value))
        {
            OpenXDataStatus(TextKey("mswkPRG1161", "Insufficient ¶ data. Power cannot be raised."));
            return;
        }
        Recalc();
        OpenXDataStatus("OK!");
    }

    void OpenXDataChips(string status = "")
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
        options.Add(("Voltar", () => OpenXDataMenu()));
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
        if (next < 0) { OpenXDataChips(TextKey("mswkPRG1170", "This Ability Chip cannot be increased.")); return; }
        if (inv.XData < next) { OpenXDataChips($"¶ Data insuficiente ({inv.XData}/{next})."); return; }
        if (!inv.UpgradeChip(index, next)) { OpenXDataChips("Não deu para fortalecer."); return; }
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
        OpenXDataChips($"{_db["abChip"].Text(after.Row, "NAME")}!");
    }
}
