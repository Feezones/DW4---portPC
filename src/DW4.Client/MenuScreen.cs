using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>Tom de uma linha de texto do quadro de detalhes.</summary>
public enum MenuTone { Normal, Dim, Info, Good, Bad, Warn }

public sealed record MenuLine(string Text, MenuTone Tone = MenuTone.Normal);

/// <summary>O que o quadro da direita mostra da linha escolhida: nome, texto, atributos antes → depois.</summary>
public sealed class MenuInfo
{
    public string Title = "";
    public List<MenuLine> Lines = [];
    /// <summary>Título da comparação ("Comparado com a sua …") e os atributos antes → depois.</summary>
    public string CompareTitle = "";
    public List<(string Name, int Before, int After)> Stats = [];
    /// <summary>Requisitos para usar o item: texto e se o jogador cumpre.</summary>
    public List<(string Text, bool Ok)> Requirements = [];
}

/// <summary>Linha da lista: nome, quantidade e valor (preço, custo, nível) à direita.</summary>
public sealed class MenuRow
{
    public string Label = "", Count = "", Value = "";
    /// <summary>Faltou algo (BIT, espaço, equipado): o valor fica vermelho; a linha continua escolhível para mostrar o motivo.</summary>
    public bool Short;
    public Color? Color;
    /// <summary>Marca curta entre o nome e a quantidade (ex.: "+20" verde se é melhor que o equipado, "nv 40" vermelho se falta).</summary>
    public string Tag = "";
    public Color? TagColor;
    /// <summary>Confirmar (Enter, botão do quadro, duplo clique; clique simples nas linhas sem <see cref="Info"/>).</summary>
    public Action? Pick;
    /// <summary>Esquerda/direita ou os botões − e + (quantidade, valor).</summary>
    public Action<int>? Adjust;
    public string AdjustLabel = "";
    public Func<MenuInfo>? Info;
    /// <summary>Texto do botão de confirmar no quadro da direita ("" = sem botão).</summary>
    public string Action = "";
}

/// <summary>Uma tela: título, abas, dinheiro, lista e mensagem. A Fatia monta e manda de novo a cada mudança.</summary>
public sealed class MenuPage
{
    public string Title = "";
    public string[] Tabs = [];
    public int Tab;
    public Action<int>? SetTab;
    public List<(string Label, string Value)> Money = [];
    /// <summary>Cabeçalho das colunas (nome, quantidade, valor); null = sem cabeçalho.</summary>
    public (string Name, string Count, string Value)? Columns;
    public List<MenuRow> Rows = [];
    /// <summary>Quadro da direita quando a linha não tem <see cref="MenuRow.Info"/>.</summary>
    public MenuInfo? Intro;
    public string Message = "";
    public bool MessageBad;
    public Action? Back;
    public int Cursor;
}

/// <summary>
/// Telas de lista das lojas, do banco, do X-data, do Digi-Lab e do avaliador, com layout próprio no estilo do menu do TAB
/// (<see cref="StatusScreen"/>). DECISÃO DO PORT (pedida pelo usuário em 2026-11-06: "refaz esses menus... está muito ruim de usar"):
/// no lugar da janela j_shwin de 11 linhas por página só de teclado, uma lista que rola (roda do mouse), itens iguais juntos com a
/// quantidade, quadro de detalhes com o texto do item e a comparação com o equipado, dinheiro e espaço da pasta sempre à vista,
/// quantidade com esquerda/direita ou − e +, abas Comprar/Vender clicáveis. Controles: setas/direcional escolhem, Enter/A confirma,
/// esquerda/direita mexem na quantidade, Q/E/V (LB/RB/Y) trocam a aba, Backspace/B/clique direito voltam, Esc fecha.
/// Mouse: passar por cima mostra o item; duplo clique (ou o botão do quadro) confirma; nas linhas sem botão, um clique confirma.
/// </summary>
public partial class MenuScreen : Control
{
    const float BaseHeight = 720, BaseWidth = 1280, MinWidth = 1200;
    static readonly Color Navy = new(0.05f, 0.06f, 0.22f, 0.94f), Lilac = new(0.66f, 0.68f, 1f), Pink = new(0.95f, 0.42f, 0.48f),
        Line = new(0.78f, 0.78f, 0.9f, 0.55f), Dim = new(0.62f, 0.62f, 0.75f), Gold = new(1f, 0.85f, 0.35f), Good = new(0.45f, 1f, 0.55f),
        Bad = new(1f, 0.45f, 0.45f), Focus = new(0.22f, 0.27f, 0.68f, 0.85f);

    readonly BitmapFont? _font;
    Control _scaled = null!;
    Label _title = null!, _message = null!;
    HBoxContainer _tabs = null!, _help = null!;
    VBoxContainer _money = null!, _rows = null!, _info = null!;
    HBoxContainer _columns = null!, _adjust = null!;
    Label _adjustLabel = null!;
    Button _action = null!;
    ScrollContainer _scroll = null!;
    MenuPage _page = new();
    readonly List<Button> _buttons = [];
    int _cursor;
    bool _hasTags;
    StyleBoxFlat _selected = null!, _plain = null!;

    /// <summary>Esc (pausa) ou o botão do status: fechar tudo.</summary>
    public event Action? CloseRequested;

    public MenuScreen(BitmapFont? font) => _font = font;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0.01f, 0.01f, 0.05f, 0.8f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        _scaled = new Control { Theme = BuildTheme(), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_scaled);
        var margin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var (side, v) in new[] { ("left", 40), ("right", 40), ("top", 28), ("bottom", 10) }) margin.AddThemeConstantOverride("margin_" + side, v);
        _scaled.AddChild(margin);
        var page = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        page.AddThemeConstantOverride("separation", 12);
        margin.AddChild(page);

        // Alto: título e abas | dinheiro.
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 16);
        page.AddChild(top);
        var titlePanel = Panel(new HBoxContainer(), 8);
        titlePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var titleRow = (HBoxContainer)titlePanel.GetChild(0);
        titleRow.AddThemeConstantOverride("separation", 18);
        _title = Text("", 30, Gold);
        _title.VerticalAlignment = VerticalAlignment.Center;
        titleRow.AddChild(_title);
        _tabs = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        _tabs.AddThemeConstantOverride("separation", 10);
        titleRow.AddChild(_tabs);
        top.AddChild(titlePanel);
        var moneyPanel = Panel(new VBoxContainer(), 8);
        moneyPanel.CustomMinimumSize = new Vector2(360, 0);
        _money = (VBoxContainer)moneyPanel.GetChild(0);
        _money.AddThemeConstantOverride("separation", 2);
        top.AddChild(moneyPanel);

        // Corpo: lista | detalhes.
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        body.AddThemeConstantOverride("separation", 16);
        page.AddChild(body);
        var listPanel = Panel(new VBoxContainer(), 10);
        listPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        listPanel.SizeFlagsStretchRatio = 1.25f;
        var listBox = (VBoxContainer)listPanel.GetChild(0);
        listBox.AddThemeConstantOverride("separation", 4);
        _columns = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        listBox.AddChild(_columns);
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_rows);
        listBox.AddChild(_scroll);
        body.AddChild(listPanel);

        var infoPanel = Panel(new VBoxContainer(), 14);
        infoPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var infoBox = (VBoxContainer)infoPanel.GetChild(0);
        infoBox.AddThemeConstantOverride("separation", 10);
        _info = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        _info.AddThemeConstantOverride("separation", 4);
        infoBox.AddChild(_info);
        _adjust = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _adjust.AddThemeConstantOverride("separation", 14);
        var minus = SmallButton("−", () => AdjustCurrent(-1));
        _adjustLabel = Text("", 22, null, HorizontalAlignment.Center);
        _adjustLabel.CustomMinimumSize = new Vector2(220, 0);
        var plus = SmallButton("+", () => AdjustCurrent(1));
        _adjust.AddChild(minus);
        _adjust.AddChild(_adjustLabel);
        _adjust.AddChild(plus);
        infoBox.AddChild(_adjust);
        _action = new Button { CustomMinimumSize = new Vector2(0, 48), FocusMode = FocusModeEnum.None, ClipText = true };
        _action.AddThemeFontSizeOverride("font_size", 24);
        _action.AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.2f, 0.55f, 0.95f), Lilac, 2, 10));
        _action.AddThemeStyleboxOverride("hover", Box(new Color(0.24f, 0.29f, 0.72f, 0.95f), Gold, 2, 10));
        _action.AddThemeStyleboxOverride("pressed", Box(Focus, Gold, 2, 10));
        _action.AddThemeColorOverride("font_hover_color", Gold);
        _action.Pressed += () => PickCurrent();
        infoBox.AddChild(_action);
        body.AddChild(infoPanel);

        _message = Text("", 20, Pink, HorizontalAlignment.Center);
        _message.ClipText = true;
        page.AddChild(_message);
        _help = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _help.AddThemeConstantOverride("separation", 22);
        page.AddChild(_help);

        _selected = Box(Focus, Gold, 2, 8);
        _plain = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        _plain.SetContentMarginAll(4);
        Rebuild();
        FitScreen();
    }

    // ---------- aparência (a mesma do menu do TAB) ----------

    Theme BuildTheme()
    {
        var theme = new Theme();
        if (_font?.ToFontFile("MINIMUM") is { } gf) { theme.DefaultFont = gf; theme.DefaultFontSize = 22; }
        theme.SetColor("font_outline_color", "Label", new Color(0.02f, 0.02f, 0.12f));
        theme.SetConstant("outline_size", "Label", 4);
        var flat = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0) };
        flat.SetContentMarginAll(4);
        theme.SetStylebox("normal", "Button", flat);
        theme.SetStylebox("disabled", "Button", flat);
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetStylebox("hover", "Button", Box(new Color(0.18f, 0.2f, 0.5f, 0.6f), new Color(0, 0, 0, 0), 0, 8));
        theme.SetStylebox("pressed", "Button", Box(Focus, Gold, 2, 8));
        theme.SetColor("font_color", "Button", Colors.White);
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_pressed_color", "Button", Gold);
        theme.SetColor("font_outline_color", "Button", new Color(0.02f, 0.02f, 0.12f));
        theme.SetConstant("outline_size", "Button", 4);
        var grab = Box(Lilac, new Color(0, 0, 0, 0), 0, 4);
        theme.SetStylebox("grabber", "VScrollBar", grab);
        theme.SetStylebox("grabber_highlight", "VScrollBar", Box(Gold, new Color(0, 0, 0, 0), 0, 4));
        theme.SetStylebox("grabber_pressed", "VScrollBar", Box(Gold, new Color(0, 0, 0, 0), 0, 4));
        theme.SetStylebox("scroll", "VScrollBar", Box(new Color(0.1f, 0.1f, 0.3f, 0.8f), new Color(0, 0, 0, 0), 0, 4));
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

    static Color ToneColor(MenuTone t) => t switch
    {
        MenuTone.Dim => Dim,
        MenuTone.Info => Lilac,
        MenuTone.Good => Good,
        MenuTone.Bad => Bad,
        MenuTone.Warn => Pink,
        _ => Colors.White,
    };

    Button SmallButton(string text, Action press)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(52, 40), FocusMode = FocusModeEnum.None };
        b.AddThemeFontSizeOverride("font_size", 26);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.12f, 0.14f, 0.4f, 0.9f), Lilac, 2, 8));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.22f, 0.26f, 0.62f, 0.95f), Gold, 2, 8));
        b.Pressed += press;
        return b;
    }

    static void Clear(Node box)
    {
        foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
    }

    // ---------- conteúdo ----------

    /// <summary>Mostra a tela (a mesma janela; a rolagem fica onde estava quando a lista é a mesma).</summary>
    public void Show(MenuPage page)
    {
        bool same = page.Title == _page.Title && page.Tab == _page.Tab && page.Rows.Count == _page.Rows.Count;
        _page = page;
        _cursor = page.Rows.Count == 0 ? -1 : Math.Clamp(page.Cursor, 0, page.Rows.Count - 1);
        if (!IsNodeReady()) return;
        float scroll = same ? _scroll.ScrollVertical : 0;
        Rebuild();
        if (same) _scroll.SetDeferred(ScrollContainer.PropertyName.ScrollVertical, scroll);
        else if (_cursor >= 0) Callable.From(() => { if (_cursor >= 0 && _cursor < _buttons.Count) _scroll.EnsureControlVisible(_buttons[_cursor]); }).CallDeferred();
    }

    public int Cursor => _cursor;

    void Rebuild()
    {
        var p = _page;
        _title.Text = p.Title;
        Clear(_tabs);
        for (int i = 0; i < p.Tabs.Length; i++)
        {
            int k = i;
            var b = new Button { Text = p.Tabs[i], CustomMinimumSize = new Vector2(140, 40), FocusMode = FocusModeEnum.None, ClipText = true, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            b.AddThemeFontSizeOverride("font_size", 22);
            bool on = i == p.Tab;
            b.AddThemeStyleboxOverride("normal", on ? Box(Focus, Gold, 2, 10) : Box(new Color(0.08f, 0.09f, 0.3f, 0.9f), Line, 1, 10));
            b.AddThemeColorOverride("font_color", on ? Gold : Dim);
            b.Pressed += () => { if (k != _page.Tab) _page.SetTab?.Invoke(k); };
            _tabs.AddChild(b);
        }
        Clear(_money);
        foreach (var (label, value) in p.Money) _money.AddChild(Row(label, value, 21));
        _money.GetParent<Control>().Visible = p.Money.Count > 0;

        _hasTags = p.Rows.Any(r => r.Tag.Length > 0);
        Clear(_columns);
        _columns.Visible = p.Columns != null;
        if (p.Columns is { } cols)
        {
            var name = Text(cols.Name, 17, Lilac);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _columns.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0), MouseFilter = MouseFilterEnum.Ignore });
            _columns.AddChild(name);
            if (_hasTags) _columns.AddChild(Fixed(Text("vs. o seu", 17, Lilac, HorizontalAlignment.Right), 110));
            _columns.AddChild(Fixed(Text(cols.Count, 17, Lilac, HorizontalAlignment.Right), 90));
            _columns.AddChild(Fixed(Text(cols.Value, 17, Lilac, HorizontalAlignment.Right), 130));
            _columns.AddChild(new Control { CustomMinimumSize = new Vector2(22, 0), MouseFilter = MouseFilterEnum.Ignore });
        }

        Clear(_rows);
        _buttons.Clear();
        for (int i = 0; i < p.Rows.Count; i++) _buttons.Add(RowButton(p.Rows[i], i));
        if (p.Rows.Count == 0) _rows.AddChild(Text("(vazio)", 21, Dim));
        _message.Text = p.Message;
        _message.AddThemeColorOverride("font_color", p.MessageBad ? Pink : Good);
        BuildHelp();
        Select(_cursor, scroll: false);
    }

    static Control Fixed(Control c, float width)
    {
        c.CustomMinimumSize = new Vector2(width, 0);
        return c;
    }

    // Linha sublinhada do jogo: rótulo à esquerda, valor à direita, traço claro embaixo.
    static Control Row(string label, string value, int size)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var l = Text(label, size);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        l.ClipText = true;
        row.AddChild(l);
        row.AddChild(Text(value, size, Gold, HorizontalAlignment.Right));
        box.AddChild(row);
        box.AddChild(new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore });
        return box;
    }

    Button RowButton(MenuRow r, int index)
    {
        var b = new Button { CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None, ClipText = true, MouseFilter = MouseFilterEnum.Pass };
        var line = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        line.SetAnchorsPreset(LayoutPreset.FullRect);
        line.OffsetLeft = 14;
        line.OffsetRight = -10;
        var name = Text(r.Label, 23, r.Color);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        name.VerticalAlignment = VerticalAlignment.Center;
        line.AddChild(name);
        // Colunas da direita só quando a linha tem algo nelas (senão "Voltar" e as frases do jogo eram cortadas).
        if (r.Tag.Length + r.Count.Length + r.Value.Length > 0)
        {
            if (_hasTags)
            {
                var tag = Text(r.Tag, 20, r.TagColor, HorizontalAlignment.Right);
                tag.VerticalAlignment = VerticalAlignment.Center;
                line.AddChild(Fixed(tag, 110));
            }
            var count = Text(r.Count, 21, Dim, HorizontalAlignment.Right);
            count.VerticalAlignment = VerticalAlignment.Center;
            line.AddChild(Fixed(count, 90));
            var value = Text(r.Value, 23, r.Short ? Bad : null, HorizontalAlignment.Right);
            value.VerticalAlignment = VerticalAlignment.Center;
            line.AddChild(Fixed(value, 130));
        }
        b.AddChild(line);
        // Por baixo, o traço das linhas do jogo.
        var rule = new ColorRect { Color = Line, MouseFilter = MouseFilterEnum.Ignore };
        rule.SetAnchorsPreset(LayoutPreset.BottomWide);
        rule.OffsetTop = -2;
        rule.OffsetLeft = 10;
        rule.OffsetRight = -10;
        b.AddChild(rule);
        b.MouseEntered += () => Select(index, scroll: false);
        b.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mb) return;
            Select(index, scroll: false);
            // Linha com botão de confirmar no quadro (comprar, vender, guardar): um clique escolhe, duplo clique confirma (não compra
            // sem querer). Linha sem botão (Voltar, Depositar, escolhas): um clique confirma.
            if (mb.DoubleClick || _page.Rows[index].Action.Length == 0) PickCurrent();
            b.AcceptEvent();
        };
        _rows.AddChild(b);
        return b;
    }

    void Select(int index, bool scroll = true)
    {
        if (_page.Rows.Count == 0) { _cursor = -1; ShowInfo(null); return; }
        _cursor = Math.Clamp(index, 0, _page.Rows.Count - 1);
        for (int i = 0; i < _buttons.Count; i++)
        {
            bool on = i == _cursor;
            _buttons[i].AddThemeStyleboxOverride("normal", on ? _selected : _plain);
            _buttons[i].AddThemeStyleboxOverride("hover", on ? _selected : Box(new Color(0.18f, 0.2f, 0.5f, 0.6f), new Color(0, 0, 0, 0), 0, 8));
            var name = (Label)_buttons[i].GetChild(0).GetChild(0);
            name.AddThemeColorOverride("font_color", on ? Gold : _page.Rows[i].Color ?? Colors.White);
        }
        if (scroll && _cursor < _buttons.Count) _scroll.EnsureControlVisible(_buttons[_cursor]);
        ShowInfo(_page.Rows[_cursor]);
    }

    void ShowInfo(MenuRow? r)
    {
        Clear(_info);
        var info = r?.Info?.Invoke() ?? _page.Intro;
        if (info != null)
        {
            if (info.Title.Length > 0) { var t = Text(info.Title, 26, Gold); t.AutowrapMode = TextServer.AutowrapMode.WordSmart; _info.AddChild(t); }
            foreach (var l in info.Lines)
                foreach (var part in l.Text.Replace("\r\n", "\n").Split('\n'))
                {
                    // Um rótulo por linha: com "\n" dentro de um rótulo só, a fonte do jogo deixava linhas em branco.
                    var label = Text(part, 20, ToneColor(l.Tone));
                    label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    _info.AddChild(label);
                }
            if (info.CompareTitle.Length > 0)
            {
                _info.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore });
                var head = Text(info.CompareTitle, 19, Lilac);
                head.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                _info.AddChild(head);
            }
            if (info.Stats.Count > 0)
            {
                // Atributo, agora > com o item, diferença (verde melhor, vermelho pior); duas colunas.
                var grid = new GridContainer { Columns = 6, MouseFilter = MouseFilterEnum.Ignore };
                grid.AddThemeConstantOverride("h_separation", 12);
                grid.AddThemeConstantOverride("v_separation", 0);
                foreach (var (name, a, b) in info.Stats)
                {
                    var c = b > a ? Good : b < a ? Bad : Dim;
                    grid.AddChild(Text(name, 19, Dim));
                    grid.AddChild(Text($"{a} > {b}", 19));
                    grid.AddChild(Text(b == a ? "=" : $"{(b > a ? "+" : "")}{b - a}", 19, c));
                }
                _info.AddChild(grid);
            }
            if (info.Requirements.Count > 0)
            {
                _info.AddChild(new Control { CustomMinimumSize = new Vector2(0, 4), MouseFilter = MouseFilterEnum.Ignore });
                _info.AddChild(Text("Requisitos", 19, Lilac));
                foreach (var (text, ok) in info.Requirements)
                {
                    var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
                    row.AddThemeConstantOverride("separation", 8);
                    row.AddChild(Fixed(Text(ok ? "OK" : "Falta", 19, ok ? Good : Bad), 56));
                    var l = Text(text, 19, ok ? null : Bad);
                    l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    row.AddChild(l);
                    _info.AddChild(row);
                }
            }
        }
        _adjust.Visible = r?.Adjust != null;
        _adjustLabel.Text = r?.AdjustLabel ?? "";
        _action.Visible = r != null && r.Pick != null && r.Action.Length > 0;
        _action.Text = r?.Action ?? "";
    }

    void BuildHelp()
    {
        Clear(_help);
        bool pad = DeviceIcons.Current != ButtonNames.Kind.Keyboard;
        var kind = pad ? DeviceIcons.Current : ButtonNames.Kind.Keyboard;
        var b = PortSettings.Current.Bindings;
        string? Bind(string action) => b.Get(pad ? InputBindings.Pad : InputBindings.Keyboard, action).FirstOrDefault();
        void Item(string? binding, string label, string? word = null)
        {
            var item = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            item.AddThemeConstantOverride("separation", 6);
            if (word != null) item.AddChild(Text(word, 18, Gold));
            else if (binding != null) item.AddChild(DeviceIcons.Chip(binding, kind));
            item.AddChild(Text(label, 18));
            _help.AddChild(item);
        }
        Item(null, "Escolher", pad ? "Direcional" : "Setas/Roda");
        Item(Bind("confirmar"), "Confirmar");
        if (_page.Rows.Any(r => r.Adjust != null)) { Item(Bind("esquerda"), ""); Item(Bind("direita"), "Quantidade"); }
        if (_page.Tabs.Length > 1) { Item(pad ? "botao:9" : "tecla:Q", ""); Item(pad ? "botao:10" : "tecla:E", "Aba"); }
        Item(Bind("cancelar"), "Voltar");
        if (!pad) Item(Bind("pausa"), "Fechar");
    }

    public override void _Process(double delta) => FitScreen();

    void FitScreen()
    {
        var win = GetViewportRect().Size;
        if (win.Y <= 0 || win.X <= 0) return;
        float s = Math.Min(win.Y / BaseHeight, win.X / MinWidth);
        float w = Math.Min(win.X / s, BaseWidth * 1.15f);
        _scaled.Scale = new Vector2(s, s);
        _scaled.Size = new Vector2(w, win.Y / s);
        _scaled.Position = new Vector2((win.X - w * s) / 2, 0);
    }

    // ---------- entrada ----------

    void PickCurrent()
    {
        if (_cursor < 0 || _cursor >= _page.Rows.Count || _page.Rows[_cursor].Pick is not { } pick) return;
        // Depois do sinal: a escolha remonta a lista (e pode fechar a janela ou trocar de fase).
        Callable.From(pick).CallDeferred();
    }

    void AdjustCurrent(int dir)
    {
        if (_cursor < 0 || _cursor >= _page.Rows.Count || _page.Rows[_cursor].Adjust is not { } adjust) return;
        Callable.From(() => adjust(dir)).CallDeferred();
    }

    void Tab(int dir)
    {
        if (_page.Tabs.Length < 2 || _page.SetTab is not { } set) return;
        int n = _page.Tabs.Length;
        set(((_page.Tab + dir) % n + n) % n);
    }

    static bool Hit(InputEvent e, string action, bool echo = false) =>
        InputMap.HasAction(PortInput.Name(action)) && e.IsActionPressed(PortInput.Name(action), allowEcho: echo, exactMatch: false);

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        DeviceIcons.Seen(e);
        bool Key(params Key[] keys) => e is InputEventKey { Pressed: true } k && keys.Contains(k.Keycode);
        bool KeyOnce(params Key[] keys) => e is InputEventKey { Pressed: true, Echo: false } k && keys.Contains(k.Keycode);
        bool Pad(params JoyButton[] buttons) => e is InputEventJoypadButton { Pressed: true } jb && buttons.Contains(jb.ButtonIndex);
        bool mouse = e is InputEventMouse;
        bool handled = true;
        if (!mouse && (Hit(e, "pausa") || Hit(e, "status"))) CloseRequested?.Invoke();
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } || !mouse && Hit(e, "cancelar")) _page.Back?.Invoke();
        else if (KeyOnce(Godot.Key.Q) || Pad(JoyButton.LeftShoulder)) Tab(-1);
        else if (KeyOnce(Godot.Key.E, Godot.Key.V) || Pad(JoyButton.RightShoulder, JoyButton.Y)) Tab(1);
        else if (Key(Godot.Key.Pageup)) Select(_cursor - 8);
        else if (Key(Godot.Key.Pagedown)) Select(_cursor + 8);
        else if (Key(Godot.Key.Home)) Select(0);
        else if (Key(Godot.Key.End)) Select(_page.Rows.Count - 1);
        else if (!mouse && (Hit(e, "cima", true) || Pad(JoyButton.DpadUp))) Select(_cursor <= 0 ? _page.Rows.Count - 1 : _cursor - 1);
        else if (!mouse && (Hit(e, "baixo", true) || Pad(JoyButton.DpadDown))) Select(_cursor >= _page.Rows.Count - 1 ? 0 : _cursor + 1);
        else if (!mouse && (Hit(e, "esquerda", true) || Pad(JoyButton.DpadLeft))) { if (CurrentAdjusts) AdjustCurrent(-1); else Tab(-1); }
        else if (!mouse && (Hit(e, "direita", true) || Pad(JoyButton.DpadRight))) { if (CurrentAdjusts) AdjustCurrent(1); else Tab(1); }
        else if (!mouse && (Hit(e, "confirmar") || Hit(e, "ataque"))) PickCurrent();
        else if (Key(Godot.Key.Tab)) { }
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }

    bool CurrentAdjusts => _cursor >= 0 && _cursor < _page.Rows.Count && _page.Rows[_cursor].Adjust != null;
}
