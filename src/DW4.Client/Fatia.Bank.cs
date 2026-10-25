using System;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// D.S.G. Bank (bankshop.lua → SUBWND GENERATE "BANK/I&lt;jogador&gt;"; classe beNDMWShopCtrlEditBit, campos _editBit e _editMax).
/// Regras em <see cref="Inventory"/> (BANKBIT e BANKDEVICE, até 96 itens). DECISÃO DA FATIA: janela da Godot com duas abas, BITS e
/// itens, no lugar das janelas j_* do jogo (não exportadas). Item equipado não vai para o banco (HIPÓTESE, como na venda).
/// </summary>
public partial class Fatia
{
    bool DeliverToBank(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "BANK") return false;
        OpenBank();
        return true;
    }

    void OpenBank(int tab = 0)
    {
        CloseSaveWindow();
        _padEnabled = false;
        _saveLayer = new CanvasLayer { Layer = 20 };
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 460) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        var title = new Label { Text = TextKey("mswkPRG1300", "Bank"), HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        box.AddChild(tabs);
        var inv = _inventory;
        var status = new Label();

        // BITS
        var bits = new VBoxContainer { Name = TextKey("mswkPRG1356", "BITS") };
        var folder = new Label();
        var bank = new Label();
        void Refresh() { folder.Text = $"{TextKey("mswkPRG1301", "BITS Folder")}: {inv.Bit}"; bank.Text = $"{TextKey("mswkPRG1302", "BITS Bank")}: {inv.BankBit}"; }
        Refresh();
        bits.AddChild(folder);
        bits.AddChild(bank);
        var amount = new SpinBox { MinValue = 0, MaxValue = Inventory.BitMax, Step = 1, Rounded = true, CustomMinimumSize = new Vector2(240, 0) };
        bits.AddChild(amount);
        var row = new HBoxContainer();
        Button Act(string text, Func<int, bool> act, string fail)
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            b.Pressed += () => { status.Text = act((int)amount.Value) ? "" : fail; Refresh(); };
            row.AddChild(b);
            return b;
        }
        var deposit = Act("Depositar", inv.DepositBit, "Não deu: valor maior que o que você tem (ou banco cheio).");
        Act("Sacar", inv.WithdrawBit, "Não deu: valor maior que o guardado (ou passa de 9 999 999).");
        var all = new Button { Text = "Depositar tudo", CustomMinimumSize = new Vector2(0, 44) };
        all.Pressed += () => { amount.Value = inv.Bit; status.Text = inv.DepositBit(inv.Bit) ? "" : "Nada para depositar."; Refresh(); };
        row.AddChild(all);
        bits.AddChild(row);
        tabs.AddChild(bits);

        // Itens
        var items = new HBoxContainer { Name = TextKey("mswkPRG1303", "Item Bank") };
        ItemList List(string head)
        {
            var col = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            col.AddChild(new Label { Text = head });
            var list = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 280) };
            col.AddChild(list);
            items.AddChild(col);
            return list;
        }
        var mine = List($"{TextKey("mswkPRG1350", "Deposit Item into Bank.")}  ({inv.Devices.Count}/{Inventory.DeviceMax})");
        var stored = List($"{TextKey("mswkPRG1351", "Withdraw Item.")}  ({inv.BankDevices.Count}/{Inventory.BankDeviceMax})");
        string Name(ItemRef i) => _db != null && _db.Contains(i.Table) ? _db[i.Table].Text(i.Row, "NAME") : $"{i.Table} {i.Row}";
        foreach (var d in inv.Devices) mine.AddItem(Name(d) + (Equipped(d) ? "  (equipado)" : ""));
        foreach (var d in inv.BankDevices) stored.AddItem(Name(d));
        mine.ItemActivated += i =>
        {
            var item = inv.Devices[(int)i];
            if (Equipped(item)) { status.Text = "Está equipado."; return; }
            if (!inv.DepositDevice(item)) { status.Text = "O banco está cheio (96)."; return; }
            OpenBank(1);
        };
        stored.ItemActivated += i =>
        {
            if (!inv.WithdrawDevice(inv.BankDevices[(int)i])) { status.Text = "A pasta está cheia (36)."; return; }
            OpenBank(1);
        };
        tabs.AddChild(items);
        tabs.CurrentTab = tab;

        box.AddChild(status);
        var close = new Button { Text = "Sair do banco", CustomMinimumSize = new Vector2(0, 44) };
        close.Pressed += CloseSaveWindow;
        box.AddChild(close);
        panel.AddChild(box);
        center.AddChild(panel);
        _saveLayer.AddChild(center);
        AddChild(_saveLayer);
        (tab == 0 ? (Control)deposit : mine).CallDeferred(Control.MethodName.GrabFocus);
    }
}
