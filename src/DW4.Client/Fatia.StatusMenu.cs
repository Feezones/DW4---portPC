using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Menu de status, tela base (beNDMWStatusCtrl00 no original: código nativo, reescrito aqui). Levantamento:
/// docs/gameplay/fase4-menu-status.md. Atores e sequências de &lt;fatia&gt;\menu
/// (dw4 igb-hud ndmw.afs 179 area.afs 9 &lt;fatia&gt;\menu --atores=… e um igb-seq por ator); textos pelo índice da textos.json.
/// FATO: atores e sequências de abertura/fecho; textos I&lt;11..13&gt; (ajuda), I&lt;14..25&gt; (atributos), I&lt;26..28&gt; (menu).
/// HIPÓTESES: Tab/Start abre, Tab/Esc/B fecha, a fase fica pausada; atores na origem da tela 640 × 480 (os GENERATE nativos
/// não foram lidos); i_stmenu01 abre com SQ_3LINEUPOPEN.
/// DECISÃO DA FATIA: textos com a fonte da Godot em posições aproximadas; cursor como "▶"; submenus ainda não portados.
/// </summary>
public partial class Fatia
{
    sealed record MenuActor(HudActor Actor, ObjectSequencer? Seq);

    HudData? _menuData;
    bool _menuTried, _menuOpen, _menuClosing;
    float _menuCloseT;
    int _menuCursor;
    readonly List<MenuActor> _menuActors = [];
    CanvasLayer? _menuLayer;
    SubViewport? _menuVp;
    TextureRect? _menuRect;
    Control? _menuText;
    Label? _menuStats, _menuItems, _menuHelp;

    // Atores da tela base e a sequência de abertura de cada um (FATO pelas strings do StatusCtrl00 e o igb-seq).
    static readonly (string Actor, string Open)[] MenuBase =
    [
        ("i_stback", "SQ_OPEN"), ("i_stbase", "SQ_LOPEN"), ("i_sttitle", "SQ_OPEN"), ("i_staccount", "SQ_OPEN"),
        ("i_stdigiface", "SQ_girumonOPEN"), ("i_stmenu01", "SQ_3LINEUPOPEN"),
    ];

    bool MenuInput(InputEvent e)
    {
        bool Key(params Key[] keys) => e is InputEventKey { Pressed: true, Echo: false } k && keys.Contains(k.Keycode);
        bool Pad(params JoyButton[] buttons) => e is InputEventJoypadButton { Pressed: true } b && buttons.Contains(b.ButtonIndex);
        if (!_menuOpen)
        {
            if (Key(Godot.Key.Tab) || Pad(JoyButton.Start)) { OpenMenu(); return true; }
            return false;
        }
        if (_menuClosing) return true;
        if (Key(Godot.Key.Tab, Godot.Key.Escape, Godot.Key.K) || Pad(JoyButton.Start, JoyButton.B)) CloseMenu();
        else if (Key(Godot.Key.W, Godot.Key.Up) || Pad(JoyButton.DpadUp)) { _menuCursor = (_menuCursor + 2) % 3; UpdateMenuText(); }
        else if (Key(Godot.Key.S, Godot.Key.Down) || Pad(JoyButton.DpadDown)) { _menuCursor = (_menuCursor + 1) % 3; UpdateMenuText(); }
        else if (Key(Godot.Key.J, Godot.Key.Enter) || Pad(JoyButton.A))
            ScriptLog($"menu: {Text(26 + _menuCursor)} (submenu ainda não portado)");
        return true;
    }

    void OpenMenu()
    {
        if (!_menuTried)
        {
            _menuTried = true;
            _menuData = HudData.Load(Path.Combine(_dir, "menu"), _problems, "*_hud.json",
                "falta o *_hud.json do menu (dw4 igb-hud ndmw.afs 179 area.afs 9 <fatia>\\menu --atores=i_stbase,…): menu só em texto");
            BuildMenu();
        }
        _menuOpen = true;
        _menuClosing = false;
        _menuCursor = 0;
        _menuLayer!.Visible = true;
        foreach (var a in _menuActors) a.Actor.Root.QueueFree();
        _menuActors.Clear();
        if (_menuData != null && _menuVp != null)
            foreach (var (name, open) in MenuBase)
            {
                if (!_menuData.Actors.TryGetValue(name, out int root)) continue;
                var actor = new HudActor(_menuData, root, slot: 0);
                var seqDir = Path.Combine(_dir, "menu", name);
                var seqFile = Directory.Exists(seqDir) ? Directory.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
                ObjectSequencer? seq = null;
                if (seqFile != null)
                {
                    var json = File.ReadAllText(seqFile);
                    seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
                    seq.Start(open);
                }
                _menuVp.AddChild(actor.Root);
                _menuActors.Add(new MenuActor(actor, seq));
            }
        UpdateMenuText();
    }

    void CloseMenu()
    {
        _menuClosing = true;
        _menuCloseT = 0;
        foreach (var a in _menuActors) a.Seq?.Start("SQ_CLOSE");
        _menuText!.Visible = false;
    }

    void BuildMenu()
    {
        _menuLayer = new CanvasLayer { Layer = 5 };
        _menuVp = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _menuVp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true });
        // Fundo escuro: o menu esconde o cenário (LAYERCTL BG01..GM03 DISPOFF nas strings do StatusCtrl00).
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuRect = new TextureRect { Texture = _menuVp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        _menuRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuText = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _menuText.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Label L(Vector2 pos, int size)
        {
            var l = new Label { Position = pos };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            l.AddThemeConstantOverride("outline_size", 6);
            _menuText!.AddChild(l);
            return l;
        }
        _menuStats = L(new Vector2(60, 90), 22);
        _menuItems = L(new Vector2(0, 90), 26);
        _menuHelp = L(new Vector2(60, 0), 20);
        _menuLayer.AddChild(_menuVp);
        _menuLayer.AddChild(dim);
        _menuLayer.AddChild(_menuRect);
        _menuLayer.AddChild(_menuText);
        AddChild(_menuLayer);
    }

    void UpdateMenuText()
    {
        if (_menuStats == null || _menuItems == null || _menuHelp == null) return;
        _menuText!.Visible = true;
        string V(Func<Fighter, int> f) => _pf != null ? f(_pf).ToString() : "-";
        // FATO: rótulos I<14..25>: Strength, Defense, Wisdom, Spirit, Speed, 4 resistências, Data, BITS, Next Level.
        var values = new[]
        {
            V(f => f.Tsuyosa), V(f => f.Mamori), V(f => f.Kashikosa), V(f => f.Seishin), V(f => f.Subayasa),
            V(f => f.Res(0)), V(f => f.Res(1)), V(f => f.Res(2)), V(f => f.Res(3)),
            "-", "-", (PlayerGrowth.NextExp(_level) - _exp).ToString(),
        };
        _menuStats.Text = $"Guilmon  Lv {_level}   HP {_hp}/{_pf?.MaxHp ?? 0}   MP {_mp}/{_pf?.MaxMp ?? 0}\n\n" +
            string.Join("\n", values.Select((v, i) => $"{Text(14 + i)?.Replace("\r\n", " ")}   {v}"));
        _menuItems.Text = string.Join("\n\n", Enumerable.Range(0, 3).Select(i => (i == _menuCursor ? "▶ " : "   ") + Text(26 + i)));
        _menuHelp.Text = string.Join("    ", Enumerable.Range(11, 3).Select(i => Text(i)?.Replace("\r\n", " ")));
        var size = GetViewport().GetVisibleRect().Size;
        _menuItems.Position = new Vector2(size.X - 320, 120);
        _menuHelp.Position = new Vector2(60, size.Y - 60);
    }

    /// <summary>Com o menu aberto a fase fica parada (HIPÓTESE): só o menu anda. true = pular o resto do quadro.</summary>
    bool TickMenu(float dt)
    {
        if (!_menuOpen) return false;
        foreach (var a in _menuActors)
        {
            a.Seq?.Tick(dt);
            if (a.Seq != null)
            {
                a.Actor.Time = a.Seq.Frame / 60f;
                a.Actor.Root.Visible = !a.Seq.Erased && a.Seq.Alpha > 0;
            }
            a.Actor.Update();
        }
        if (_menuClosing && (_menuCloseT += dt) > 10 / 60f) // SQ_CLOSE: 2 a 5 quadros
        {
            _menuOpen = _menuClosing = false;
            _menuLayer!.Visible = false;
        }
        return true;
    }
}
