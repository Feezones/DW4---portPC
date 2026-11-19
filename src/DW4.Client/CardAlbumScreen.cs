using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>Uma carta do álbum: nome, raridade (RARE 0..2), chance por sorteio em milésimos (KAKURITSU), se o jogador tem e a arte.</summary>
public sealed record CardEntry(string Name, int Rare, int Chance, bool Owned, Texture2D? Art);

/// <summary>
/// Guardião das Cartas (stagIS11): álbum e abertura de B. Pack numa tela só, no estilo do menu do TAB e das lojas
/// (<see cref="MenuScreen"/>). DECISÃO DO PORT (pedida pelo usuário em 2026-11-06: "refaz essas telas dos albums que estão estranhas e
/// difícil de navegar"): no lugar da janela de botões com 10 nomes por página, duas abas:
///  · Álbum: as 50 cartas numa grade 10 × 5 com a arte do jogo (area.afs #621, card01..card50; card00 é o verso), as que faltam
///    aparecem viradas com o número; o quadro da direita mostra a carta grande, raridade, chance e se já tem.
///  · B. Pack: o pacote e um botão para abrir; as 3 cartas sorteadas viram uma por uma, com "NOVA!" ou "repetida".
/// Controles: setas/direcional andam na grade, Q/E (LB/RB) ou clique trocam a aba, Enter/A abre um pacote (na aba B. Pack),
/// Backspace/B/clique direito voltam (B. Pack → Álbum → sair), Esc sai. Mouse/toque: passar ou tocar escolhe a carta, botões na tela.
/// A regra do sorteio não mudou (<see cref="CardPack"/>).
/// </summary>
public partial class CardAlbumScreen : Control
{
    const float BaseHeight = 720, BaseWidth = 1280, MinWidth = 1200;
    const int Cols = 10;
    static readonly Color Navy = new(0.05f, 0.06f, 0.22f, 0.94f), Lilac = new(0.66f, 0.68f, 1f), Pink = new(0.95f, 0.42f, 0.48f),
        Line = new(0.78f, 0.78f, 0.9f, 0.55f), Dim = new(0.62f, 0.62f, 0.75f), Gold = new(1f, 0.85f, 0.35f), Good = new(0.45f, 1f, 0.55f),
        Focus = new(0.22f, 0.27f, 0.68f, 0.85f);

    readonly BitmapFont? _font;
    readonly bool _touch;
    readonly Texture2D? _back;
    List<CardEntry> _cards = [];
    int _packs, _tab, _cursor;
    string _message = "";
    bool _messageBad;

    Control _scaled = null!;
    HBoxContainer _tabs = null!, _help = null!;
    VBoxContainer _counters = null!;
    Control _albumBody = null!, _packBody = null!;
    readonly List<Button> _tiles = [];
    TextureRect _bigArt = null!;
    Label _bigName = null!, _bigNumber = null!, _bigRare = null!, _bigOwned = null!, _bigChance = null!, _summary = null!, _messageLabel = null!;
    Label _packCount = null!;
    Button _openButton = null!;
    readonly TextureRect[] _drawn = new TextureRect[CardPack.CardsPerPack];
    readonly Label[] _drawnName = new Label[CardPack.CardsPerPack], _drawnTag = new Label[CardPack.CardsPerPack];
    Tween? _flip;

    /// <summary>Abrir um B. Pack (a Fatia sorteia e chama <see cref="ShowDrawn"/>).</summary>
    public event Action? OpenPackRequested;
    /// <summary>Sair do Guardião (volta à Central Area, como no jogo).</summary>
    public event Action? LeaveRequested;

    public CardAlbumScreen(BitmapFont? font, Texture2D? back, bool touch)
    {
        _font = font;
        _back = back;
        _touch = touch;
    }

    public static string RareName(int rare) => rare switch { 2 => "Rara", 1 => "Incomum", _ => "Comum" };

    public static Color RareColor(int rare)
    {
        var (r, g, b) = CardPack.RareColor(rare);
        // A cor do jogo (verde puro, amarelo puro, branco) um pouco mais suave, como o resto da tela.
        return rare switch { 2 => Good, 1 => Gold, _ => new Color(r, g, b) };
    }

    /// <summary>Atualiza cartas e pacotes (a tela fica onde estava).</summary>
    public void SetData(List<CardEntry> cards, int packs)
    {
        _cards = cards;
        _packs = packs;
        if (IsNodeReady()) Refresh();
    }

    public void SetMessage(string text, bool bad)
    {
        _message = text;
        _messageBad = bad;
        if (IsNodeReady()) RefreshMessage();
    }

    public void SetTab(int tab)
    {
        _tab = Math.Clamp(tab, 0, 1);
        if (IsNodeReady()) Refresh();
    }

    public int Tab => _tab;
    public int Cursor => _cursor;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var dim = new ColorRect { Color = new Color(0.01f, 0.01f, 0.05f, 0.9f), MouseFilter = MouseFilterEnum.Ignore };
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

        // Alto: título e abas | pacotes e cartas.
        var top = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        top.AddThemeConstantOverride("separation", 16);
        page.AddChild(top);
        var titlePanel = Panel(new HBoxContainer(), 8);
        titlePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var titleRow = (HBoxContainer)titlePanel.GetChild(0);
        titleRow.AddThemeConstantOverride("separation", 18);
        var title = Text("Guardião das Cartas", 30, Gold);
        title.VerticalAlignment = VerticalAlignment.Center;
        titleRow.AddChild(title);
        _tabs = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.End };
        _tabs.AddThemeConstantOverride("separation", 10);
        titleRow.AddChild(_tabs);
        top.AddChild(titlePanel);
        var countPanel = Panel(new VBoxContainer(), 8);
        countPanel.CustomMinimumSize = new Vector2(300, 0);
        _counters = (VBoxContainer)countPanel.GetChild(0);
        _counters.AddThemeConstantOverride("separation", 2);
        top.AddChild(countPanel);
        // Celular: o botão FECHAR fica no canto de cima à direita (TouchControls); o quadro não fica por baixo dele.
        if (_touch) top.AddChild(new Control { CustomMinimumSize = new Vector2(96, 0), MouseFilter = MouseFilterEnum.Ignore });

        // Corpo: uma das duas abas.
        var body = new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        page.AddChild(body);
        _albumBody = BuildAlbum();
        _albumBody.SetAnchorsPreset(LayoutPreset.FullRect);
        body.AddChild(_albumBody);
        _packBody = BuildPack();
        _packBody.SetAnchorsPreset(LayoutPreset.FullRect);
        body.AddChild(_packBody);

        _messageLabel = Text("", 20, Pink, HorizontalAlignment.Center);
        _messageLabel.ClipText = true;
        page.AddChild(_messageLabel);
        var bottom = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        bottom.AddThemeConstantOverride("separation", 16);
        var leave = ActionButton("Sair", () => LeaveRequested?.Invoke(), 150, _touch ? 52 : 40, 22);
        bottom.AddChild(leave);
        _help = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _help.AddThemeConstantOverride("separation", 22);
        bottom.AddChild(_help);
        bottom.AddChild(new Control { CustomMinimumSize = new Vector2(150, 0), MouseFilter = MouseFilterEnum.Ignore }); // ajuda no meio
        page.AddChild(bottom);

        Refresh();
        FitScreen();
    }

    // ---------- aparência (a mesma do menu do TAB e das lojas) ----------

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
        theme.SetColor("font_disabled_color", "Button", Dim);
        theme.SetColor("font_outline_color", "Button", new Color(0.02f, 0.02f, 0.12f));
        theme.SetConstant("outline_size", "Button", 4);
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

    static Button ActionButton(string text, Action press, float width, float height, int size)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(width, height), FocusMode = FocusModeEnum.None, ClipText = true };
        b.AddThemeFontSizeOverride("font_size", size);
        b.AddThemeStyleboxOverride("normal", Box(new Color(0.16f, 0.2f, 0.55f, 0.95f), Lilac, 2, 10));
        b.AddThemeStyleboxOverride("hover", Box(new Color(0.24f, 0.29f, 0.72f, 0.95f), Gold, 2, 10));
        b.AddThemeStyleboxOverride("pressed", Box(Focus, Gold, 2, 10));
        b.AddThemeStyleboxOverride("disabled", Box(new Color(0.1f, 0.11f, 0.28f, 0.9f), Line, 1, 10));
        b.AddThemeColorOverride("font_hover_color", Gold);
        b.Pressed += press;
        return b;
    }

    static TextureRect Art(Texture2D? tex) => new()
    {
        Texture = tex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        MouseFilter = MouseFilterEnum.Ignore, TextureFilter = TextureFilterEnum.Linear,
    };

    static void Clear(Node box)
    {
        foreach (var c in box.GetChildren()) { box.RemoveChild(c); c.QueueFree(); }
    }

    static Control Rule() => new ColorRect { Color = Line, CustomMinimumSize = new Vector2(0, 2), MouseFilter = MouseFilterEnum.Ignore };

    // ---------- aba Álbum ----------

    Control BuildAlbum()
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 16);
        var gridPanel = Panel(new CenterContainer(), 12);
        gridPanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var grid = new GridContainer { Columns = Cols, MouseFilter = MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        gridPanel.GetChild(0).AddChild(grid);
        for (int i = 0; i < CardGameRows; i++) grid.AddChild(Tile(i));
        row.AddChild(gridPanel);

        var infoPanel = Panel(new VBoxContainer(), 14);
        infoPanel.CustomMinimumSize = new Vector2(330, 0);
        var info = (VBoxContainer)infoPanel.GetChild(0);
        info.AddThemeConstantOverride("separation", 6);
        _bigArt = Art(null);
        _bigArt.CustomMinimumSize = new Vector2(0, 230);
        info.AddChild(_bigArt);
        _bigName = Text("", 28, Gold, HorizontalAlignment.Center);
        _bigName.ClipText = true;
        info.AddChild(_bigName);
        info.AddChild(Rule());
        _bigNumber = Text("", 20, Dim);
        info.AddChild(_bigNumber);
        _bigRare = Text("", 21);
        info.AddChild(_bigRare);
        _bigChance = Text("", 20, Lilac);
        info.AddChild(_bigChance);
        _bigOwned = Text("", 21);
        info.AddChild(_bigOwned);
        info.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        info.AddChild(Rule());
        _summary = Text("", 19, Dim);
        info.AddChild(_summary);
        row.AddChild(infoPanel);
        return row;
    }

    // A tabela "card" tem 50 linhas (FATO, area.afs #621); a grade é montada uma vez com esse tamanho.
    const int CardGameRows = 50;

    Button Tile(int index)
    {
        var b = new Button { CustomMinimumSize = new Vector2(78, 78), FocusMode = FocusModeEnum.None, MouseFilter = MouseFilterEnum.Pass };
        var art = Art(null);
        art.SetAnchorsPreset(LayoutPreset.FullRect);
        art.OffsetLeft = art.OffsetTop = 3;
        art.OffsetRight = art.OffsetBottom = -3;
        b.AddChild(art);
        var number = Text($"{index + 1:D2}", 16, Colors.White, HorizontalAlignment.Right);
        number.SetAnchorsPreset(LayoutPreset.BottomRight);
        number.GrowHorizontal = GrowDirection.Begin;
        number.GrowVertical = GrowDirection.Begin;
        number.OffsetRight = -5;
        number.OffsetBottom = -1;
        b.AddChild(number);
        b.MouseEntered += () => { if (!_touch) Select(index); };
        b.GuiInput += e =>
        {
            if (e is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) return;
            Select(index);
            b.AcceptEvent();
        };
        _tiles.Add(b);
        return b;
    }

    void RefreshTiles()
    {
        for (int i = 0; i < _tiles.Count; i++)
        {
            var c = i < _cards.Count ? _cards[i] : null;
            var art = (TextureRect)_tiles[i].GetChild(0);
            var number = (Label)_tiles[i].GetChild(1);
            bool owned = c?.Owned == true;
            art.Texture = owned ? c!.Art ?? _back : _back;
            // Carta que falta: o verso apagado, com o número em destaque.
            art.Modulate = owned ? Colors.White : new Color(0.32f, 0.33f, 0.45f, 0.9f);
            number.AddThemeColorOverride("font_color", owned ? Colors.White : Dim);
            number.AddThemeFontSizeOverride("font_size", owned ? 16 : 22);
            bool on = i == _cursor;
            // A raridade já está na moldura da arte; o quadro da grade só marca a carta escolhida (dourado e grosso).
            var border = on ? Gold : new Color(0.3f, 0.32f, 0.5f, 0.8f);
            var normal = Box(on ? Gold * new Color(1, 1, 1, 0.35f) : new Color(0.06f, 0.07f, 0.24f, 0.9f), border, on ? 5 : 1, 8);
            _tiles[i].AddThemeStyleboxOverride("normal", normal);
            _tiles[i].AddThemeStyleboxOverride("hover", on ? normal : Box(new Color(0.18f, 0.2f, 0.5f, 0.9f), Lilac, 2, 8));
            _tiles[i].AddThemeStyleboxOverride("pressed", normal);
        }
    }

    void Select(int index)
    {
        if (_cards.Count == 0) return;
        _cursor = Math.Clamp(index, 0, Math.Min(_cards.Count, _tiles.Count) - 1);
        RefreshTiles();
        RefreshInfo();
    }

    void RefreshInfo()
    {
        if (_cursor < 0 || _cursor >= _cards.Count) return;
        var c = _cards[_cursor];
        _bigArt.Texture = c.Owned ? c.Art ?? _back : _back;
        _bigArt.Modulate = c.Owned ? Colors.White : new Color(0.45f, 0.46f, 0.6f);
        _bigName.Text = c.Owned ? c.Name : "????";
        _bigName.AddThemeColorOverride("font_color", c.Owned ? Gold : Dim);
        _bigNumber.Text = $"Carta {_cursor + 1} de {_cards.Count}";
        _bigRare.Text = $"Raridade: {RareName(c.Rare)}";
        _bigRare.AddThemeColorOverride("font_color", RareColor(c.Rare));
        _bigChance.Text = $"Chance no B. Pack: {(c.Chance / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')}%";
        _bigOwned.Text = c.Owned ? "Na sua coleção" : "Ainda não tem";
        _bigOwned.AddThemeColorOverride("font_color", c.Owned ? Good : Pink);
    }

    // ---------- aba B. Pack ----------

    Control BuildPack()
    {
        var panel = Panel(new VBoxContainer(), 18);
        var box = (VBoxContainer)panel.GetChild(0);
        box.AddThemeConstantOverride("separation", 14);
        box.Alignment = BoxContainer.AlignmentMode.Center;
        _packCount = Text("", 24, null, HorizontalAlignment.Center);
        box.AddChild(_packCount);
        var cards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        cards.AddThemeConstantOverride("separation", 40);
        for (int i = 0; i < _drawn.Length; i++)
        {
            var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(250, 0) };
            col.AddThemeConstantOverride("separation", 6);
            var frame = new PanelContainer { CustomMinimumSize = new Vector2(250, 250), MouseFilter = MouseFilterEnum.Ignore };
            frame.AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.04f, 0.16f, 0.9f), Line, 2, 10));
            _drawn[i] = Art(_back);
            _drawn[i].Modulate = new Color(0.5f, 0.5f, 0.65f);
            frame.AddChild(_drawn[i]);
            col.AddChild(frame);
            _drawnName[i] = Text("", 24, Colors.White, HorizontalAlignment.Center);
            _drawnName[i].ClipText = true;
            col.AddChild(_drawnName[i]);
            _drawnTag[i] = Text("", 21, Dim, HorizontalAlignment.Center);
            col.AddChild(_drawnTag[i]);
            cards.AddChild(col);
        }
        box.AddChild(cards);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        _openButton = ActionButton("Abrir um B. Pack", () => OpenPackRequested?.Invoke(), 340, _touch ? 64 : 54, 26);
        buttons.AddChild(_openButton);
        box.AddChild(buttons);
        return panel;
    }

    /// <summary>Mostra as cartas sorteadas: viram uma por uma (verso → frente), com "NOVA!" ou "repetida".</summary>
    public void ShowDrawn(IReadOnlyList<int> rows, IReadOnlyList<bool> isNew)
    {
        _tab = 1;
        Refresh();
        _flip?.Kill();
        _flip = CreateTween();
        for (int i = 0; i < _drawn.Length; i++)
        {
            var rect = _drawn[i];
            rect.Texture = _back;
            rect.Modulate = Colors.White;
            rect.PivotOffset = rect.Size / 2;
            rect.Scale = Vector2.One;
            _drawnName[i].Text = "";
            _drawnTag[i].Text = "";
        }
        for (int i = 0; i < _drawn.Length && i < rows.Count; i++)
        {
            int k = i, row = rows[i];
            var rect = _drawn[k];
            var card = row >= 0 && row < _cards.Count ? _cards[row] : null;
            _flip.TweenInterval(k == 0 ? 0.2 : 0.15);
            _flip.TweenCallback(Callable.From(() => rect.PivotOffset = rect.Size / 2));
            _flip.TweenProperty(rect, "scale", new Vector2(0, 1.06f), 0.14);
            _flip.TweenCallback(Callable.From(() =>
            {
                rect.Texture = card?.Art ?? _back;
                _drawnName[k].Text = card?.Name ?? "?";
                _drawnName[k].AddThemeColorOverride("font_color", card != null ? RareColor(card.Rare) : Colors.White);
                bool fresh = k < isNew.Count && isNew[k];
                _drawnTag[k].Text = (card != null ? RareName(card.Rare) + " · " : "") + (fresh ? "NOVA!" : "repetida");
                _drawnTag[k].AddThemeColorOverride("font_color", fresh ? Gold : Dim);
            }));
            _flip.TweenProperty(rect, "scale", Vector2.One, 0.16);
        }
    }

    // ---------- conteúdo ----------

    void Refresh()
    {
        Clear(_tabs);
        string[] names = ["Álbum", "B. Pack"];
        for (int i = 0; i < names.Length; i++)
        {
            int k = i;
            var b = new Button { Text = names[i], CustomMinimumSize = new Vector2(150, _touch ? 48 : 40), FocusMode = FocusModeEnum.None, ClipText = true, SizeFlagsVertical = SizeFlags.ShrinkCenter };
            b.AddThemeFontSizeOverride("font_size", 22);
            bool on = i == _tab;
            b.AddThemeStyleboxOverride("normal", on ? Box(Focus, Gold, 2, 10) : Box(new Color(0.08f, 0.09f, 0.3f, 0.9f), Line, 1, 10));
            b.AddThemeColorOverride("font_color", on ? Gold : Dim);
            b.Pressed += () => { if (k != _tab) { _tab = k; SetMessage("", false); Refresh(); } };
            _tabs.AddChild(b);
        }
        int owned = _cards.Count(c => c.Owned);
        Clear(_counters);
        _counters.AddChild(Counter("B. Pack", _packs.ToString()));
        _counters.AddChild(Counter("Cartas", $"{owned}/{_cards.Count}"));

        _albumBody.Visible = _tab == 0;
        _packBody.Visible = _tab == 1;
        RefreshTiles();
        RefreshInfo();
        var byRare = Enumerable.Range(0, 3).Select(r => (r, have: _cards.Count(c => c.Rare == r && c.Owned), all: _cards.Count(c => c.Rare == r)));
        _summary.Text = string.Join("   ", byRare.Select(x => $"{RareName(x.r)} {x.have}/{x.all}"));

        _packCount.Text = _packs > 0 ? $"Você tem {_packs} B. Pack{(_packs > 1 ? "s" : "")}. Cada um traz {CardPack.CardsPerPack} cartas." : "Você não tem B. Pack.";
        _openButton.Disabled = _packs <= 0;
        _openButton.Text = _packs > 0 ? "Abrir um B. Pack" : "Sem B. Pack";
        RefreshMessage();
        BuildHelp();
    }

    static Control Counter(string label, string value)
    {
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        var l = Text(label, 21);
        l.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(l);
        row.AddChild(Text(value, 21, Gold, HorizontalAlignment.Right));
        box.AddChild(row);
        box.AddChild(Rule());
        return box;
    }

    void RefreshMessage()
    {
        _messageLabel.Text = _message;
        _messageLabel.AddThemeColorOverride("font_color", _messageBad ? Pink : Good);
    }

    void BuildHelp()
    {
        Clear(_help);
        if (_touch && DeviceIcons.Current == ButtonNames.Kind.Keyboard)
        {
            _help.AddChild(Text(_tab == 0 ? "Toque numa carta para ver" : "Toque em Abrir um B. Pack", 18, Dim));
            return;
        }
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
        if (_tab == 0) Item(null, "Escolher carta", pad ? "Direcional" : "Setas/Mouse");
        else Item(Bind("confirmar"), "Abrir B. Pack");
        Item(pad ? "botao:9" : "tecla:Q", "");
        Item(pad ? "botao:10" : "tecla:E", "Aba");
        Item(Bind("cancelar"), _tab == 0 ? "Sair" : "Álbum");
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

    void ChangeTab(int dir)
    {
        _tab = ((_tab + dir) % 2 + 2) % 2;
        SetMessage("", false);
        Refresh();
    }

    void Back()
    {
        if (_tab == 1) ChangeTab(-1);
        else LeaveRequested?.Invoke();
    }

    void Move(int dx, int dy)
    {
        if (_tab != 0 || _cards.Count == 0) return;
        int n = Math.Min(_cards.Count, _tiles.Count), rows = (n + Cols - 1) / Cols;
        int col = _cursor % Cols, row = _cursor / Cols;
        if (dx != 0)
        {
            int i = _cursor + dx;
            Select(i < 0 ? n - 1 : i >= n ? 0 : i);
            return;
        }
        row = ((row + dy) % rows + rows) % rows;
        int next = row * Cols + col;
        if (next >= n) next = dy > 0 ? col : n - 1;
        Select(next);
    }

    static bool Hit(InputEvent e, string action, bool echo = false) =>
        InputMap.HasAction(PortInput.Name(action)) && e.IsActionPressed(PortInput.Name(action), allowEcho: echo, exactMatch: false);

    public override void _Input(InputEvent e)
    {
        if (!Visible) return;
        DeviceIcons.Seen(e);
        bool KeyOnce(params Key[] keys) => e is InputEventKey { Pressed: true, Echo: false } k && keys.Contains(k.Keycode);
        bool Pad(params JoyButton[] buttons) => e is InputEventJoypadButton { Pressed: true } jb && buttons.Contains(jb.ButtonIndex);
        bool mouse = e is InputEventMouse;
        bool handled = true;
        if (!mouse && (Hit(e, "pausa") || Hit(e, "status"))) LeaveRequested?.Invoke();
        else if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } || !mouse && Hit(e, "cancelar")) Back();
        else if (KeyOnce(Godot.Key.Q) || Pad(JoyButton.LeftShoulder)) ChangeTab(-1);
        else if (KeyOnce(Godot.Key.E, Godot.Key.Tab) || Pad(JoyButton.RightShoulder, JoyButton.Y)) ChangeTab(1);
        else if (!mouse && (Hit(e, "cima", true) || Pad(JoyButton.DpadUp))) Move(0, -1);
        else if (!mouse && (Hit(e, "baixo", true) || Pad(JoyButton.DpadDown))) Move(0, 1);
        else if (!mouse && (Hit(e, "esquerda", true) || Pad(JoyButton.DpadLeft))) Move(-1, 0);
        else if (!mouse && (Hit(e, "direita", true) || Pad(JoyButton.DpadRight))) Move(1, 0);
        else if (!mouse && (Hit(e, "confirmar") || Hit(e, "ataque")))
        {
            // Na aba B. Pack abre um pacote; no álbum leva à aba B. Pack.
            if (_tab == 1) { if (_packs > 0) OpenPackRequested?.Invoke(); else SetMessage("Você não tem B. Pack.", true); }
            else ChangeTab(1);
        }
        else handled = false;
        if (handled) GetViewport().SetInputAsHandled();
    }
}
