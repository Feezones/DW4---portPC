using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Save;

namespace DW4.Client;

/// <summary>
/// Telas de vagas e de criação com layout próprio (DECISÃO DO PORT, autorizada pelo usuário em 2026-11-02: "se não estiver
/// conseguindo reproduzir fielmente, pode fazer outro do 0, sendo parecido"). A montagem com as peças originais da stagIS12/13
/// (j_saveareawin, j_mainstatuswin…) dependia de uma posição-base do código nativo que não foi lida e saía quebrada.
/// Desenho com controles da Godot e a fonte do jogo, no estilo das janelas do jogo (vermelho escuro, bordas azul/roxo,
/// cantos arredondados), sobre o túnel do título escurecido; tudo escala com a altura da janela (base 720 linhas).
/// </summary>
public partial class MainMenu
{
    const float BaseHeight = 720;
    Control? _screen, _screenContent;
    ModelView? _bigModel;
    Label? _detail;
    bool _creating;
    int _createSlotUi, _createFormUi;
    LineEdit? _nameEdit;
    Label? _formLabel, _createStatus;
    readonly List<Button> _cards = [];

    static readonly Color CardBg = new(0.22f, 0.05f, 0.08f, 0.92f), CardBorder = new(0.45f, 0.42f, 0.95f), Gold = new(1f, 0.85f, 0.35f);

    static StyleBoxFlat Box(Color bg, Color border, int width, int radius)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 8 };
        s.SetBorderWidthAll(width);
        s.SetCornerRadiusAll(radius);
        s.SetContentMarginAll(10);
        return s;
    }

    static Label Text(string text, int size, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.AddThemeColorOverride("font_color", c);
        return l;
    }

    // Tela cheia: fundo escurecido, conteúdo na escala da altura da janela e a barra de botões embaixo.
    VBoxContainer NewScreen(string title)
    {
        Clear();
        _center.Visible = false;
        _screen = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_screen);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.7f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        _screen.AddChild(dim);
        // O fim do túnel é um clarão branco: um escuro radial por cima dele, sumindo para as bordas.
        var glow = new Gradient { Offsets = [0f, 0.55f, 1f], Colors = [new Color(0, 0, 0, 0.9f), new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0)] };
        var hole = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = glow, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(0.5f, 0f), Width = 256, Height = 256 },
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore,
        };
        hole.SetAnchorsPreset(LayoutPreset.FullRect);
        _screen.AddChild(hole);
        _screenContent = new Control { Theme = ScreenTheme() };
        _screen.AddChild(_screenContent);
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right" }) margin.AddThemeConstantOverride("margin_" + side, 48);
        margin.AddThemeConstantOverride("margin_top", 28);
        margin.AddThemeConstantOverride("margin_bottom", 18);
        _screenContent.AddChild(margin);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);
        column.AddChild(Text(title, 40, Gold, HorizontalAlignment.Center));
        FitScreen();
        return column;
    }

    // Fonte do jogo e botões/campos no estilo dos cartões.
    Theme ScreenTheme()
    {
        var theme = _gameTheme?.Duplicate() as Theme ?? new Theme();
        theme.SetStylebox("normal", "Button", Box(new Color(0.35f, 0.08f, 0.12f, 0.95f), CardBorder, 2, 12));
        theme.SetStylebox("hover", "Button", Box(new Color(0.45f, 0.12f, 0.16f, 0.95f), CardBorder.Lightened(0.2f), 2, 12));
        theme.SetStylebox("focus", "Button", Box(new Color(0.5f, 0.14f, 0.18f, 0.95f), Gold, 3, 12));
        theme.SetStylebox("pressed", "Button", Box(new Color(0.55f, 0.16f, 0.2f, 0.95f), Gold, 3, 12));
        theme.SetColor("font_focus_color", "Button", Gold);
        theme.SetStylebox("normal", "LineEdit", Box(new Color(0.04f, 0.04f, 0.12f, 0.95f), CardBorder, 2, 10));
        theme.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), Gold, 3, 10));
        return theme;
    }

    void FitScreen()
    {
        if (_screenContent == null || !IsInsideTree()) return;
        var win = GetViewportRect().Size;
        float s = win.Y / BaseHeight;
        _screenContent.Scale = new Vector2(s, s);
        _screenContent.Size = win / s;
    }

    // Barra de ajuda: ícone do botão do aparelho atual (desenhado pelo port) e o que ele faz.
    Control HelpBar(params (string Action, string Label)[] items)
    {
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeConstantOverride("separation", 22);
        bool pad = DeviceIcons.Current != ButtonNames.Kind.Keyboard;
        var b = PortSettings.Current.Bindings;
        foreach (var (action, label) in items)
        {
            var item = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            item.AddThemeConstantOverride("separation", 6);
            string? binding = action switch
            {
                "setas" => null,
                "apagar" => pad ? "botao:2" : "tecla:Delete",
                _ => b.Get(pad ? InputBindings.Pad : InputBindings.Keyboard, action).FirstOrDefault(),
            };
            if (binding != null) item.AddChild(DeviceIcons.Chip(binding, pad ? DeviceIcons.Current : ButtonNames.Kind.Keyboard));
            else item.AddChild(Text(pad ? "Direcional" : "Setas", 18, Gold));
            item.AddChild(Text(label, 18));
            bar.AddChild(item);
        }
        return bar;
    }

    // ---------- vagas ----------

    bool ShowSlotsUi()
    {
        IReadOnlyList<CharacterSave?> slots;
        try { slots = Session.Store.List(Session.Account); }
        catch (Exception e) when (e is IOException or ArgumentException) { GD.PrintErr($"[menu] vagas: {e.Message}"); slots = new CharacterSave?[ICharacterStore.Slots]; }
        _slots = slots;
        var column = NewScreen("Seus Digimon");
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 28);
        column.AddChild(body);
        _bigModel = new ModelView(Common, new Vector2(360, 420), spin: true) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.8f };
        body.AddChild(_bigModel);
        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.2f, Alignment = BoxContainer.AlignmentMode.Center };
        right.AddThemeConstantOverride("separation", 16);
        body.AddChild(right);
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 14);
        grid.AddThemeConstantOverride("v_separation", 14);
        right.AddChild(grid);
        _cards.Clear();
        for (int i = 0; i < ICharacterStore.Slots; i++) grid.AddChild(SlotCard(i, i < slots.Count ? slots[i] : null));
        var detailPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 150) };
        detailPanel.AddThemeStyleboxOverride("panel", Box(new Color(0.06f, 0.06f, 0.16f, 0.92f), CardBorder, 2, 14));
        _detail = Text("", 20);
        detailPanel.AddChild(_detail);
        right.AddChild(detailPanel);
        column.AddChild(HelpBar(("setas", "Escolher"), ("confirmar", "Entrar / criar"), ("apagar", "Apagar"), ("cancelar", "Voltar")));
        _slotCursor = Math.Clamp(_slotCursor, 0, _cards.Count - 1);
        SelectSlot(_slotCursor);
        _cards[_slotCursor].CallDeferred(Control.MethodName.GrabFocus);
        return true;
    }

    Button SlotCard(int index, CharacterSave? c)
    {
        var card = new Button { CustomMinimumSize = new Vector2(330, 118), FocusMode = FocusModeEnum.All, ClipContents = true };
        card.AddThemeStyleboxOverride("normal", Box(CardBg, CardBorder, 2, 14));
        card.AddThemeStyleboxOverride("hover", Box(CardBg.Lightened(0.12f), CardBorder.Lightened(0.2f), 2, 14));
        card.AddThemeStyleboxOverride("focus", Box(CardBg.Lightened(0.18f), Gold, 3, 14));
        card.AddThemeStyleboxOverride("pressed", Box(CardBg.Lightened(0.25f), Gold, 3, 14));
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.SetAnchorsPreset(LayoutPreset.FullRect);
        row.OffsetLeft = 10; row.OffsetTop = 8; row.OffsetRight = -10; row.OffsetBottom = -8;
        row.AddThemeConstantOverride("separation", 12);
        card.AddChild(row);
        var info = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
        if (c != null)
        {
            var face = new ModelView(Common, new Vector2(96, 96), spin: false);
            face.Show(c.Model);
            row.AddChild(face);
            info.AddChild(Text(c.Name, 26, Gold));
            info.AddChild(Text($"{FormName(c.CurrentForm)}   Lv {c.Level}", 18));
            info.AddChild(Text(AreaName(c.Stage), 16, new Color(0.75f, 0.75f, 0.85f)));
        }
        else
        {
            row.AddChild(Text("+", 54, CardBorder.Lightened(0.3f)));
            info.AddChild(Text("Criar novo Digimon", 22));
            info.AddChild(Text($"Vaga {index + 1} vazia", 16, new Color(0.75f, 0.75f, 0.85f)));
        }
        row.AddChild(info);
        card.FocusEntered += () => SelectSlot(index);
        card.MouseEntered += () => card.GrabFocus();
        card.Pressed += () => { _slotCursor = index; if (c != null) Play(index, c); else ShowCreate(index); };
        card.GuiInput += e =>
        {
            if (c != null && (e is InputEventKey { Pressed: true, Keycode: Key.Delete } || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.X }))
            {
                card.AcceptEvent();
                ConfirmDelete(index, c);
            }
        };
        _cards.Add(card);
        return card;
    }

    // Quadro de detalhes e modelo grande da vaga escolhida.
    void SelectSlot(int index)
    {
        _slotCursor = index;
        var c = index < _slots.Count ? _slots[index] : null;
        _bigModel?.Show(c?.Model ?? "");
        if (_detail == null) return;
        if (c == null) { _detail.Text = $"Vaga {index + 1}\n\nVazia: crie um Digimon para começar."; return; }
        var (hp, mp) = SlotStats(c);
        _detail.Text = $"{c.Name}   ·   {FormName(c.CurrentForm)}   ·   Lv {c.Level}\n" +
            $"HP {(hp > 0 ? $"{hp} / {hp}" : "—")}      MP {(mp > 0 ? $"{mp} / {mp}" : "—")}\n" +
            $"Área: {AreaName(c.Stage)}\nGravado em {c.SavedAt.ToLocalTime():dd/MM/yyyy HH:mm}";
    }

    // Nome da área gravada, com os nomes do jogo (textos mswk7xxxx). FATO: as saídas (matz*.lua) mandam o nome do destino na
    // janela de troca (STCHGFONT00) junto com a fase de destino, e o mesmo destino tem o mesmo nome vindo de saídas diferentes
    // (dng307b1 e dng309b1 → stag0301 "Gekogeko Land"). As fases sem saída nomeada para elas ficam com o código.
    // HIPÓTESE: masmorras pela saída da fase que leva a elas, na ordem matz00, matz01 (stag0301 → dng307 "Geko Path" e dng309
    // "Vein"; stag0401 → dng411 "Mecha Nest" e dng412 "Electro Mine"). Lobby: a fase que nenhuma saída dela nomeia.
    // DECISÃO DO PORT: stag0000/0001 aparecem como "Tutorial".
    static readonly Dictionary<string, string> AreaNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stag0101"] = "Goblin Pass", ["stag0102"] = "Abyss of Grief", ["stag0104"] = "Doom Dome",
        ["stag0201"] = "Mummy Desert", ["stag0202"] = "Grief Wasteland", ["stag0204"] = "Station X", ["stag0205"] = "Power Plant",
        ["stag0301"] = "Gekogeko Land", ["stag0302"] = "Mangrove Forest", ["stag0303"] = "Venom Swamp", ["stag0304"] = "Dread Note",
        ["stag0401"] = "Battle Arena", ["stag0404"] = "Core Area",
        ["stagml11"] = "Terminal Area", ["stagml12"] = "Central Area", ["stagml13"] = "Shop Area",
        ["dng101"] = "Humid Cave", ["dng102"] = "Cliff Dungeon", ["dng103"] = "Dome Base",
        ["dng204"] = "Sand Labyrinth", ["dng205"] = "Ancient Ruins", ["dng206"] = "Virus Lab",
        ["dng307"] = "Geko Path", ["dng308"] = "Vine Tunnel", ["dng309"] = "Vein", ["dng310"] = "Dread Bridge",
        ["dng411"] = "Mecha Nest", ["dng412"] = "Electro Mine",
    };

    static string AreaName(string stage) => stage.Length == 0 ? "—"
        : stage.StartsWith("stag00", StringComparison.OrdinalIgnoreCase) ? "Tutorial"
        : AreaNames.TryGetValue(stage, out var n) ? n
        : stage.Length > 6 && AreaNames.TryGetValue(stage[..6], out var d) ? d
        : stage;

    // ---------- criação ----------

    bool ShowCreateUi(int slot)
    {
        _creating = true;
        _createSlotUi = slot;
        _createFormUi = Enumerable.Range(0, 4).FirstOrDefault(d => Playable(FormModel(d)), 0);
        var column = NewScreen("Criar Digimon");
        _creating = true; // o NewScreen limpou
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 28);
        column.AddChild(body);
        _bigModel = new ModelView(Common, new Vector2(420, 460), spin: true) { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(_bigModel);
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        panel.AddThemeStyleboxOverride("panel", Box(CardBg, CardBorder, 2, 16));
        body.AddChild(panel);
        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 16);
        panel.AddChild(form);
        form.AddChild(Text("Digimon", 20, new Color(0.75f, 0.75f, 0.85f)));
        var pick = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        pick.AddThemeConstantOverride("separation", 18);
        var prev = new Button { Text = "<", CustomMinimumSize = new Vector2(56, 52) };
        var next = new Button { Text = ">", CustomMinimumSize = new Vector2(56, 52) };
        _formLabel = Text("", 32, Gold, HorizontalAlignment.Center);
        _formLabel.CustomMinimumSize = new Vector2(240, 0);
        prev.Pressed += () => StepForm(-1);
        next.Pressed += () => StepForm(1);
        pick.AddChild(prev); pick.AddChild(_formLabel); pick.AddChild(next);
        form.AddChild(pick);
        form.AddChild(Text("Nome", 20, new Color(0.75f, 0.75f, 0.85f)));
        _nameEdit = new LineEdit { MaxLength = NameMax, PlaceholderText = "Digite o nome", CustomMinimumSize = new Vector2(0, 52) };
        _nameEdit.AddThemeFontSizeOverride("font_size", 26);
        _nameEdit.TextSubmitted += _ => CreateFromUi();
        form.AddChild(_nameEdit);
        form.AddChild(Text("Dificuldade: Normal (Hard e Very Hard são liberadas pelo progresso)", 16, new Color(0.75f, 0.75f, 0.85f)));
        _createStatus = Text("", 18, new Color(1f, 0.6f, 0.5f));
        form.AddChild(_createStatus);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 14);
        var create = new Button { Text = "Criar", CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var back = new Button { Text = "Voltar", CustomMinimumSize = new Vector2(0, 52), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        create.Pressed += CreateFromUi;
        back.Pressed += ShowSlots;
        buttons.AddChild(create); buttons.AddChild(back);
        form.AddChild(buttons);
        column.AddChild(HelpBar(("setas", "Digimon (fora do campo do nome)"), ("confirmar", "Criar"), ("cancelar", "Voltar")));
        StepForm(0);
        _nameEdit.CallDeferred(Control.MethodName.GrabFocus);
        return true;
    }

    void StepForm(int dir)
    {
        _createFormUi = (_createFormUi + 4 + dir) % 4;
        bool ok = Playable(FormModel(_createFormUi));
        if (_formLabel != null) _formLabel.Text = FormName(_createFormUi) + (ok ? "" : "\n(sem modelo)");
        _bigModel?.Show(ok ? FormModel(_createFormUi) : "");
    }

    void CreateFromUi()
    {
        var n = _nameEdit?.Text.Trim() ?? "";
        if (n.Length == 0) { _createStatus!.Text = "Dê um nome ao Digimon."; _nameEdit?.GrabFocus(); return; }
        if (!Playable(FormModel(_createFormUi))) { _createStatus!.Text = "Esse Digimon ainda não tem modelo no cliente."; return; }
        var save = CharacterSave.New(n, _createFormUi, FormModel(_createFormUi), 0, Session.NewGameStage, Session.NewGameEntry,
            _createFormUi < _shinka.Count ? _shinka[_createFormUi].Weapons : null);
        Session.Store.Save(Session.Account, _createSlotUi, save);
        _slotCursor = _createSlotUi;
        ShowSlots();
    }

    // Esquerda/direita trocam a forma na criação quando o campo do nome não está com o foco.
    bool CreateUiInput(InputEvent e)
    {
        if (!_creating || _screen == null) return false;
        if (e.IsActionPressed("ui_cancel")) { ShowSlots(); return true; }
        if (_nameEdit?.HasFocus() == true) return false;
        if (e.IsActionPressed("ui_left")) { StepForm(-1); return true; }
        if (e.IsActionPressed("ui_right")) { StepForm(1); return true; }
        return false;
    }
}
