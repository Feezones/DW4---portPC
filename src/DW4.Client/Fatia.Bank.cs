using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// D.S.G. Bank (bankshop.lua → SUBWND GENERATE "BANK/I&lt;jogador&gt;"; classe beNDMWShopCtrlEditBit, campos _editBit e _editMax).
/// Regras em <see cref="Inventory"/> (BANKBIT e BANKDEVICE, até 96 itens). Com a pasta loja, menus na j_shwin (Fatia.ArtMenu.cs):
/// SQ_BITPUTINOPEN para depositar BITS e SQ_BITDRAWOUTOPEN para sacar (FATO: as variantes existem; HIPÓTESE: qual é qual pelo
/// nome), com o valor mexido por esquerda/direita e um passo de 1 a 1 000 000 (HIPÓTESE: o _editBit do jogo edita o valor, o modo
/// não foi lido). Sem a pasta, janela da Godot com duas abas. Item equipado não vai para o banco (HIPÓTESE, como na venda).
/// </summary>
public partial class Fatia
{
    bool DeliverToBank(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "BANK") return false;
        if (ShopArtReady()) OpenBankArt(); else OpenBank();
        return true;
    }

    int _bankAmount, _bankStep = 1000;

    string BankHead => $"{TextKey("mswkPRG1301", "BITS Folder")}: {_inventory.Bit}  {TextKey("mswkPRG1302", "BITS Bank")}: {_inventory.BankBit}";

    void OpenBankArt(string status = "") => Menu("SQ_AWINOPEN", $"{TextKey("mswkPRG1300", "Bank")}\n{BankHead}" + (status.Length > 0 ? $"\n{status}" : ""),
    [
        ("Depositar BITS", () => OpenBankBits(true)),
        ("Sacar BITS", () => OpenBankBits(false)),
        ($"{TextKey("mswkPRG1350", "Deposit Item into Bank.")}\t{_inventory.Devices.Count}/{Inventory.DeviceMax}", () => OpenBankItems(true)),
        ($"{TextKey("mswkPRG1351", "Withdraw Item.")}\t{_inventory.BankDevices.Count}/{Inventory.BankDeviceMax}", () => OpenBankItems(false)),
        ("Sair do banco", CloseSaveWindow),
    ]);

    void OpenBankBits(bool deposit, string status = "", int cursor = 0)
    {
        var inv = _inventory;
        int max = deposit ? inv.Bit : Math.Min(inv.BankBit, Math.Max(0, Inventory.BitMax - inv.Bit));
        _bankAmount = Math.Clamp(_bankAmount, 0, max);
        var rows = new List<ArtRow>
        {
            new("Valor", $"{_bankAmount}", null, d => { _bankAmount = Math.Clamp(_bankAmount + d * _bankStep, 0, max); OpenBankBits(deposit, "", 0); }),
            new("Passo", $"{_bankStep}", null, d => { _bankStep = Math.Clamp(d > 0 ? _bankStep * 10 : _bankStep / 10, 1, 1_000_000); OpenBankBits(deposit, "", 1); }),
            new(deposit ? "Tudo da pasta" : "Tudo o que cabe", $"{max}", () => { _bankAmount = max; OpenBankBits(deposit, "", 2); }),
            new(deposit ? "Depositar" : "Sacar", "", () =>
            {
                bool ok = _bankAmount > 0 && (deposit ? inv.DepositBit(_bankAmount) : inv.WithdrawBit(_bankAmount));
                if (ok) _bankAmount = 0;
                OpenBankBits(deposit, ok ? "OK!" : deposit ? "Não deu: valor maior que o que você tem." : "Não deu: valor maior que o guardado.", 3);
            }),
            new("Voltar", "", () => OpenBankArt()),
        };
        OpenArtMenu(deposit ? "SQ_BITPUTINOPEN" : "SQ_BITDRAWOUTOPEN", deposit ? "Depositar BITS" : "Sacar BITS",
            BankHead + "  esq./dir. muda a linha" + (status.Length > 0 ? $"  {status}" : ""), rows, () => OpenBankArt(), cursor);
    }

    void OpenBankItems(bool deposit, string status = "", int cursor = 0)
    {
        var inv = _inventory;
        var list = (deposit ? inv.Devices : inv.BankDevices).ToList();
        string Name(ItemRef i) => _db != null && _db.Contains(i.Table) ? _db[i.Table].Text(i.Row, "NAME") : $"{i.Table} {i.Row}";
        var rows = list.Select((item, k) => new ArtRow(Name(item), deposit && Equipped(item) ? "(E)" : "", () =>
        {
            string fail = deposit
                ? Equipped(item) ? "Está equipado." : inv.DepositDevice(item) ? "" : "O banco está cheio (96)."
                : inv.WithdrawDevice(item) ? "" : "A pasta está cheia (36).";
            OpenBankItems(deposit, fail, k);
        })).ToList();
        rows.Add(new("Voltar", "", () => OpenBankArt()));
        string head = deposit ? $"{TextKey("mswkPRG1350", "Deposit Item into Bank.")}  {inv.Devices.Count}/{Inventory.DeviceMax}"
            : $"{TextKey("mswkPRG1351", "Withdraw Item.")}  {inv.BankDevices.Count}/{Inventory.BankDeviceMax}";
        OpenArtMenu("SQ_AWINOPEN", head, status, rows, () => OpenBankArt(), Math.Min(cursor, rows.Count - 1));
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
        UseGameFont(center);
        _saveLayer.AddChild(center);
        AddChild(_saveLayer);
        (tab == 0 ? (Control)deposit : mine).CallDeferred(Control.MethodName.GrabFocus);
    }
}
