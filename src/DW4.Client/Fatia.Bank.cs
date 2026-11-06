using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// D.S.G. Bank (bankshop.lua → SUBWND GENERATE "BANK/I&lt;jogador&gt;"; classe beNDMWShopCtrlEditBit, campos _editBit e _editMax).
/// Regras em <see cref="Inventory"/> (BANKBIT e BANKDEVICE, até 96 itens). Tela no <see cref="MenuScreen"/> (DECISÃO DO PORT,
/// 2026-11-06): abas BITS / Guardar item / Retirar item; o valor muda por esquerda/direita com um passo de 1 a 1 000 000
/// (HIPÓTESE: o _editBit do jogo edita o valor, o modo não foi lido). Item equipado não vai para o banco (HIPÓTESE, como na venda).
/// </summary>
public partial class Fatia
{
    bool DeliverToBank(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "BANK") return false;
        OpenBankArt();
        return true;
    }

    int _bankAmount, _bankStep = 1000, _bankTab;

    /// <summary>Banco: abas BITS / Guardar item / Retirar item, com a pasta e o banco sempre à vista.</summary>
    void OpenBankArt(string status = "", bool bad = false, int cursor = 0)
    {
        var inv = _inventory;
        var rows = new List<MenuRow>();
        if (_bankTab == 0)
        {
            int maxIn = inv.Bit, maxOut = Math.Min(inv.BankBit, Math.Max(0, Inventory.BitMax - inv.Bit));
            _bankAmount = Math.Clamp(_bankAmount, 0, Math.Max(maxIn, maxOut));
            var help = new MenuInfo
            {
                Title = TextKey("mswkPRG1356", "BITS"),
                Lines = [new MenuLine("Escolha o valor com esquerda/direita (ou − e +). \"Cada toque soma\" diz de quanto em quanto o valor muda (1, 10, 100, 1000…). Depois escolha Depositar ou Sacar."), new MenuLine("Os BITS do banco não se perdem.", MenuTone.Dim)],
            };
            rows.Add(new MenuRow { Label = "Valor", Value = $"{_bankAmount}", AdjustLabel = $"Valor {_bankAmount}", Info = () => help,
                Adjust = d => { _bankAmount = Math.Clamp(_bankAmount + d * _bankStep, 0, Math.Max(maxIn, maxOut)); OpenBankArt("", false, 0); } });
            rows.Add(new MenuRow { Label = "Cada toque soma", Value = $"{_bankStep}", AdjustLabel = $"De {_bankStep} em {_bankStep}", Info = () => help,
                Adjust = d => { _bankStep = Math.Clamp(d > 0 ? _bankStep * 10 : _bankStep / 10, 1, 1_000_000); OpenBankArt("", false, 1); } });
            rows.Add(new MenuRow { Label = $"Depositar {_bankAmount}", Short = _bankAmount > maxIn || _bankAmount == 0, Info = () => help,
                Pick = () => BankBits(true, _bankAmount, 2) });
            rows.Add(new MenuRow { Label = $"Sacar {_bankAmount}", Short = _bankAmount > maxOut || _bankAmount == 0, Info = () => help,
                Pick = () => BankBits(false, _bankAmount, 3) });
            rows.Add(new MenuRow { Label = "Depositar tudo", Value = $"{maxIn}", Short = maxIn == 0, Info = () => help, Pick = () => BankBits(true, maxIn, 4) });
            rows.Add(new MenuRow { Label = "Sacar tudo o que cabe", Value = $"{maxOut}", Short = maxOut == 0, Info = () => help, Pick = () => BankBits(false, maxOut, 5) });
        }
        else
        {
            bool deposit = _bankTab == 1;
            var list = Grouped(deposit ? inv.Devices : inv.BankDevices);
            for (int k = 0; k < list.Count; k++)
            {
                var (item, count) = list[k];
                int index = k;
                bool locked = deposit && Equipped(item);
                rows.Add(new MenuRow
                {
                    Label = ItemName(item) + (deposit && EquipRules.InUse(_equip, item) > 0 ? " (E)" : ""), Count = $"×{count}", Short = locked,
                    Info = () => ItemMenuInfo(item, locked ? [new MenuLine("Está equipado: não vai para o banco.", MenuTone.Warn)] : []),
                    Action = deposit ? "Guardar no banco" : "Retirar para a pasta",
                    Pick = () =>
                    {
                        string fail = deposit
                            ? Equipped(item) ? "Está equipado." : inv.DepositDevice(item) ? "" : "O banco está cheio (96)."
                            : inv.WithdrawDevice(item) ? "" : "A pasta está cheia (36).";
                        OpenBankArt(fail.Length > 0 ? fail : $"{(deposit ? "Guardou" : "Retirou")} {ItemName(item)}.", fail.Length > 0, index);
                    },
                });
            }
        }
        rows.Add(new MenuRow { Label = TextKey("mswkPRG1373", "End bank transaction."), Pick = CloseSaveWindow });
        ShowList(new MenuPage
        {
            Title = TextKey("mswkPRG1300", "Bank"),
            Tabs = [TextKey("mswkPRG1356", "BITS"), "Guardar item", "Retirar item"],
            Tab = _bankTab,
            SetTab = t => { _bankTab = t; OpenBankArt(); },
            Money = [(TextKey("mswkPRG1301", "BITS Folder"), $"{inv.Bit}"), (TextKey("mswkPRG1302", "BITS Bank"), $"{inv.BankBit}"),
                (TextKey("mswkPRG1000", "Device Folder"), $"{inv.Devices.Count}/{Inventory.DeviceMax}"), (TextKey("mswkPRG1303", "Item Bank"), $"{inv.BankDevices.Count}/{Inventory.BankDeviceMax}")],
            Columns = _bankTab == 0 ? null : ("Item", "Qtd", ""),
            Rows = rows,
            Intro = new MenuInfo { Title = TextKey("mswkPRG1300", "Bank"), Lines = [new MenuLine(_bankTab == 1 ? "Nada na pasta." : TextKey("mswkPRG1374", "There are no deposited Items."), MenuTone.Dim)] },
            Message = status,
            MessageBad = bad,
            Back = CloseSaveWindow,
            Cursor = cursor,
        });
    }

    void BankBits(bool deposit, int amount, int cursor)
    {
        var inv = _inventory;
        bool ok = amount > 0 && (deposit ? inv.DepositBit(amount) : inv.WithdrawBit(amount));
        if (DiagNpc) GD.Print($"[banco] {(deposit ? "depositar" : "sacar")} {amount}: {ok} (pasta {inv.Bit}, banco {inv.BankBit})");
        if (ok) _bankAmount = 0;
        OpenBankArt(ok ? $"{(deposit ? "Depositou" : "Sacou")} {amount} BITS." : amount <= 0 ? "Escolha um valor."
            : deposit ? "Não deu: valor maior que o que você tem." : "Não deu: valor maior que o guardado (ou a pasta passa de 9 999 999).", !ok, cursor);
    }
}
