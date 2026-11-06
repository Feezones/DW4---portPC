using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Guardião das Cartas (stagIS11, fase de menu): abrir B. Pack pela regra do jogo (<see cref="CardPack"/>, 0x8039ACC4) com a tabela
/// "card" de &lt;fases&gt;\cartas (dw4 fase-exportar, area.afs #621) e o álbum das 50 cartas. Tela: <see cref="CardAlbumScreen"/>
/// (DECISÃO DO PORT, 2026-11-06: abas Álbum/B. Pack com a arte card01..card50 do mesmo #621, no lugar do j_shbooster/j_shcardNN e da
/// janela de botões de antes). Sem a arte (dados antigos), a tela usa o verso ou fica sem imagem; as regras não mudam.
/// </summary>
public partial class Fatia
{
    GameTables? _cardDb;
    bool _cardTried;
    CardAlbumScreen? _cardScreen;
    CanvasLayer? _cardLayer;
    readonly Dictionary<int, Texture2D?> _cardArt = [];
    static bool s_debugCardsGiven;

    GameTables.Table? CardTable()
    {
        if (!_cardTried)
        {
            _cardTried = true;
            var dir = CardDir;
            var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "*_db.json").FirstOrDefault() : null;
            _cardDb = file != null ? GameTables.FromJson(GameFiles.ReadAllText(file)) : null;
            if (_cardDb == null) _problems.Add("falta a tabela das cartas (dw4 fase-exportar → <fases>/cartas): loja de cartas desligada");
        }
        return _cardDb != null && _cardDb.Contains("card") ? _cardDb["card"] : null;
    }

    string CardDir => Path.Combine(_dir, "..", "cartas");

    /// <summary>Arte da carta n (0 = verso, 1..50 = linha n − 1 da tabela; FATO: CARDINFO j_shcardNN e imagens cardNN.dds do #621).</summary>
    Texture2D? CardArt(int n)
    {
        if (_cardArt.TryGetValue(n, out var tex)) return tex;
        var path = Path.Combine(CardDir, $"card{n:D2}.png");
        tex = GameFiles.Exists(path) && Image.LoadFromFile(GameFiles.GodotPath(path)) is { } img && !img.IsEmpty() ? ImageTexture.CreateFromImage(img) : null;
        return _cardArt[n] = tex;
    }

    List<CardEntry> CardEntries(GameTables.Table card)
    {
        var list = Ram.Players[0].Cards;
        return Enumerable.Range(0, card.Rows).Select(r => new CardEntry(card.Text(r, "NAME"), Math.Clamp(card.Int(r, "RARE"), 0, 2),
            card.Int(r, "KAKURITSU"), r < list.Length && list[r] != 0, CardArt(r + 1))).ToList();
    }

    void OpenCardShop()
    {
        var card = CardTable();
        if (card == null) { SaveWindow("Loja de cartas: falta a tabela das cartas (dw4 fase-exportar).", [("OK", CloseSaveWindow)]); return; }
        GiveDebugCards();
        if (_saveLayer != null) { _saveLayer.QueueFree(); _saveLayer = null; }
        CloseListScreen();
        _artMenuOpen = true;
        if (_padEnabled) { _padEnabled = false; _listLockedPad = true; }
        if (_quickHud != null) _quickHud.Visible = false;
        if (_techBar != null) _techBar.Visible = false;
        if (_cardScreen == null)
        {
            _cardLayer ??= new CanvasLayer { Layer = 18 }; // por cima do HUD (atalhos, técnicas, vida)
            if (!_cardLayer.IsInsideTree()) AddChild(_cardLayer);
            _cardScreen = new CardAlbumScreen(GameFont(), CardArt(0), Touch);
            _cardScreen.OpenPackRequested += () => Callable.From(OpenPack).CallDeferred();
            _cardScreen.LeaveRequested += () => Callable.From(LeaveCardShop).CallDeferred();
            _cardLayer.AddChild(_cardScreen);
        }
        _cardScreen.SetData(CardEntries(card), _inventory.Disks(CardPack.PackRow));
        if (DiagNpc) GD.Print($"[cartas] aberto: {_inventory.Disks(CardPack.PackRow)} B. Pack, {Ram.Players[0].Cards.Count(b => b != 0)} cartas");
    }

    void CloseCardScreen()
    {
        if (_cardScreen == null) return;
        _cardScreen.QueueFree();
        _cardScreen = null;
        if (_listLockedPad) { _padEnabled = true; _listLockedPad = false; }
    }

    // FATO (beNDMWStatus, 0x8034FA10): ao fechar a janela SUBWND tipo 10 (CARD), se não é STAGECTL ISRESETSTART, o jogo põe
    // PLAYERPOINT SETGENERATEPOS 4 e manda NEXTSTAGE "stagML12": volta à Central Area pela entrada 4.
    void LeaveCardShop()
    {
        if (_cardScreen == null && !_artMenuOpen) return;
        CloseSaveWindow();
        _nextEntry = 4;
        NextStage("stagML12");
    }

    void OpenPack()
    {
        var card = CardTable();
        if (card == null || _cardScreen == null) return;
        if (_inventory.Disks(CardPack.PackRow) <= 0) { _cardScreen.SetMessage("Você não tem B. Pack.", true); return; }
        var list = Ram.Players[0].Cards;
        var before = list.ToArray();
        var rows = CardPack.Open(_inventory, card, list, _mt);
        // "NOVA!" só na primeira vez que a carta sai (duas iguais no mesmo pacote: a segunda é repetida).
        var seen = new HashSet<int>();
        var fresh = rows.Select(r => r >= 0 && r < before.Length && before[r] == 0 && seen.Add(r)).ToArray();
        ScriptLog($"B. Pack: {string.Join(", ", rows)}");
        _cardScreen.SetData(CardEntries(card), _inventory.Disks(CardPack.PackRow));
        int news = fresh.Count(f => f);
        _cardScreen.SetMessage(news == 0 ? "Nenhuma carta nova desta vez." : news == 1 ? "1 carta nova no álbum!" : $"{news} cartas novas no álbum!", news == 0);
        _cardScreen.ShowDrawn(rows, fresh);
    }

    /// <summary>
    /// DEPURAÇÃO (só no build Debug): DW4_CARTAS=n dá n B. Packs e, se o álbum estiver vazio, umas cartas de cada raridade (para testar
    /// a tela sem juntar pacotes). Uma vez por execução.
    /// </summary>
    void GiveDebugCards()
    {
        if (s_debugCardsGiven || !OS.IsDebugBuild() || !int.TryParse(OS.GetEnvironment("DW4_CARTAS"), out int packs) || packs <= 0) return;
        s_debugCardsGiven = true;
        for (int i = 0; i < packs; i++) _inventory.Add(new ItemRef("conItem", CardPack.PackRow));
        var list = Ram.Players[0].Cards;
        if (list.All(b => b == 0))
            foreach (int r in new[] { 0, 1, 2, 4, 5, 7, 9, 13, 17, 20, 22, 24, 25, 28, 33, 36, 41, 45, 49 })
                if (r < list.Length) list[r] = 1;
    }
}
