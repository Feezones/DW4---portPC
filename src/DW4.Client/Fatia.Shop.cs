using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Lojas de equipamento da Central Area (SUBWND GENERATE "WPU/I&lt;p&gt;", "APU", "CHIP", "JUNK", "DISK"). O estoque e a compra/venda
/// seguem o executável (ShopStock, ShopTrade; docs/gameplay/fase5-lojas.md). A tela é o <see cref="MenuScreen"/> (DECISÃO DO PORT,
/// 2026-11-06): abas Comprar/Vender, itens iguais numa linha só, quadro com o texto do item e a comparação com o equipado,
/// quantidade com esquerda/direita ou − e +. FATO: o estoque do jogo tem 36 posições com repetidos e vazios ("----") e a compra não
/// tira do estoque, então juntar os repetidos não muda o que dá para comprar.
/// </summary>
public partial class Fatia
{
    // FATO (0x804E16B0): nomes dos tipos de SUBWND, índice = tipo.
    static readonly string[] SubWndNames = ["STATUS", "XDATA", "LABO", "BANK", "CNOS", "DISK", "WPU", "APU", "CHIP", "JUNK", "CARD"];

    GameTables? _shopDb;
    int _shopKind = -1, _shopCursor, _shopQty = 1;
    bool _shopSelling, _shopMessageBad;
    ItemRef? _shopQtyItem;
    List<ItemRef?> _shopItems = [];
    string _shopMessage = "";

    bool ShopOpen => _shopKind >= 0;

    // Banco da própria fase (o *_db.json que não é o da área): tem as tabelas shop*.
    GameTables? ShopDb()
    {
        if (_shopDb != null) return _shopDb;
        var dir = Path.Combine(_dir, "db");
        var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "ndmw_*_db.json").FirstOrDefault() : null;
        return _shopDb = file != null ? GameTables.FromJson(GameFiles.ReadAllText(file)) : null;
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
        _shopCursor = 0;
        _shopSelling = false;
        (_shopMessage, _shopMessageBad) = row < 0 ? ("Nada à venda ainda.", true) : ("", false);
        ScriptLog($"loja {type}: {table} linha {row}, {_shopItems.Count(i => i != null)} itens");
        ShowShop();
        return true;
    }

    double? QueryShop(BeMessage m) => m.Class == "SUBWND" && m.Command == "ISRUN" ? (ShopOpen || SubWindowOpen ? 1 : 0) : null;

    /// <summary>Loja aberta: o jogador fica parado (a tela lê as teclas sozinha).</summary>
    bool TickShop() => ShopOpen;

    void CloseShop()
    {
        if (!ShopOpen) return;
        ScriptLog("loja fechada");
        _shopKind = -1;
        if (!_artMenuOpen) CloseListScreen();
    }

    // FATO (textos mswkPRG1500..1900 da Central Area): "Disk Shop", "Weapon Shop", "Armor Shop", "Chip Shop", "Junk Shop".
    string ShopTitle => _shopKind switch
    {
        ShopStock.Disk => TextKey("mswkPRG1500", "Disk Shop"),
        ShopStock.Weapon => TextKey("mswkPRG1600", "Weapon Shop"),
        ShopStock.Armor => TextKey("mswkPRG1700", "Armor Shop"),
        ShopStock.Chip => TextKey("mswkPRG1800", "Chip Shop"),
        ShopStock.Junk => TextKey("mswkPRG1900", "Junk Shop"),
        _ => SubWndNames[Math.Max(0, _shopKind)],
    };

    // Equipado = nenhuma cópia livre (soquetes da arma, armaduras e chips contam: EquipRules).
    bool Equipped(ItemRef item) => item.Table is "weapon" or "armor1" or "armor2" or "abChip" && EquipRules.InUse(_equip, item) > 0
        && EquipRules.Free(_inventory, _equip, item) <= 0;

    // Cópias que dá para vender (as equipadas ficam).
    int FreeCopies(ItemRef item) => item.Table == "conItem" ? _inventory.Disks(item.Row)
        : Math.Max(0, item.Table is "weapon" or "armor1" or "armor2" or "abChip" ? EquipRules.Free(_inventory, _equip, item) : _inventory.Devices.Count(d => d == item));

    int Owned(ItemRef item) => item.Table == "conItem" ? _inventory.Disks(item.Row) : _inventory.Devices.Count(d => d == item);

    // Quantas dá para comprar: BIT, espaço na pasta (36) ou na pasta de discos (9 por disco).
    int BuyMax(ItemRef item, int price)
    {
        int room = item.Table == "conItem" ? Inventory.DiskMax - _inventory.Disks(item.Row) : Inventory.DeviceMax - _inventory.Devices.Count;
        int money = price > 0 ? _inventory.Bit / price : 99;
        return Math.Max(0, Math.Min(Math.Min(room, money), 99));
    }

    int QtyFor(ItemRef item, int max) => _shopQtyItem == item ? Math.Clamp(_shopQty, 1, Math.Max(1, max)) : 1;

    void ShowShop()
    {
        if (!ShopOpen || _db == null) return;
        var inv = _inventory;
        bool disks = _shopKind == ShopStock.Disk;
        var rows = new List<MenuRow>();
        if (!_shopSelling)
        {
            // Comprar: o estoque sem os vazios, cada item uma vez.
            var stock = _shopItems.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
            for (int k = 0; k < stock.Count; k++)
            {
                var it = stock[k];
                int index = k, price = _db[it.Table].Int(it.Row, "HANBAI"), have = Owned(it), max = BuyMax(it, price), q = QtyFor(it, max);
                var (tag, tagColor) = ItemTag(it);
                rows.Add(new MenuRow
                {
                    Label = ItemName(it), Tag = tag, TagColor = tagColor, Count = have > 0 ? $"×{have}" : "", Value = $"{price}", Short = max <= 0,
                    Info = () => ItemMenuInfo(it, new MenuLine($"Preço {price} BIT   Você possui {have}" + (max > 0 ? $"   Cabem {max}" : ""), MenuTone.Dim)),
                    Adjust = d => { _shopQty = Math.Clamp(QtyFor(it, max) + d, 1, Math.Max(1, max)); _shopQtyItem = it; _shopCursor = index; ShowShop(); },
                    AdjustLabel = $"Quantidade {q}",
                    Action = $"Comprar ×{q}  ·  {price * q} BIT",
                    Pick = () => { _shopCursor = index; ShopBuy(it, price, QtyFor(it, max)); },
                });
            }
        }
        else
        {
            // Vender: discos guardados na loja de discos (HIPÓTESE: beNDMWShopCtrlDiskSell vende da DISKFOLDER) ou a Device Folder,
            // itens iguais juntos; equipado não vende (FATO 0x803A1338), só as cópias livres.
            var list = disks ? inv.DiskCounts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => (new ItemRef("conItem", kv.Key), kv.Value)).ToList()
                : Grouped(inv.Devices);
            for (int k = 0; k < list.Count; k++)
            {
                var (it, count) = list[k];
                int index = k, price = _db[it.Table].Int(it.Row, "KAITORI"), free = FreeCopies(it), q = QtyFor(it, free);
                bool equipped = free < count;
                rows.Add(new MenuRow
                {
                    Label = ItemName(it) + (equipped ? " (E)" : ""), Count = $"×{count}", Value = price >= 1 ? $"{price}" : "—", Short = free <= 0 || price < 1,
                    Info = () => ItemMenuInfo(it, [new MenuLine($"Vende por {price} BIT   Você possui {count}", MenuTone.Dim),
                        .. equipped ? [new MenuLine(free > 0 ? $"{count - free} equipado: vende só as outras {free}." : "Está equipado: não dá para vender.", MenuTone.Warn)] : Array.Empty<MenuLine>()]),
                    Adjust = free > 1 ? d => { _shopQty = Math.Clamp(QtyFor(it, free) + d, 1, free); _shopQtyItem = it; _shopCursor = index; ShowShop(); } : null,
                    AdjustLabel = $"Quantidade {q}",
                    Action = $"Vender ×{q}  ·  {Math.Max(0, price) * q} BIT",
                    Pick = () => { _shopCursor = index; ShopSell(it, price, QtyFor(it, free)); },
                });
            }
        }
        string folder = disks ? $"{inv.DiskCounts.Values.Sum()}" : $"{inv.Devices.Count}/{Inventory.DeviceMax}";
        ShowList(new MenuPage
        {
            Title = ShopTitle,
            Tabs = ["Comprar", "Vender"],
            Tab = _shopSelling ? 1 : 0,
            SetTab = t => { _shopSelling = t == 1; _shopCursor = 0; _shopMessage = ""; _shopQtyItem = null; ShowShop(); },
            Money = [(TextKey("mswkPRG1356", "BITS"), $"{inv.Bit}"), (TextKey(disks ? "mswkPRG1001" : "mswkPRG1000", disks ? "Disk Folder" : "Device Folder"), folder)],
            Columns = ("Item", _shopSelling ? "Qtd" : "Possui", _shopSelling ? "Venda" : "Preço"),
            Rows = rows,
            Intro = new MenuInfo { Title = ShopTitle, Lines = [new MenuLine(_shopSelling ? "Nada para vender." : "Nada à venda.", MenuTone.Dim)] },
            Message = _shopMessage,
            MessageBad = _shopMessageBad,
            Back = CloseShop,
            Cursor = _shopCursor,
        });
    }

    void ShopBuy(ItemRef item, int price, int qty)
    {
        string name = ItemName(item);
        int done = 0;
        while (done < qty && ShopTrade.Buy(_inventory, item, price)) done++;
        if (done > 0) (_shopMessage, _shopMessageBad) = ($"Comprou {(done > 1 ? $"{done}× " : "")}{name} por {price * done} BIT.", false);
        else (_shopMessage, _shopMessageBad) = (_inventory.Bit < price ? TextKey("mswkPRG1570", "Not enough BITS.")
            : item.Table == "conItem" ? "Já tem 9 desse disco." : "Pasta cheia (36).", true);
        ScriptLog($"loja: {_shopMessage} (BIT {_inventory.Bit})");
        _shopQty = 1;
        ShowShop();
    }

    void ShopSell(ItemRef item, int price, int qty)
    {
        string name = ItemName(item);
        int done = 0;
        while (done < qty && ShopTrade.Sell(_inventory, item, price, Equipped(item))) done++; // disco: tira um da DISKFOLDER
        if (done > 0) (_shopMessage, _shopMessageBad) = ($"Vendeu {(done > 1 ? $"{done}× " : "")}{name} por {price * done} BIT.", false);
        else (_shopMessage, _shopMessageBad) = (Equipped(item) ? "Está equipado." : "Não dá para vender.", true);
        ScriptLog($"loja: {_shopMessage} (BIT {_inventory.Bit})");
        _shopQty = 1;
        ShowShop();
    }
}
