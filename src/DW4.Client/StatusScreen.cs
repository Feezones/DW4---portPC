using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>O que o menu de status lê e muda do jogador (a Fatia preenche; a prévia de depuração usa um jogador de teste).</summary>
public sealed class StatusContext
{
    public required GameTables Db { get; init; }
    public required Func<Inventory> Inventory { get; init; }
    public required Func<Equipment?> Equipment { get; init; }
    /// <summary>Troca o equipamento (a Fatia refaz o RECALC, a arma na mão e limita HP/MP).</summary>
    public required Action<Equipment> SetEquipment { get; init; }
    /// <summary>Atributos que o jogador teria com outro equipamento (comparação antes de equipar).</summary>
    public required Func<Equipment, Fighter> Preview { get; init; }
    public required Func<Fighter?> Player { get; init; }
    public required Func<(int Level, int Hp, int Mp, long Exp)> Numbers { get; init; }
    public required string Name { get; init; }
    public required int ShinkaRow { get; init; }
    public required Func<IReadOnlyList<int>> Skill { get; init; }
    public Evolution? Forms { get; init; }
    /// <summary>Texto do jogo pela chave (mswkPRG0003…) no textos.json da fase; null sem texto.</summary>
    public required Func<string, string?> TextByKey { get; init; }
    public BitmapFont? Font { get; init; }
    /// <summary>charaicon.png (rostos 64 × 64 em 4 × 4, na ordem da shinka); null sem a arte.</summary>
    public Texture2D? Faces { get; init; }
    public Action<string>? Log { get; init; }
}

/// <summary>
/// Menu de status (TAB) com layout próprio no estilo do original (DECISÃO DO PORT, pedida pelo usuário em 2026-11-02: "retrabalho
/// no menu ao apertar TAB, todos os seus dados e funcionalidades"), como as telas de vagas e criação (MainMenu.Slots.cs).
/// Estrutura e textos do jogo (FATO, txt_status.lua I&lt;11..112&gt;): coluna de status (Lv, nome, HP/MP, 5 atributos, 4 resistências,
/// ¶ Data, BITS, Next Level), menu Equipment / Folder / Techniques e as subtelas: Weapon Slot (3 soquetes e Mods), Armor Slot, Sub
/// Slot; Device / Key / Disk / BITS Folder com Sort e Break (sem o Give, a pedido do usuário); Digivolve Tech, Skill Tech e MP Tech. Visual da captura do menu
/// antigo (conferir_status.png): janelas azul-escuras de borda lilás, cantos redondos, linhas sublinhadas, faixas rosa.
/// Regras em <see cref="EquipRules"/>. Desenho com controles da Godot e a fonte do jogo, escala pela altura da janela (base 720).
/// </summary>
public partial class StatusScreen : Control
{
    const float BaseHeight = 720, BaseWidth = 1280;
    static readonly Color Navy = new(0.05f, 0.06f, 0.22f, 0.94f), Lilac = new(0.66f, 0.68f, 1f), Pink = new(0.95f, 0.42f, 0.48f),
        Line = new(0.78f, 0.78f, 0.9f, 0.55f), Dim = new(0.62f, 0.62f, 0.75f), Gold = new(1f, 0.85f, 0.35f), Good = new(0.45f, 1f, 0.55f),
        Bad = new(1f, 0.45f, 0.45f), Focus = new(0.22f, 0.27f, 0.68f, 0.85f);

    readonly StatusContext _c;
    Control _scaled = null!;
    VBoxContainer _status = null!, _left = null!, _right = null!, _info = null!;
    HBoxContainer _tabs = null!;
    ScrollContainer _rightScroll = null!;
    Label _message = null!, _rightTitle = null!;
    readonly List<Button> _leftButtons = [], _rightButtons = [];
    int _tab, _leftCursor, _rightCursor;
    bool _inRight;
    string _msg = "";
    // Break pedindo confirmação (Yes/No, I<55..57>).
    ItemRef? _breakAsk;
    int _breakIndex = -1;

    public event Action? Closed;

    public StatusScreen(StatusContext context) => _c = context;

    // ---------- textos ----------

    // Os números dos textos abaixo são os índices da dng1 (beUnicodeObjList do area #9), só como nome; o texto é buscado pela
    // chave mswkPRG, porque cada fase tem o pacote de textos em outra ordem (FATO: no lobby o índice 26 é uma fala de NPC).
    static readonly Dictionary<int, string> Keys = new() { [11] = "0000", [12] = "0001", [13] = "0002", [14] = "0003", [15] = "0004", [16] = "0005", [17] = "0006", [18] = "0007", [19] = "0008", [20] = "0009", [21] = "0010", [22] = "0011", [23] = "0012", [24] = "0013", [25] = "0014", [26] = "0015", [27] = "0016", [28] = "0017", [29] = "0031", [30] = "0032", [31] = "0033", [32] = "0034", [33] = "0035", [34] = "0036", [35] = "0037", [36] = "0039", [37] = "0043", [38] = "0044", [39] = "0045", [40] = "0046", [41] = "0047", [42] = "0048", [43] = "0049", [44] = "0061", [45] = "0062", [46] = "0063", [47] = "0064", [48] = "0085", [49] = "0065", [50] = "0066", [51] = "0067", [52] = "0073", [53] = "0075", [54] = "0077", [55] = "0078", [56] = "0079", [57] = "0080", [58] = "0081", [59] = "0082", [60] = "0083", [61] = "0084", [62] = "0087", [63] = "0088", [64] = "0091", [65] = "0093", [66] = "0094", [67] = "0096", [68] = "0098", [69] = "0099", [70] = "0100", [71] = "0101", [72] = "0102", [73] = "0103", [74] = "0104", [75] = "0105", [76] = "0106", [77] = "0107", [78] = "0108", [79] = "0109", [80] = "0111", [81] = "0112", [82] = "0113", [83] = "0114", [84] = "0115", [85] = "0116", [86] = "0117", [87] = "0118", [88] = "0119", [89] = "0120", [90] = "0124", [91] = "0127", [92] = "0128", [93] = "0130", [94] = "0131", [95] = "0132", [96] = "0133", [97] = "0134", [98] = "0135", [99] = "0136", [100] = "0137", [101] = "0138", [102] = "0139", [103] = "0140", [104] = "0141", [105] = "0142", [106] = "0143", [107] = "0144", [108] = "0145", [109] = "0146", [110] = "0147", [111] = "0148", [112] = "0149" };

    string T(int index, string fallback) => TextOf(_c.TextByKey, index, fallback);

    /// <summary>Texto do menu pelo índice da dng1 (as lojas usam os mesmos nomes de atributo e skill).</summary>
    internal static string TextOf(Func<string, string?> byKey, int index, string fallback) =>
        (Keys.TryGetValue(index, out var key) && byKey("mswkPRG" + key) is { Length: > 0 } t ? t : fallback).Replace("\r\n", "\n").Trim();

    // Os textos de ajuda começam com ícones do GameCube (≡∇, ≪, ≫): a barra de ajuda põe o botão do aparelho no lugar.
    string Help(int index, string fallback) => new(T(index, fallback).SkipWhile(ch => !char.IsAsciiLetter(ch)).ToArray());

    static string Desc(string txt) => string.Join("\n", txt.Split('/').Select(x => x.Trim()).Where(x => x.Length > 0));

    string ItemName(ItemRef i) => _c.Db.Contains(i.Table) ? _c.Db[i.Table].Text(i.Row, "NAME") : $"{i.Table} {i.Row}";

    static readonly string[] SkillNames = ["Blunt", "Slash", "Stab", "Bash", "Shot", "Crush", "Blast", "Heal", "Force"];
    string SkillName(int k) => k is >= 0 and < 9 ? T(71 + k, SkillNames[k]) : "-";

    // ---------- montagem ----------

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0.01f, 0.01f, 0.05f, 0.88f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);
        // Fundo: o i_stback do jogo é uma esfera azul escura com um brilho no meio; aqui um degradê radial.
        var glow = new Gradient { Offsets = [0f, 0.6f, 1f], Colors = [new Color(0.16f, 0.18f, 0.48f, 0.97f), new Color(0.07f, 0.08f, 0.27f, 0.98f), new Color(0.02f, 0.02f, 0.1f, 0.98f)] };
        var back = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = glow, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.55f, 0.5f), FillTo = new Vector2(0.55f, -0.2f), Width = 256, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        back.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(back);

        _scaled = new Control { Theme = BuildTheme(), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_scaled);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 16);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _scaled.AddChild(margin);
        var page = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        page.AddThemeConstantOverride("separation", 4);
        margin.AddChild(page);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 12);
        page.AddChild(body);

        _status = new VBoxContainer { CustomMinimumSize = new Vector2(310, 0) };
        _status.AddThemeConstantOverride("separation", 6);
        body.AddChild(_status);

        var main = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        main.AddThemeConstantOverride("separation", 12);
        body.AddChild(main);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 16);
        main.AddChild(top);
        var tabsPanel = Panel(new MarginContainer(), pad: 6);
        tabsPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _tabs = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _tabs.AddThemeConstantOverride("separation", 10);
        ((MarginContainer)tabsPanel.GetChild(0)).AddChild(_tabs);
        top.AddChild(tabsPanel);
        top.AddChild(NamePanel());

        var content = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 14);
        main.AddChild(content);
        var leftPanel = Panel(new VBoxContainer(), pad: 10);
        leftPanel.CustomMinimumSize = new Vector2(290, 0);
        _left = (VBoxContainer)leftPanel.GetChild(0);
        _left.AddThemeConstantOverride("separation", 4);
        content.AddChild(leftPanel);
        var rightCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rightCol.AddThemeConstantOverride("separation", 12);
        content.AddChild(rightCol);
        var rightPanel = Panel(new VBoxContainer(), pad: 10);
        rightPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        var rightBox = (VBoxContainer)rightPanel.GetChild(0);
        _rightTitle = Text("", 20, Gold);
        _rightTitle.ClipText = true;
        rightBox.AddChild(_rightTitle);
        _rightScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _right.AddThemeConstantOverride("separation", 2);
        _rightScroll.AddChild(_right);
        rightBox.AddChild(_rightScroll);
        rightCol.AddChild(rightPanel);
        var infoPanel = Panel(new VBoxContainer(), pad: 12);
        infoPanel.CustomMinimumSize = new Vector2(0, 186);
        _info = (VBoxContainer)infoPanel.GetChild(0);
        _info.AddThemeConstantOverride("separation", 4);
        rightCol.AddChild(infoPanel);

        _message = Text("", 18, Pink);
        _message.ClipText = true;
        page.AddChild(_message);
        page.AddChild(HelpBar());
        _message.HorizontalAlignment = HorizontalAlignment.Center;

        BuildTabs();
        RefreshAll();
        FitScreen();
    }

    Theme BuildTheme()
    {
        var theme = new Theme();
        if (_c.Font?.ToFontFile("MINIMUM") is { } gf) { theme.DefaultFont = gf; theme.DefaultFontSize = 22; }
        theme.SetColor("font_outline_color", "Label", new Color(0.02f, 0.02f, 0.12f));
        theme.SetConstant("outline_size", "Label", 4);
        var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        flat.SetContentMarginAll(4);
        flat.ContentMarginLeft = 10;
        theme.SetStylebox("normal", "Button", flat);
        theme.SetStylebox("disabled", "Button", flat);
        var hover = Box(new Color(0.18f, 0.2f, 0.5f, 0.6f), new Color(0, 0, 0, 0), 0, 8);
        theme.SetStylebox("hover", "Button", hover);
        var focus = Box(Focus, Gold, 2, 8);
        theme.SetStylebox("focus", "Button", focus);
        theme.SetStylebox("pressed", "Button", focus);
        theme.SetColor("font_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Gold);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Gold);
        theme.SetColor("font_outline_color", "Button", new Color(0.02f, 0.02f, 0.12f));
        theme.SetConstant("outline_size", "Button", 4);
        theme.SetStylebox("panel", "PanelContainer", Box(Navy, Lilac, 3, 14));
        return theme;
    }

    static StyleBoxFlat Box(Color bg, Color border, int width, int radius)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 8 };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(4);
        return s;
    }

    static PanelContainer Panel(Control child, int pad)
    {
        var p = new PanelContainer();
        var s = Box(Navy, Lilac, 3, 14);
        s.SetContentMarginAll(pad);
        p.AddThemeStyleboxOverride("panel", s);
        p.AddChild(child);
        return p;
    }

    static Label Text(string text, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    // Linha sublinhada do jogo: rótulo à esquerda, valor à direita, traço claro embaixo.
    static Control Row(string label, string value, int size = 22, Color? valueColor = null)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var l = Text(label, size);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.ClipText = true;
        row.AddChild(l);
        row.AddChild(Text(value, size, valueColor, HorizontalAlignment.Right));
        box.AddChild(row);
        box.AddChild(new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore });
        return box;
    }

    // Faixa rosa listrada dos cantos das janelas do jogo.
    static Control Stripes(float width)
    {
        var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 3);
        box.AddChild(new ColorRect { Color = Pink, CustomMinimumSize = new Vector2(width, 8), MouseFilter = MouseFilterEnum.Ignore });
        for (int i = 0; i < 3; i++) box.AddChild(new ColorRect { Color = Pink, CustomMinimumSize = new Vector2(6, 8), MouseFilter = MouseFilterEnum.Ignore });
        return box;
    }

    Control NamePanel()
    {
        var panel = Panel(new HBoxContainer(), pad: 8);
        panel.CustomMinimumSize = new Vector2(250, 0);
        var row = (HBoxContainer)panel.GetChild(0);
        row.AddThemeConstantOverride("separation", 10);
        if (_c.Faces != null && _c.ShinkaRow is >= 0 and < 16)
            row.AddChild(new TextureRect
            {
                Texture = new AtlasTexture { Atlas = _c.Faces, Region = new Rect2(_c.ShinkaRow % 4 * 64, _c.ShinkaRow / 4 * 64, 64, 64) },
                CustomMinimumSize = new Vector2(56, 56), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        var names = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        names.AddThemeConstantOverride("separation", 0);
        names.AddChild(Text(_c.Name, 24));
        names.AddChild(Text(FormName(_c.ShinkaRow), 18, Dim));
        row.AddChild(names);
        return panel;
    }

    string FormName(int d) => d >= 0 && _c.Db.Contains("shinka") && d < _c.Db["shinka"].Rows ? _c.Db["shinka"].Text(d, "NAME") : "-";

    void BuildTabs()
    {
        string[] names = [T(26, "Equipment"), T(27, "Folder"), T(28, "Techniques")];
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var b = new Button { Text = names[i], CustomMinimumSize = new Vector2(150, 40), FocusMode = FocusModeEnum.None, Flat = false, ClipText = true };
            b.AddThemeFontSizeOverride("font_size", 24);
            b.Pressed += () => SetTab(k);
            _tabs.AddChild(b);
        }
    }

    Control HelpBar()
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 22);
        bool pad = DeviceIcons.Current != ButtonNames.Kind.Keyboard;
        var kind = pad ? DeviceIcons.Current : ButtonNames.Kind.Keyboard;
        var b = PortSettings.Current.Bindings;
        void Item(string? binding, string label)
        {
            var item = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            item.AddThemeConstantOverride("separation", 6);
            if (binding != null) item.AddChild(DeviceIcons.Chip(binding, kind));
            item.AddChild(Text(label, 18));
            bar.AddChild(item);
        }
        string? Bind(string action) => b.Get(pad ? InputBindings.Pad : InputBindings.Keyboard, action).FirstOrDefault();
        var arrows = Text(pad ? "Direcional" : "Setas", 18, Gold);
        var sel = new HBoxContainer();
        sel.AddThemeConstantOverride("separation", 6);
        sel.AddChild(arrows);
        sel.AddChild(Text(Help(11, "Select"), 18));
        bar.AddChild(sel);
        Item(Bind("confirmar"), Help(12, "Confirm"));
        Item(Bind("cancelar"), Help(13, "Cancel"));
        // DECISÃO DO PORT: Q/E (LB/RB no controle) trocam a aba; o botão do status fecha o menu.
        Item(pad ? "botao:9" : "tecla:Q", "");
        Item(pad ? "botao:10" : "tecla:E", "Aba");
        Item(Bind("status"), "Fechar");
        return bar;
    }

    public override void _Process(double delta) => FitScreen();

    /// <summary>DEPURAÇÃO (prévia do MainMenu): abre numa aba/estado para a captura.</summary>
    public void Preview(string what)
    {
        (_tab, _leftCursor, _inRight) = what switch
        {
            "equiparma" or "aplicado" => (0, 0, true),
            "equipchip" => (0, 3, true),
            "equiparmadura" => (0, 5, true),
            "pasta" or "quebrar" or "item" => (1, 0, true),
            "discos" => (1, 2, true),
            "tecnicas" => (2, 2, true),
            "skill" => (2, 1, true),
            "evolucao" => (2, 0, true),
            _ => (0, 0, false),
        };
        _rightCursor = what is "quebrar" or "item" ? 3 : 0;
        if (what == "quebrar") { _breakAsk = _c.Inventory().Devices[3]; _breakIndex = 3; _rightCursor = 1; }
        RefreshAll();
        if (what == "item") Callable.From(() => ItemMenu(_c.Inventory().Devices[3], 3)).CallDeferred();
        // Escolhe a 2ª arma da lista (testa o caminho do Confirm até o RECALC).
        if (what == "aplicado") Callable.From(() => _rightButtons[1].EmitSignal(BaseButton.SignalName.Pressed)).CallDeferred();
    }

    // Largura mínima do desenho (as colunas não cabem em menos): em janelas estreitas (4:3, 5:4) a escala segue a largura.
    const float MinWidth = 1200;

    void FitScreen()
    {
        var win = GetViewportRect().Size;
        if (win.Y <= 0 || win.X <= 0) return;
        float s = Math.Min(win.Y / BaseHeight, win.X / MinWidth);
        // Janelas muito largas: o conteúdo fica com no máximo ~18:9 e centralizado (como o HUD).
        float w = Math.Min(win.X / s, BaseWidth * 1.15f);
        _scaled.Scale = new Vector2(s, s);
        _scaled.Size = new Vector2(w, win.Y / s);
        _scaled.Position = new Vector2((win.X - w * s) / 2, 0);
    }

    // ---------- entrada ----------

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        DeviceIcons.Seen(e);
        bool Key(params Key[] keys) => e is InputEventKey { Pressed: true, Echo: false } k && keys.Contains(k.Keycode);
        bool Pad(params JoyButton[] buttons) => e is InputEventJoypadButton { Pressed: true } b && buttons.Contains(b.ButtonIndex);
        bool handled = true;
        if (PortInput.Pressed(e, "status") || PortInput.Pressed(e, "pausa")) Close();
        else if (PortInput.Pressed(e, "cancelar") || e.IsActionPressed("ui_cancel") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) Back();
        else if (Key(Godot.Key.Q, Godot.Key.Pageup) || Pad(JoyButton.LeftShoulder)) SetTab((_tab + 2) % 3);
        else if (Key(Godot.Key.E, Godot.Key.Pagedown) || Pad(JoyButton.RightShoulder)) SetTab((_tab + 1) % 3);
        else if (Key(Godot.Key.Tab)) { } // nunca troca o foco da Godot
        else if (PortInput.Pressed(e, "confirmar") && !e.IsActionPressed("ui_accept") && GetViewport().GuiGetFocusOwner() is Button { Disabled: false } fb) fb.EmitSignal(BaseButton.SignalName.Pressed);
        else if (_inRight && e.IsActionPressed("ui_left") && _tab != 0) { _inRight = false; RefreshAll(); }
        else if (!_inRight && e.IsActionPressed("ui_right") && _rightButtons.Count > 0 && _tab != 0) { _inRight = true; _rightCursor = 0; RefreshAll(); }
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    public void Close()
    {
        Visible = false;
        Closed?.Invoke();
        QueueFree();
    }

    void Back()
    {
        _msg = "";
        if (_breakAsk != null) { _breakAsk = null; RefreshAll(); return; }
        if (_inRight) { _inRight = false; RefreshAll(); return; }
        Close();
    }

    void SetTab(int tab)
    {
        if (tab == _tab && !_inRight) return;
        _tab = tab;
        _leftCursor = _rightCursor = 0;
        _inRight = false;
        _breakAsk = null;
        _msg = "";
        RefreshAll();
    }

    // ---------- conteúdo ----------

    void RefreshAll()
    {
        RefreshStatus();
        for (int i = 0; i < _tabs.GetChildCount(); i++)
        {
            var b = (Button)_tabs.GetChild(i);
            var on = i == _tab;
            b.AddThemeStyleboxOverride("normal", on ? Box(Focus, Gold, 2, 10) : Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 0, 10));
            b.AddThemeColorOverride("font_color", on ? Gold : Colors.White);
        }
        Clear(_left);
        Clear(_right);
        _leftButtons.Clear();
        _rightButtons.Clear();
        switch (_tab)
        {
            case 0: BuildEquipment(); break;
            case 1: BuildFolder(); break;
            default: BuildTechniques(); break;
        }
        _message.Text = _msg;
        var list = _inRight && _rightButtons.Count > 0 ? _rightButtons : _leftButtons;
        int cursor = Math.Clamp(list == _rightButtons ? _rightCursor : _leftCursor, 0, Math.Max(0, list.Count - 1));
        if (list.Count > 0) list[cursor].CallDeferred(Control.MethodName.GrabFocus);
        else ShowInfo();
    }

    static void Clear(Node box)
    {
        foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
    }

    void RefreshStatus()
    {
        Clear(_status);
        var f = _c.Player();
        var (level, hp, mp, exp) = _c.Numbers();
        var inv = _c.Inventory();
        string V(Func<Fighter, int> g) => f != null ? g(f).ToString() : "-";

        var lv = Panel(new VBoxContainer(), pad: 8);
        var lvBox = (VBoxContainer)lv.GetChild(0);
        var lvRow = new HBoxContainer();
        lvRow.AddChild(Text("Lv.", 22, Dim));
        var lvValue = Text(level.ToString(), 28, null, HorizontalAlignment.Right);
        lvValue.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lvRow.AddChild(lvValue);
        lvBox.AddChild(lvRow);
        long start = level > 1 ? PlayerGrowth.NextExp(level - 1) : 0, next = PlayerGrowth.NextExp(level);
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, next - start), Value = Math.Clamp(exp - start, 0, Math.Max(1, next - start)), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        bar.AddThemeStyleboxOverride("background", Box(new Color(0.1f, 0.1f, 0.25f), Line, 1, 4));
        bar.AddThemeStyleboxOverride("fill", Box(Pink, new Color(0, 0, 0, 0), 0, 4));
        lvBox.AddChild(bar);
        _status.AddChild(lv);

        var hpmp = Panel(new VBoxContainer(), pad: 8);
        var hb = (VBoxContainer)hpmp.GetChild(0);
        hb.AddChild(Row("HP", $"{hp} / {(f?.MaxHp ?? 0)}", 20));
        hb.AddChild(Row("MP", $"{mp} / {(f?.MaxMp ?? 0)}", 20));
        _status.AddChild(hpmp);

        var attrs = Panel(new VBoxContainer(), pad: 8);
        var ab = (VBoxContainer)attrs.GetChild(0);
        ab.AddThemeConstantOverride("separation", 1);
        // FATO: I<14..23> Strength, Defense, Wisdom, Spirit, Speed, ⊂⊃∪∩Resist (fogo, gelo, raio, trevas), ¶ Data.
        string[] values = [V(x => x.Tsuyosa), V(x => x.Mamori), V(x => x.Kashikosa), V(x => x.Seishin), V(x => x.Subayasa),
            V(x => x.Res(0)), V(x => x.Res(1)), V(x => x.Res(2)), V(x => x.Res(3)), inv.XData.ToString()];
        string[] fallback = ["Strength", "Defense", "Wisdom", "Spirit", "Speed", "⊂Resist", "⊃Resist", "∪Resist", "∩Resist", "¶ Data"];
        for (int i = 0; i < values.Length; i++) ab.AddChild(Row(T(14 + i, fallback[i]), values[i], 20));
        _status.AddChild(attrs);

        var money = Panel(new VBoxContainer(), pad: 8);
        var mb = (VBoxContainer)money.GetChild(0);
        mb.AddChild(Row(T(24, "BITS"), inv.Bit.ToString(), 20));
        mb.AddChild(Row(T(25, "Next Level"), Math.Max(0, next - exp).ToString(), 20));
        mb.AddChild(Stripes(80));
        _status.AddChild(money);
    }

    // Botão de lista: texto, valor à direita, desligado em cinza (continua focável para mostrar o motivo).
    Button ListButton(List<Button> list, string text, string value, Action onFocus, Action onPress, bool enabled = true, Color? color = null)
    {
        int index = list.Count;
        bool right = list == _rightButtons;
        var b = new Button { Text = text, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.All, ClipText = true };
        b.AddThemeFontSizeOverride("font_size", 22);
        if (!enabled) b.AddThemeColorOverride("font_color", Dim);
        else if (color is { } c) b.AddThemeColorOverride("font_color", c);
        if (value.Length > 0)
        {
            var v = Text(value, 20, enabled ? null : Dim, HorizontalAlignment.Right);
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetRight = -10;
            v.VerticalAlignment = VerticalAlignment.Center;
            b.AddChild(v);
        }
        b.FocusEntered += () =>
        {
            if (right) { _rightCursor = index; _inRight = true; } else { _leftCursor = index; if (_tab != 0) _inRight = false; }
            onFocus();
            if (right) _rightScroll.EnsureControlVisible(b);
        };
        b.MouseEntered += () => b.GrabFocus();
        b.Pressed += onPress;
        list.Add(b);
        (right ? _right : _left).AddChild(b);
        return b;
    }

    void Header(VBoxContainer box, string text)
    {
        var l = Text(text, 18, Lilac);
        box.AddChild(l);
    }

    void ShowInfo(params Control[] items)
    {
        Clear(_info);
        foreach (var i in items) _info.AddChild(i);
    }

    // Texto de várias linhas: um rótulo por linha (com "\n" dentro de um rótulo só, a fonte do jogo deixava uma linha em branco
    // entre as linhas). Os textos do jogo já vêm quebrados (\r\n ou " / ").
    Control InfoText(string text, int size = 19, Color? color = null)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 1) return Text(text, size, color);
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        foreach (var line in lines) box.AddChild(Text(line, size, color));
        return box;
    }

    // ---------- Equipment ----------

    sealed record EqEntry(EquipSlot Slot, int Socket, int Pocket);

    List<EqEntry> EquipEntries(Equipment eq)
    {
        var db = _c.Db;
        var list = new List<EqEntry>();
        for (int s = 0; s < EquipRules.Sockets; s++) list.Add(new EqEntry(EquipSlot.Weapon, s, -1));
        for (int p = 0; p < EquipRules.Pockets(db, "weapon", eq.WeaponRow); p++) list.Add(new EqEntry(EquipSlot.Weapon, -1, p));
        list.Add(new EqEntry(EquipSlot.Armor, -1, -1));
        for (int p = 0; p < EquipRules.Pockets(db, "armor1", eq.Armor1Row); p++) list.Add(new EqEntry(EquipSlot.Armor, -1, p));
        list.Add(new EqEntry(EquipSlot.Sub, -1, -1));
        return list;
    }

    // Item no lugar (null = vazio).
    static ItemRef? InSlot(Equipment eq, EqEntry e)
    {
        if (e.Pocket >= 0)
        {
            var chips = e.Slot == EquipSlot.Weapon ? eq.WeaponChips : eq.ArmorChips;
            return e.Pocket < chips.Count ? new ItemRef("abChip", chips[e.Pocket]) : null;
        }
        int row = e.Slot switch { EquipSlot.Weapon => EquipRules.SocketRows(eq)[e.Socket], EquipSlot.Armor => eq.Armor1Row, _ => eq.Armor2Row };
        return row >= 0 ? new ItemRef(EquipRules.TableOf(e.Slot), row) : null;
    }

    void BuildEquipment()
    {
        var eq = _c.Equipment();
        _rightTitle.Text = T(26, "Equipment");
        if (eq == null) { _left.AddChild(InfoText("Sem arma equipada.")); return; }
        var entries = EquipEntries(eq);
        EquipSlot? last = null;
        foreach (var e in entries)
        {
            if (e.Slot != last)
            {
                last = e.Slot;
                Header(_left, e.Slot switch { EquipSlot.Weapon => T(29, "Weapon Slot"), EquipSlot.Armor => T(30, "Armor Slot"), _ => T(31, "Sub Slot") });
            }
            var item = InSlot(eq, e);
            string label = e.Pocket >= 0 ? $"  {T(36, "Mods")} {e.Pocket + 1}" : e.Socket >= 0 ? $"{e.Socket + 1}" : "";
            string name = item is { } it ? ItemName(it) : "-----";
            var entry = e;
            ListButton(_leftButtons, $"{label} {name}".Trim(), "", () => { if (!_inRight) EquipInfo(entry, null); }, () => OpenEquipChoices(entry), color: e.Socket == 0 ? Gold : null);
        }
        if (_inRight && _leftCursor < entries.Count) BuildEquipChoices(entries[_leftCursor]);
        else
        {
            _rightTitle.Text = T(32, "Select the slot to change Item.");
            // Sem escolha aberta, a direita mostra o que está equipado, como no jogo (i_stsoubi).
            foreach (var e in entries.Where(x => InSlot(eq, x) != null))
            {
                var it = InSlot(eq, e)!.Value;
                var l = Text($"{ItemName(it)}", 20);
                _right.AddChild(l);
            }
        }
    }

    void OpenEquipChoices(EqEntry e)
    {
        _msg = "";
        _inRight = true;
        _rightCursor = 0;
        RefreshAll();
    }

    sealed record Choice(string Text, ItemRef? Item, Func<Equipment, Equipment?> Apply, bool Enabled, int Reason);

    List<Choice> EquipChoices(EqEntry e, Equipment eq)
    {
        var db = _c.Db;
        var inv = _c.Inventory();
        var (level, _, _, _) = _c.Numbers();
        var skill = _c.Skill();
        var list = new List<Choice>();
        var current = InSlot(eq, e);
        if (e.Pocket >= 0)
        {
            bool weapon = e.Slot == EquipSlot.Weapon;
            if (current != null) list.Add(new Choice(T(92, "Remove Ability Chip"), null, q => EquipRules.SetChip(db, q, weapon, e.Pocket, -1), true, 0));
            foreach (var c in EquipRules.Candidates(inv, eq, "abChip"))
            {
                bool ok = EquipRules.ChipFits(db, c.Row, weapon);
                list.Add(new Choice(ItemName(c), c, q => EquipRules.SetChip(db, q, weapon, e.Pocket, c.Row), ok, EquipRules.TextCannotEquip));
            }
            return list;
        }
        if (e.Slot == EquipSlot.Weapon)
        {
            if (e.Socket > 0 && current != null)
            {
                list.Add(new Choice(T(90, "Exchange Weapon Unit."), null, q => EquipRules.Exchange(db, q, e.Socket), db.CanEquipWeapon(current.Value.Row, _c.ShinkaRow, skill), EquipRules.TextCannotEquip));
                list.Add(new Choice(T(42, "Remove Equipment"), null, q => EquipRules.SetSocket(db, q, e.Socket, -1), true, 0));
            }
            foreach (var c in EquipRules.Candidates(inv, eq, "weapon"))
            {
                // Reserva: o jogo guarda a arma no soquete; só a ativa (soquete 0) precisa poder ser usada agora (HIPÓTESE).
                bool ok = e.Socket != 0 || db.CanEquipWeapon(c.Row, _c.ShinkaRow, skill);
                list.Add(new Choice(ItemName(c), c, q => EquipRules.SetSocket(db, q, e.Socket, c.Row), ok, EquipRules.TextCannotEquip));
            }
            return list;
        }
        string table = EquipRules.TableOf(e.Slot);
        if (current != null) list.Add(new Choice(T(42, "Remove Equipment"), null, q => EquipRules.SetArmor(db, q, e.Slot, -1), true, 0));
        foreach (var c in EquipRules.Candidates(inv, eq, table))
            list.Add(new Choice(ItemName(c), c, q => EquipRules.SetArmor(db, q, e.Slot, c.Row), db.CanEquipArmor(table, c.Row, level), EquipRules.TextCannotEquip));
        return list;
    }

    void BuildEquipChoices(EqEntry e)
    {
        var eq = _c.Equipment()!;
        _rightTitle.Text = e.Pocket >= 0 ? T(91, "Equip Ability Chip") : e.Slot == EquipSlot.Weapon ? T(40, "Equip Weapon Unit.") : T(89, "Equip Armor Board");
        var choices = EquipChoices(e, eq);
        if (choices.Count == 0) { _right.AddChild(InfoText("Nada na Device Folder para este lugar.", 20, Dim)); return; }
        foreach (var ch in choices)
        {
            var choice = ch;
            ListButton(_rightButtons, choice.Text, "", () => EquipInfo(e, choice), () =>
            {
                if (!choice.Enabled) { _msg = T(choice.Reason, "This Item cannot be equipped."); _message.Text = _msg; return; }
                if (choice.Apply(_c.Equipment()!) is not { } next) { _msg = T(EquipRules.TextCannotEquip, "This Item cannot be equipped."); _message.Text = _msg; return; }
                _c.SetEquipment(next);
                _c.Log?.Invoke($"menu: equipamento {e.Slot} {(choice.Item is { } it ? ItemName(it) : choice.Text)}");
                _msg = "";
                _inRight = false;
                RefreshAll();
            }, choice.Enabled, choice.Item == null ? Gold : null);
        }
    }

    // Quadro de baixo: descrição do lugar ou do item e, numa escolha, os atributos antes → depois.
    void EquipInfo(EqEntry e, Choice? choice)
    {
        var eq = _c.Equipment();
        if (eq == null) return;
        var items = new List<Control>();
        var item = choice != null ? choice.Item : InSlot(eq, e);
        if (choice == null && item == null)
            items.Add(InfoText(e.Pocket >= 0 ? T(101, "Select the mod.") : T(e.Slot switch { EquipSlot.Weapon => 33, EquipSlot.Armor => 34, _ => 35 }, "")));
        if (item is { } it) items.Add(ItemInfo(it));
        if (choice != null && choice.Apply(eq) is { } after && _c.Player() is { } now)
        {
            var then = _c.Preview(after);
            var grid = new GridContainer { Columns = 4 };
            grid.AddThemeConstantOverride("h_separation", 18);
            (string, int, int)[] stats =
            [
                (T(14, "Strength"), now.Tsuyosa, then.Tsuyosa), (T(15, "Defense"), now.Mamori, then.Mamori), (T(16, "Wisdom"), now.Kashikosa, then.Kashikosa),
                (T(17, "Spirit"), now.Seishin, then.Seishin), (T(18, "Speed"), now.Subayasa, then.Subayasa), ("HP", now.MaxHp, then.MaxHp), ("MP", now.MaxMp, then.MaxMp),
                (T(19, "⊂Resist"), now.Res(0), then.Res(0)), (T(20, "⊃Resist"), now.Res(1), then.Res(1)), (T(21, "∪Resist"), now.Res(2), then.Res(2)), (T(22, "∩Resist"), now.Res(3), then.Res(3)),
            ];
            foreach (var (name, a, b) in stats.Where(s => s.Item2 != s.Item3).Take(8))
            {
                grid.AddChild(Text(name, 19, Dim));
                grid.AddChild(Text($"{a} > {b}", 19, b > a ? Good : Bad));
            }
            if (grid.GetChildCount() > 0) items.Add(grid);
            else items.Add(InfoText("Sem mudança nos atributos.", 18, Dim));
            if (!choice.Enabled) items.Add(InfoText(T(choice.Reason, "This Item cannot be equipped."), 18, Pink));
        }
        ShowInfo(items.ToArray());
    }

    // Descrição do item: nome, texto da tabela (TXT, " / " = quebra de linha) e os números que decidem o uso.
    Control ItemInfo(ItemRef it)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        box.AddChild(Text(ItemName(it), 21, Gold));
        var (txt, extra) = ItemText(_c.Db, it, T);
        if (txt.Length > 0) box.AddChild(InfoText(txt, 18));
        if (extra.Length > 0) box.AddChild(Text(extra, 18, Lilac));
        return box;
    }

    /// <summary>Texto do item (TXT, uma linha por "/") e a linha de números (força, defesa, skill, pockets…). Serve às lojas também.</summary>
    internal static (string Text, string Numbers) ItemText(GameTables db, ItemRef it, Func<int, string, string> t)
    {
        if (!db.Contains(it.Table)) return ("", "");
        var tb = db[it.Table];
        string txt = Desc(tb.Text(it.Row, "TXT"));
        int pockets = EquipRules.Pockets(db, it.Table, it.Row);
        if (txt.EndsWith("ポケットのかず:", StringComparison.Ordinal)) txt += $" {pockets}";
        string Skill(int k) => k is >= 0 and < 9 ? t(71 + k, SkillNames[k]) : "-";
        string extra = it.Table switch
        {
            "weapon" => $"{t(14, "Strength")} {tb.Int(it.Row, "TSUYOSA")}   {Skill(tb.Int(it.Row, "SKILL"))} {tb.Int(it.Row, "SKILLPOINT")}   Delay {tb.Int(it.Row, "DELAY")}",
            "armor1" => $"{t(15, "Defense")} {tb.Int(it.Row, "MAMORI")}   HP +{tb.Int(it.Row, "MAXHP")}   Lv {tb.Int(it.Row, "LVPOINT")}",
            "armor2" => $"Lv {tb.Int(it.Row, "LVPOINT")}",
            "abChip" => $"{(tb.Int(it.Row, "WU") != 0 ? "∈" : "")}{(tb.Int(it.Row, "BLUEAB") != 0 ? "∋" : "")}   ¶ {(tb.Int(it.Row, "NEXTX") is var x and >= 0 ? x.ToString() : "-")}",
            "conItem" => $"HP {tb.Int(it.Row, "HP")}   MP {tb.Int(it.Row, "MP")}",
            _ => "",
        };
        return (txt, extra);
    }

    // ---------- Folder ----------

    void BuildFolder()
    {
        var inv = _c.Inventory();
        var db = _c.Db;
        _rightTitle.Text = T(48, "Select the folder to check Item.");
        string[] names = [T(44, "Device Folder"), T(45, "Key Folder"), T(46, "Disk Folder"), T(47, "BITS Folder")];
        string[] counts = [$"{inv.Devices.Count}/{Inventory.DeviceMax}", inv.ImportantFlags.Count.ToString(), inv.DiskCounts.Values.Sum().ToString(), ""];
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            ListButton(_leftButtons, names[i], counts[i], () => { if (!_inRight && _leftCursor == k) FolderInfo(k); BuildFolderItems(k); },
                () => { if (_rightButtons.Count > 0) { _inRight = true; _rightCursor = 0; _rightButtons[0].GrabFocus(); } });
        }
        // Ações da pasta (I<49..51>): Sort na Device Folder; Break no item escolhido (o Give ficou de fora: ver ItemMenu).
        Header(_left, "");
        ListButton(_leftButtons, T(49, "Sort"), "", () => ShowInfo(InfoText("Ordena a Device Folder por tipo (∈ ∋ ∨ ∧) e por item.")), () =>
        {
            inv.SortDevices(EquipRules.SortKey);
            _c.Log?.Invoke("menu: Device Folder ordenada");
            _leftCursor = 0;
            _inRight = true;
            RefreshAll();
        });
        BuildFolderItems(Math.Min(_leftCursor, 3));
    }

    int _folderShown = -1;

    void BuildFolderItems(int folder)
    {
        _folderShown = folder;
        var inv = _c.Inventory();
        var db = _c.Db;
        var eq = _c.Equipment();
        Clear(_right);
        _rightButtons.Clear();
        _rightTitle.Text = folder switch { 0 => T(44, "Device Folder"), 1 => T(45, "Key Folder"), 2 => T(46, "Disk Folder"), _ => T(47, "BITS Folder") };
        if (_breakAsk is { } ask)
        {
            _rightTitle.Text = T(55, "Break Item?");
            ListButton(_rightButtons, T(56, "Yes break this Item."), "", () => ShowInfo(ItemInfo(ask)), () => DoBreak(ask));
            ListButton(_rightButtons, T(57, "No change Item."), "", () => ShowInfo(ItemInfo(ask)), () => { _breakAsk = null; RefreshAll(); });
            return;
        }
        switch (folder)
        {
            case 0:
                var marks = EquipRules.EquippedMarks(inv, eq);
                for (int i = 0; i < inv.Devices.Count; i++)
                {
                    var it = inv.Devices[i];
                    int index = i;
                    ListButton(_rightButtons, ItemName(it), marks[i] ? "E" : "", () => ShowInfo(ItemInfo(it), Actions()), () => ItemMenu(it, index));
                }
                break;
            case 1:
                foreach (var row in inv.ImportantFlags.OrderBy(x => x))
                {
                    var it = new ItemRef("impItem", row);
                    ListButton(_rightButtons, ItemName(it), "", () => ShowInfo(ItemInfo(it)), () => { });
                }
                break;
            case 2:
                foreach (var (row, n) in inv.DiskCounts.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key))
                {
                    var it = new ItemRef("conItem", row);
                    ListButton(_rightButtons, ItemName(it), $"×{n}", () => ShowInfo(ItemInfo(it), Actions()), () => ItemMenu(it, -1));
                }
                break;
            default:
                _right.AddChild(Row(T(24, "BITS"), inv.Bit.ToString(), 26));
                _right.AddChild(Row("¶ Data", inv.XData.ToString(), 26));
                break;
        }
        if (_rightButtons.Count == 0 && folder != 3) _right.AddChild(InfoText("Pasta vazia.", 20, Dim));
    }

    Control Actions() => Text($"{Help(12, "Confirm")}: {T(51, "Break")}", 17, Dim);

    void FolderInfo(int folder) => ShowInfo(InfoText(T(58 + folder, "")));

    // Confirmar num item da pasta: Break (pede Yes/No). DECISÃO DO PORT (pedido do usuário em 2026-11-02): o TAB é só o menu do
    // próprio jogador; o Give do jogo (dar item a outro Digimon do grupo) ficou de fora, como compra, venda e banco, que são dos NPCs.
    void ItemMenu(ItemRef it, int index)
    {
        _msg = "";
        var inv = _c.Inventory();
        if (EquipRules.CannotBreak(_c.Db, inv, _c.Equipment(), it) is { } w) { _msg = T(w, "This Item cannot be damaged."); _message.Text = _msg; return; }
        _breakAsk = it;
        _breakIndex = index;
        _rightCursor = 1; // "No" primeiro, para não quebrar sem querer
        _inRight = true;
        RefreshAll();
    }

    void DoBreak(ItemRef it)
    {
        var inv = _c.Inventory();
        bool ok = it.Table == "conItem" ? inv.RemoveDisks(it.Row, 1)
            : _breakIndex >= 0 && _breakIndex < inv.Devices.Count && inv.Devices[_breakIndex] == it && EquipRules.CannotBreak(_c.Db, inv, _c.Equipment(), it) == null
              // Quebra uma cópia livre: a marcada (E) é a primeira; tira a última igual.
              ? inv.RemoveDeviceAt(inv.Devices.Select((d, i) => (d, i)).Last(x => x.d == it).i)
              : false;
        _c.Log?.Invoke($"menu: Break {ItemName(it)} {(ok ? "" : "(recusado)")}");
        _breakAsk = null;
        _rightCursor = Math.Max(0, _breakIndex - 1);
        _inRight = true;
        RefreshAll();
    }

    // ---------- Techniques ----------

    void BuildTechniques()
    {
        string[] names = [T(64, "Digivolve Tech"), T(67, "Skill Tech"), T(65, "MP Tech.")];
        int[] descs = [68, 69, 70];
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            ListButton(_leftButtons, names[i], "", () => { ShowInfo(InfoText(T(descs[k], ""))); BuildTechItems(k); },
                () => { if (_rightButtons.Count > 0) { _inRight = true; _rightCursor = 0; _rightButtons[0].GrabFocus(); } });
        }
        BuildTechItems(Math.Min(_leftCursor, 2));
    }

    void BuildTechItems(int sub)
    {
        Clear(_right);
        _rightButtons.Clear();
        var db = _c.Db;
        var skill = _c.Skill();
        var (level, _, _, _) = _c.Numbers();
        _rightTitle.Text = T(66, "Select the Tech to check.");
        switch (sub)
        {
            case 0:
                // HIPÓTESE: as "Digivolve Tech" são as formas liberadas (beNDMWShopCtrlEvolve / FORMS) com o nível de cada.
                if (_c.Forms is { } forms)
                    for (int d = 0; d < Evolution.Forms; d++)
                    {
                        if (!forms[d].Unlocked) continue;
                        int dd = d;
                        ListButton(_rightButtons, FormName(d), $"Lv {forms[d].Level}", () => ShowInfo(Text(FormName(dd), 21, Gold),
                            InfoText(Desc(db.Contains("shinka") ? db["shinka"].Text(dd, "EXPLANATION") : ""), 18)), () => { }, color: d == forms.Current ? Gold : null);
                    }
                break;
            case 1:
                for (int k = 0; k < 9; k++)
                {
                    int kk = k;
                    ListButton(_rightButtons, SkillName(k), (k < skill.Count ? skill[k] : 0).ToString(), () => ShowInfo(Text(SkillName(kk), 21, Gold), InfoText(T(80 + kk, ""), 18)), () => { });
                }
                break;
            default:
                if (!db.Contains("pcTech") || !db.Contains("shinka") || _c.ShinkaRow < 0) break;
                var s = db["shinka"];
                var pc = db["pcTech"];
                for (int n = 1; n <= Techniques.Families; n++)
                {
                    int baseRow = s.Int(_c.ShinkaRow, $"TECH{n}"), need = s.Int(_c.ShinkaRow, $"TECH{n}_LEVEL");
                    int idx = pc.Int(baseRow, "SKILL");
                    int sk = idx >= 0 && idx < skill.Count ? skill[idx] : 0;
                    int rank = 0;
                    if (need <= level) for (int m = Techniques.Ranks; m >= 1; m--) if (s.Int(_c.ShinkaRow, $"TECH{n}_SKILL{m}") <= sk) { rank = m; break; }
                    int nn = n;
                    if (rank == 0)
                    {
                        ListButton(_rightButtons, "？？？", $"Lv {need}", () => ShowInfo(InfoText($"Aprende no nível {need}.", 19, Dim)), () => { }, enabled: false);
                        continue;
                    }
                    var t = Techniques.Row(db, baseRow + rank - 1);
                    int r = rank;
                    ListButton(_rightButtons, t.Name, $"MP {t.Mp}", () =>
                    {
                        var parts = new List<Control> { Text($"{t.Name}   ({r}/{Techniques.Ranks})", 21, Gold), InfoText(Desc(pc.Text(t.Row, "TXT")), 18) };
                        if (r < Techniques.Ranks)
                            parts.Add(Text($"> {pc.Text(baseRow + r, "NAME")}: {SkillName(idx)} {s.Int(_c.ShinkaRow, $"TECH{nn}_SKILL{r + 1}")}", 18, Lilac));
                        ShowInfo(parts.ToArray());
                    }, () => { });
                }
                break;
        }
    }
}
