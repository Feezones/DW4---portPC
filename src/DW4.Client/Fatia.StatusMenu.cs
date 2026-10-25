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
    readonly List<Label> _menuLabels = [];

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
        int hover = e is InputEventMouseMotion motion ? MenuItemAt(motion.Position) : -1;
        if (hover >= 0 && hover != _menuCursor)
        {
            _menuCursor = hover;
            UpdateMenuText();
            return true;
        }
        if (e is InputEventMouseButton { Pressed: true } click)
        {
            if (click.ButtonIndex == MouseButton.Right) CloseMenu();
            else if (click.ButtonIndex == MouseButton.Left && MenuItemAt(click.Position) is int hit && hit >= 0)
            {
                _menuCursor = hit;
                UpdateMenuText();
                ScriptLog($"menu: {Text(StatusText(15, _menuCursor, 26))} (submenu ainda não portado)");
            }
            return true;
        }
        if (Key(Godot.Key.Tab, Godot.Key.Escape, Godot.Key.K) || Pad(JoyButton.Start, JoyButton.B)) CloseMenu();
        else if (Key(Godot.Key.W, Godot.Key.Up) || Pad(JoyButton.DpadUp)) { _menuCursor = (_menuCursor + 2) % 3; UpdateMenuText(); }
        else if (Key(Godot.Key.S, Godot.Key.Down) || Pad(JoyButton.DpadDown)) { _menuCursor = (_menuCursor + 1) % 3; UpdateMenuText(); }
        else if (Key(Godot.Key.J, Godot.Key.Enter) || Pad(JoyButton.A))
            ScriptLog($"menu: {Text(StatusText(15, _menuCursor, 26))} (submenu ainda não portado)");
        return true;
    }

    // Topo das linhas do i_stmenu01 (y para cima) e a faixa x da moldura (−110..54).
    static readonly float[] MenuRows = [212, 189, 167];

    // DECISÃO DO PORT PC (docs/decisoes-port-pc.md): o mouse aponta os itens do menu, clique esquerdo escolhe, direito cancela.
    // Mouse da janela → ponto da tela 640 × 480 do menu (mesma escala do TextureRect, KeepAspectCentered) → linha do menu.
    // Índices de texto de cada tela do menu, do txt_status.lua da fase (be:TXTgetCallNo = 0 ajuda, 3 atributos, 15 menu): os
    // pacotes de texto de cada fase têm outra ordem (FATO: na dng1 dá I<11..13>, I<14..25>, I<26..28>; no stag0101, outros).
    // Sem o script na fase, usa o da pasta comum; sem nenhum, os índices da dng1.
    readonly Dictionary<int, List<int>> _statusTexts = [];
    int StatusText(int callNo, int i, int dng1Start)
    {
        if (!_statusTexts.TryGetValue(callNo, out var list))
        {
            var common = Path.Combine(_common, "lua", "txt_status.lua");
            var got = _stage?.RunText("txt_status.lua", callNo, File.Exists(common) ? File.ReadAllBytes(common) : null) ?? [];
            _statusTexts[callNo] = list = got.Select(m => m.Args.LastOrDefault() is { Kind: 'I', Numbers.Length: > 0 } k ? (int)k.Numbers[0] : -1).ToList();
        }
        return i < list.Count && list[i] >= 0 ? list[i] : dng1Start + i;
    }

    int MenuItemAt(Vector2 mouse)
    {
        var size = GetViewport().GetVisibleRect().Size;
        float s = Math.Min(size.X / 640, size.Y / 480);
        var p = (mouse - new Vector2((size.X - 640 * s) / 2, (size.Y - 480 * s) / 2)) / s;
        float x = p.X - 320, y = 240 - p.Y;
        if (x < -110 || x > 54) return -1;
        for (int i = 0; i < MenuRows.Length; i++)
            if (y <= MenuRows[i] + 2 && y > MenuRows[i] - 21) return i;
        return -1;
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
                // Ordem de desenho pela ordem de geração (fundo primeiro) e, dentro do ator, pela ordem do grafo.
                var actor = new HudActor(_menuData, root, slot: 0, drawOrder: _menuActors.Count * 20);
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
                // HIPÓTESE: o fundo (i_stback, esfera com projeção própria) é desenhado antes e por trás das janelas; aqui
                // ele vai para trás em z (câmera ortográfica: o tamanho não muda) para não cobrir os outros atores.
                if (name == "i_stback") actor.Root.Position = new Vector3(0, 0, -2000);
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
        _menuVp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true,
            // Os atores do menu têm z de −460 (fundo) a +10 (janelas): com a câmera em z 0 os de z > 0 ficavam atrás dela.
            Position = new Vector3(0, 0, 1000) });
        // Fundo escuro: o menu esconde o cenário (LAYERCTL BG01..GM03 DISPOFF nas strings do StatusCtrl00).
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _menuRect = new TextureRect { Texture = _menuVp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        _menuRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // Textos dentro da própria tela 640 × 480 do menu (escalam com ela). Posições pelas malhas dos atores (FATO, origem no
        // centro, y para cima); a linha exata de cada texto é HIPÓTESE (os nós de fonte STWINFONT* ainda não foram lidos).
        _menuText = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(640, 480) };
        _menuVp.AddChild(_menuText);
        _menuLayer.AddChild(_menuVp);
        _menuLayer.AddChild(dim);
        _menuLayer.AddChild(_menuRect);
        AddChild(_menuLayer);
    }

    // Ponto da tela do menu (x à direita, y para cima, origem no centro) → pixel da SubViewport.
    static Vector2 MenuPx(float x, float y) => new(320 + x, 240 - y);

    void MenuLabel(string text, float x, float y, int size = 13, HorizontalAlignment align = HorizontalAlignment.Left, float width = 0)
    {
        var l = new Label { Text = text, Position = MenuPx(x, y), HorizontalAlignment = align };
        if (width > 0) l.Size = new Vector2(width, 0);
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 3);
        _menuText!.AddChild(l);
        _menuLabels.Add(l);
    }

    void UpdateMenuText()
    {
        if (_menuText == null) return;
        _menuText.Visible = true;
        foreach (var l in _menuLabels) l.QueueFree();
        _menuLabels.Clear();
        string V(Func<Fighter, int> f) => _pf != null ? f(_pf).ToString() : "-";
        // FATO: rótulos I<14..25>: Strength, Defense, Wisdom, Spirit, Speed, 4 resistências, Data, BITS, Next Level.
        var values = new[]
        {
            V(f => f.Tsuyosa), V(f => f.Mamori), V(f => f.Kashikosa), V(f => f.Seishin), V(f => f.Subayasa),
            V(f => f.Res(0)), V(f => f.Res(1)), V(f => f.Res(2)), V(f => f.Res(3)),
            "-", "-", (PlayerGrowth.NextExp(_level) - _exp).ToString(),
        };
        // Posições pelas malhas do i_stbase (geometria 'sen' #33810 e 'mojiL' #33812) e do i_stmenu01 (L3, 'sen' #33644), em
        // coordenadas da tela 640×480 com origem no centro e y para cima; MenuLabel recebe o canto superior esquerdo do rótulo.
        // Um rótulo de 12 pt tem ~17 de altura: o topo fica 17 acima do sublinhado para o texto pousar nele.
        // Nível: "Lv." do mojiL em x −244..−222, y 189..207; linha do número em x −228..−176, y 185..189.
        MenuLabel(_level.ToString(), -220, 207, 14, HorizontalAlignment.Right, 44);
        // Nome: moldura em x −306..−152, y 133..151.
        MenuLabel("Guilmon", -296, 151, 12);
        // HP/MP: "HP" do mojiL em y 105..118 e "MP" em 87..100 (x −300..−282); linhas em y 105..109 e 87..91, atual em
        // x −282..−234, "/" em −221..−214, máximo em −213..−165.
        MenuLabel(_hp.ToString(), -282, 124, 12, HorizontalAlignment.Right, 48);
        MenuLabel((_pf?.MaxHp ?? 0).ToString(), -213, 124, 12, HorizontalAlignment.Right, 48);
        MenuLabel(_mp.ToString(), -282, 106, 12, HorizontalAlignment.Right, 48);
        MenuLabel((_pf?.MaxMp ?? 0).ToString(), -213, 106, 12, HorizontalAlignment.Right, 48);
        // Atributos: moldura em y −106..77 com 10 sublinhados em x −304..−155, topos em y 62, 44, … −100 (passo 18): 9 atributos
        // e "¶ Data". Moldura de baixo (y −173..−114): sublinhados com topo em −129 (BITS) e −149 (Next Level).
        float[] tops = [62, 44, 26, 8, -10, -28, -46, -64, -82, -100, -129, -149];
        for (int i = 0; i < values.Length; i++)
        {
            float y = tops[i] + 17;
            MenuLabel(Text(StatusText(3, i, 14))?.Replace("\r\n", " ") ?? "", -292, y, 12);
            MenuLabel(values[i], -262, y, 12, HorizontalAlignment.Right, 95);
        }
        // i_stmenu01 (L3, SQ_3LINEUPOPEN): moldura em x −110..54, y 140..216; separadores em y 188..192 e 166..170 (x −84..48).
        // Linhas: topo 212, 189 e 167.
        float[] rows = MenuRows;
        for (int i = 0; i < 3; i++)
            MenuLabel((i == _menuCursor ? "▶ " : "    ") + Text(StatusText(15, i, 26)), -100, rows[i], 13);
        // i_staccount (win01: x 128..312, y 156..216): nome.
        MenuLabel("Guilmon", 140, 200, 13);
        MenuLabel(string.Join("    ", Enumerable.Range(0, 3).Select(i => Text(StatusText(0, i, 11))?.Replace("\r\n", " "))), -320, -212, 13, HorizontalAlignment.Center, 640);
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
