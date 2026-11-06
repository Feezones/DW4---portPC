using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Fortalecer chip com X-data (xdatashop.lua → SUBWND GENERATE "XDATA/I&lt;jogador&gt;"; regra em <see cref="Inventory.UpgradeChip"/>).
/// Tela no <see cref="MenuScreen"/> (DECISÃO DO PORT, 2026-11-06; antes a j_shwin SQ_XDATASTATUSOPEN/SQ_XDATAITEMOPEN): abas
/// Atributos e Chips, com o custo (NEXTX) e o X-data do jogador.
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

    int _xdataTab;

    // Tela do X-data: abas Atributos (XDataStatus) e Chips (UpgradeChip), com o ¶ Data do jogador sempre à vista (mswkPRG1100..1172).
    void OpenXDataMenu() { _xdataTab = 0; OpenXDataStatus(); }

    MenuPage XDataPage(List<MenuRow> rows, string status, bool bad, int cursor, MenuInfo intro)
    {
        rows.Add(new MenuRow { Label = TextKey("mswkPRG1152", "Return to Main Lobby."), Pick = CloseSaveWindow });
        return new MenuPage
        {
            Title = TextKey("mswkPRG1100", "¶ Data Keeper"),
            Tabs = [TextKey("mswkPRG1101", "Parameter"), TextKey("mswkPRG1110", "Ability Chip").Trim().TrimEnd('.')],
            Tab = _xdataTab,
            SetTab = t => { _xdataTab = t; if (t == 0) OpenXDataStatus(); else OpenXDataChips(); },
            Money = [(TextKey("mswkPRG1102", "¶ Data"), $"{_inventory.XData}")],
            Columns = _xdataTab == 0 ? ("", "Atual", "Custo") : ("", "Qtd", "Custo"),
            Rows = rows,
            Intro = intro,
            Message = status,
            MessageBad = bad,
            Back = CloseSaveWindow,
            Cursor = cursor,
        };
    }

    // Atributos do Digimon com o valor atual, o custo e o texto do jogo de cada um (mswkPRG1154..1160).
    void OpenXDataStatus(string status = "", bool bad = false, int cursor = 0)
    {
        if (_pf == null) { CloseSaveWindow(); return; }
        _xdataTab = 0;
        int[] values = [_pf.Tsuyosa, _pf.Mamori, _pf.Kashikosa, _pf.Seishin, _pf.Subayasa, _pf.MaxHp, _pf.MaxMp];
        string[] names = [TextKey("mswkPRG1103", "Strength"), TextKey("mswkPRG1104", "Defense"), TextKey("mswkPRG1105", "Wisdom"),
            TextKey("mswkPRG1106", "Spirit"), TextKey("mswkPRG1107", "Speed"), TextKey("mswkPRG1108", "Max HP"), TextKey("mswkPRG1109", "Max MP")];
        var rows = new List<MenuRow>();
        for (int i = 0; i < values.Length; i++)
        {
            int p = i, cost = XDataStatus.Cost(values[i]);
            var info = new MenuInfo
            {
                Title = names[i],
                Lines = [new MenuLine(TextKey($"mswkPRG{1154 + i}", "")), new MenuLine($"Agora {values[i]}   Custo {cost} ¶", MenuTone.Info)],
            };
            if (_inventory.XData < cost) info.Lines.Add(new MenuLine(TextKey("mswkPRG1161", "Insufficient ¶ Data.").Replace("\r\n", "\n").Split('\n')[0].Trim(), MenuTone.Warn));
            rows.Add(new MenuRow
            {
                Label = names[i], Count = $"{values[i]}", Value = $"{cost} ¶", Short = _inventory.XData < cost, Info = () => info,
                Action = TextKey("mswkPRG1163", "Increase this Parameter."), Pick = () => RaiseStatus(p, values[p]),
            });
        }
        ShowList(XDataPage(rows, status, bad, cursor, new MenuInfo { Title = TextKey("mswkPRG1153", "Select the Parameter you want to increase.") }));
    }

    void RaiseStatus(int param, int value)
    {
        var forms = Ram.Players[0].Forms;
        if (forms.Current < 0) { CloseSaveWindow(); return; }
        if (!XDataStatus.Raise(_inventory, forms[forms.Current].UpCount, param, value))
        {
            OpenXDataStatus(TextKey("mswkPRG1161", "Insufficient ¶ data. Power cannot be raised.").Replace("\r\n", " "), true, param);
            return;
        }
        Recalc();
        OpenXDataStatus("OK!", false, param);
    }

    // Chips da pasta (iguais juntos) com o custo da NEXTX; fortalecer mexe no primeiro deles (o equipado continua equipado).
    void OpenXDataChips(string status = "", bool bad = false, int cursor = 0)
    {
        if (_db == null || !_db.Contains("abChip")) { ScriptLog("SUBWND XDATA: sem a tabela abChip"); return; }
        _xdataTab = 1;
        var inv = _inventory;
        var t = _db["abChip"];
        var rows = new List<MenuRow>();
        var groups = Grouped(inv.Devices.Where(d => d.Table == "abChip")).ToList();
        for (int k = 0; k < groups.Count; k++)
        {
            var (chip, count) = groups[k];
            int next = t.Int(chip.Row, "NEXTX"), index = k;
            var after = next >= 0 && chip.Row + 1 < t.Rows ? ItemName(chip with { Row = chip.Row + 1 }) : "";
            rows.Add(new MenuRow
            {
                Label = ItemName(chip) + (EquipRules.InUse(_equip, chip) > 0 ? " (E)" : ""), Count = $"×{count}", Value = next < 0 ? "—" : $"{next} ¶",
                Short = next < 0 || inv.XData < next,
                Info = () => ItemMenuInfo(chip, next < 0 ? [new MenuLine(TextKey("mswkPRG1170", "This Ability Chip cannot be increased."), MenuTone.Warn)]
                    : [new MenuLine($"Custo {next} ¶   Vira {after}", MenuTone.Dim)]),
                Action = next < 0 ? "" : TextKey("mswkPRG1171", "Increase this Ability Chip's Parameter."),
                Pick = next < 0 ? null : () => UpgradeChip(Enumerable.Range(0, inv.Devices.Count).First(i => inv.Devices[i] == chip), index),
            });
        }
        var intro = new MenuInfo { Title = TextKey(groups.Count == 0 ? "mswkPRG1169" : "mswkPRG1168", groups.Count == 0 ? "No Ability Chip" : "Select the Ability Chip you want to increase.") };
        ShowList(XDataPage(rows, status, bad, cursor, intro));
    }

    void UpgradeChip(int index, int cursor)
    {
        var inv = _inventory;
        var before = inv.Devices[index];
        int next = _db!["abChip"].Int(before.Row, "NEXTX");
        if (next < 0) { OpenXDataChips(TextKey("mswkPRG1170", "This Ability Chip cannot be increased."), true, cursor); return; }
        if (inv.XData < next) { OpenXDataChips($"¶ Data insuficiente ({inv.XData}/{next}).", true, cursor); return; }
        if (!inv.UpgradeChip(index, next)) { OpenXDataChips("Não deu para fortalecer.", true, cursor); return; }
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
        OpenXDataChips($"{_db["abChip"].Text(after.Row, "NAME")}!", false, cursor);
    }
}
