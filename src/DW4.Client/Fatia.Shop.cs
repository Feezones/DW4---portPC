using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Lojas de equipamento da Central Area (SUBWND GENERATE "WPU/I&lt;p&gt;", "APU", "CHIP", "JUNK"). O estoque e a compra/venda
/// seguem o executável (ShopStock, ShopTrade; docs/gameplay/fase5-lojas.md). DECISÃO DA FATIA: as janelas do jogo (j_shwin…)
/// ainda não foram exportadas; a loja é uma caixa da Godot com 12 linhas por página, como a do jogo.
/// Controles: cima/baixo escolhe, esquerda/direita troca a página, ataque compra/vende, V ou Y alterna comprar/vender,
/// K/Backspace/B fecha.
/// </summary>
public partial class Fatia
{
    // FATO (0x804E16B0): nomes dos tipos de SUBWND, índice = tipo.
    static readonly string[] SubWndNames = ["STATUS", "XDATA", "LABO", "BANK", "CNOS", "DISK", "WPU", "APU", "CHIP", "JUNK", "CARD"];
    const int ShopPage = 12;

    GameTables? _shopDb;
    int _shopKind = -1, _shopCursor, _shopPageNo;
    bool _shopSelling;
    List<ItemRef?> _shopItems = [];
    string _shopMessage = "";
    readonly HashSet<string> _shopHeld = [];
    PanelContainer? _shopPanel;
    Label? _shopLabel;

    bool ShopOpen => _shopKind >= 0;

    // Banco da própria fase (o *_db.json que não é o da área): tem as tabelas shop*.
    GameTables? ShopDb()
    {
        if (_shopDb != null) return _shopDb;
        var dir = Path.Combine(_dir, "db");
        var file = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "ndmw_*_db.json").FirstOrDefault() : null;
        return _shopDb = file != null ? GameTables.FromJson(File.ReadAllText(file)) : null;
    }

    bool DeliverToShop(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text is not { } type) return false;
        int kind = Array.IndexOf(SubWndNames, type);
        if (ShopStock.Table(kind) is not { } table || ShopDb() is not { } db || !db.Contains(table) || _db == null)
        {
            ScriptLog($"SUBWND {type}: janela ainda não portada");
            return true;
        }
        // Linha do estoque pelo jogador 0 (FATO 0x80351864..): LEVEL, DIGIMONNO, jogadores em uso e COMMON.COUNTER[1].
        int row = ShopStock.Row(kind, _level, Ram.Players[0].Forms.Current, Math.Max(1, Ram.Ninzu), Ram.Counter(1));
        var t = db[table];
        _shopItems = row >= 0 && row < t.Rows ? ShopStock.Items(kind, col => t.Int(row, col)) : [];
        _shopKind = kind;
        _shopCursor = _shopPageNo = 0;
        _shopSelling = false;
        _shopMessage = row < 0 ? "Nada à venda ainda." : "";
        _shopHeld.Clear();
        foreach (var k in ShopKeys()) _shopHeld.Add(k); // o botão que abriu a loja ainda pode estar apertado
        ScriptLog($"loja {type}: {table} linha {row}, {_shopItems.Count(i => i != null)} itens");
        UpdateShop();
        return true;
    }

    double? QueryShop(BeMessage m) => m.Class == "SUBWND" && m.Command == "ISRUN" ? (ShopOpen || _saveLayer != null ? 1 : 0) : null;

    IEnumerable<string> ShopKeys()
    {
        if (ChatOpen) yield break;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up) || Input.IsJoyButtonPressed(0, JoyButton.DpadUp)) yield return "up";
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down) || Input.IsJoyButtonPressed(0, JoyButton.DpadDown)) yield return "down";
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left) || Input.IsJoyButtonPressed(0, JoyButton.DpadLeft)) yield return "left";
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right) || Input.IsJoyButtonPressed(0, JoyButton.DpadRight)) yield return "right";
        if (Input.IsKeyPressed(Key.J) || Input.IsKeyPressed(Key.Enter) || Input.IsJoyButtonPressed(0, JoyButton.X)) yield return "ok";
        if (Input.IsKeyPressed(Key.V) || Input.IsJoyButtonPressed(0, JoyButton.Y)) yield return "mode";
        if (Input.IsKeyPressed(Key.K) || Input.IsKeyPressed(Key.Backspace) || Input.IsJoyButtonPressed(0, JoyButton.B)) yield return "close";
    }

    /// <summary>Loja aberta: consome a entrada e segura o resto do quadro (como o menu de status).</summary>
    bool TickShop()
    {
        if (!ShopOpen) return false;
        var now = ShopKeys().ToHashSet();
        bool Pressed(string k) => now.Contains(k) && !_shopHeld.Contains(k);
        var list = ShopList();
        int pages = Math.Max(1, (list.Count + ShopPage - 1) / ShopPage);
        if (Pressed("close")) { _shopKind = -1; UpdateShop(); _shopHeld.Clear(); return true; }
        if (Pressed("mode")) { _shopSelling = !_shopSelling; _shopCursor = _shopPageNo = 0; _shopMessage = ""; }
        if (Pressed("up")) _shopCursor = (_shopCursor + ShopPage - 1) % ShopPage;
        if (Pressed("down")) _shopCursor = (_shopCursor + 1) % ShopPage;
        if (Pressed("left")) _shopPageNo = (_shopPageNo + pages - 1) % pages;
        if (Pressed("right")) _shopPageNo = (_shopPageNo + 1) % pages;
        if (Pressed("ok")) ShopAct(list);
        _shopHeld.Clear();
        foreach (var k in now) _shopHeld.Add(k);
        UpdateShop();
        return true;
    }

    // Comprar: as 36 posições do estoque. Vender: o DEVICEFOLDER (beNDMWShopCtrlDeviceSell); na loja de discos, os discos
    // guardados (HIPÓTESE: beNDMWShopCtrlDiskSell vende discos da DISKFOLDER, um por toque).
    List<ItemRef?> ShopList() => !_shopSelling ? _shopItems
        : _shopKind == ShopStock.Disk ? _inventory.DiskCounts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => (ItemRef?)new ItemRef("conItem", kv.Key)).ToList()
        : _inventory.Devices.Select(d => (ItemRef?)d).ToList();

    bool Equipped(ItemRef item) => _equip != null && item.Table switch
    {
        "weapon" => item.Row == _equip.WeaponRow,
        "armor1" => item.Row == _equip.Armor1Row,
        "armor2" => item.Row == _equip.Armor2Row,
        _ => false,
    };

    void ShopAct(List<ItemRef?> list)
    {
        int i = _shopPageNo * ShopPage + _shopCursor;
        if (_db == null || i >= list.Count || list[i] is not { } item) return;
        string name = _db[item.Table].Text(item.Row, "NAME");
        if (!_shopSelling)
        {
            int price = _db[item.Table].Int(item.Row, "HANBAI");
            _shopMessage = ShopTrade.Buy(_inventory, item, price) ? $"Comprou {name} por {price} BIT."
                : _inventory.Bit < price ? "BIT insuficiente." : item.Table == "conItem" ? "Já tem 9 desse disco." : "Pasta cheia.";
        }
        else
        {
            int price = _db[item.Table].Int(item.Row, "KAITORI");
            _shopMessage = ShopTrade.Sell(_inventory, item, price, Equipped(item)) ? $"Vendeu {name} por {price} BIT."
                : Equipped(item) ? "Está equipado." : "Não dá para vender.";
        }
    }

    void UpdateShop()
    {
        if (_shopPanel == null)
        {
            var layer = new CanvasLayer { Layer = 5 };
            _shopPanel = new PanelContainer();
            _shopPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
            _shopPanel.Position = new Vector2(-300, -230);
            _shopPanel.CustomMinimumSize = new Vector2(600, 460);
            _shopLabel = new Label();
            _shopLabel.AddThemeFontSizeOverride("font_size", 15);
            _shopLabel.AddThemeConstantOverride("line_spacing", -2);
            _shopPanel.AddChild(_shopLabel);
            layer.AddChild(_shopPanel);
            AddChild(layer);
        }
        _shopPanel.Visible = ShopOpen;
        if (!ShopOpen || _db == null) return;
        var list = ShopList();
        int pages = Math.Max(1, (list.Count + ShopPage - 1) / ShopPage);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{SubWndNames[_shopKind]} — {(_shopSelling ? "Vender" : "Comprar")}   BIT {_inventory.Bit}   página {_shopPageNo + 1}/{pages}");
        sb.AppendLine("[cima/baixo escolhe, esq./dir. página, ataque confirma, V/Y troca, K/B sai]");
        for (int r = 0; r < ShopPage; r++)
        {
            int i = _shopPageNo * ShopPage + r;
            string cur = r == _shopCursor ? "▶" : " ";
            if (i >= list.Count) { sb.AppendLine(cur); continue; }
            if (list[i] is not { } it) { sb.AppendLine($"{cur} ----"); continue; }
            var t = _db[it.Table];
            int price = t.Int(it.Row, _shopSelling ? "KAITORI" : "HANBAI");
            sb.AppendLine($"{cur} {t.Text(it.Row, "NAME"),-28} {price,8}{(_shopSelling && Equipped(it) ? "  (equipado)" : "")}");
        }
        sb.Append(_shopMessage);
        _shopLabel!.Text = sb.ToString();
    }
}
