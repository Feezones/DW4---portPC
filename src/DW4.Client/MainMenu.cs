using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Save;

namespace DW4.Client;

/// <summary>
/// Menu inicial do port (DECISÃO DO PORT, pedido do responsável; docs/decisoes-port-pc.md):
///   "Jogar online" → as 4 vagas da conta (vazia: "Criar novo Digimon"; ocupada: o Digimon criado ali) → escolhe a vaga → entra;
///   "Configurações" → jogo, gráfico, som e controles (user://config.cfg).
/// Mouse, teclado e controle (foco dos botões pelas ações ui_* da Godot). A criação usa o que se sabe da stagIS13: as 4
/// formas iniciais (FATO: DIGIMONNO 0..3 no GAMEDATAINIT), nome e dificuldade (mswkMem0232, 0163/0167, 0168). As iniciais sem modelo
/// exportado aparecem desligadas.
/// Abrir com DW4_FASE definido (depuração) pula o menu e vai direto à fase, sem vaga.
/// </summary>
public partial class MainMenu : Control
{
    // HIPÓTESE (DIFFICULTY 0..2 pela ordem das listas de drop nZ1/hZ1/vZ1): Normal, Hard, Very Hard. "Normal" e "Very Hard" são
    // textos da stagIS13; "Hard" não está nos textos exportados.
    static readonly string[] Difficulties = ["Normal", "Hard", "Very Hard"];
    // O cliente desenha e controla as formas cuja pasta foi exportada (<fatia>/formas/<modelo>, ver Fatia.Evolve.cs).
    static readonly string Common = OS.GetEnvironment("DW4_FATIA") is { Length: > 0 } d ? d : @"D:\dw4-work\fatia";
    static bool Playable(string model) => Fatia.HasPlayerModel(Common, model);
    const int NameMax = 12; // DECISÃO DO PORT: o limite do jogo não foi lido

    VBoxContainer _root = null!;
    List<(string Name, string Model)> _shinka = [];

    public override void _Ready()
    {
        PortSettings.Apply(GetTree().Root);
        if (OS.GetEnvironment("DW4_FASE").Length > 0)
        {
            CallDeferred(MethodName.GoStraight);
            return;
        }
        _shinka = LoadShinka();
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color(0.05f, 0.06f, 0.1f), AnchorRight = 1, AnchorBottom = 1 });
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        _root = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        _root.AddThemeConstantOverride("separation", 14);
        center.AddChild(_root);
        ShowTitle();
    }

    void GoStraight() => GetTree().ChangeSceneToFile(Session.GameScene);

    // Nomes das formas (NAME e MODEL_NAME da shinka), lidos da pasta de trabalho: nada do jogo fica no repositório.
    static List<(string, string)> LoadShinka()
    {
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        var csv = Path.Combine(stages, Session.NewGameStage, "db", "db_shinka.csv");
        if (!File.Exists(csv)) return [];
        var lines = File.ReadAllLines(csv);
        var head = lines[0].TrimStart('\uFEFF').Split(';');
        int name = Array.IndexOf(head, "NAME"), model = Array.IndexOf(head, "MODEL_NAME");
        if (name < 0 || model < 0) return [];
        return lines.Skip(1).Select(l => l.Split(';')).Where(c => c.Length > Math.Max(name, model)).Select(c => (c[name], c[model])).ToList();
    }

    string FormName(int d) => d >= 0 && d < _shinka.Count ? _shinka[d].Name : $"Digimon {d}";
    string FormModel(int d) => d >= 0 && d < _shinka.Count ? _shinka[d].Model : "";

    // ---------- telas ----------

    void Clear()
    {
        foreach (var c in _root.GetChildren()) { _root.RemoveChild(c); c.QueueFree(); }
    }

    Label Title(string text, int size = 40)
    {
        var l = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", size);
        _root.AddChild(l);
        return l;
    }

    Button Btn(Container parent, string text, Action click, bool enabled = true)
    {
        var b = new Button { Text = text, Disabled = !enabled, CustomMinimumSize = new Vector2(0, 48) };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.Pressed += click;
        parent.AddChild(b);
        return b;
    }

    void ShowTitle()
    {
        Clear();
        Title("Digimon World 4", 52);
        Title("port para PC", 20);
        var first = Btn(_root, "Jogar online", ShowSlots);
        Btn(_root, "Configurações", ShowSettings);
        Btn(_root, "Sair", () => GetTree().Quit());
        first.CallDeferred(Control.MethodName.GrabFocus);
    }

    void ShowSlots()
    {
        Clear();
        Title("Seus Digimon");
        Title($"conta: {Session.Account}", 16);
        IReadOnlyList<CharacterSave?> slots;
        try { slots = Session.Store.List(Session.Account); }
        catch (Exception e) when (e is IOException or ArgumentException) { Title($"Não deu para ler as vagas: {e.Message}", 16); slots = new CharacterSave?[ICharacterStore.Slots]; }
        Button? first = null;
        for (int i = 0; i < slots.Count; i++)
        {
            int slot = i;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            _root.AddChild(row);
            Button main;
            if (slots[i] is { } c)
            {
                main = Btn(row, $"{c.Name} — {FormName(c.CurrentForm)}  nv {c.Level}   ({c.Stage})", () => Play(slot, c));
                main.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                Btn(row, "Apagar", () => ConfirmDelete(slot, c));
            }
            else
            {
                main = Btn(row, "Criar novo Digimon", () => ShowCreate(slot));
                main.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            }
            first ??= main;
        }
        Btn(_root, "Voltar", ShowTitle);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Play(int slot, CharacterSave c)
    {
        if (!Playable(c.Model)) { Message($"{FormName(c.CurrentForm)} ainda não tem modelo no cliente.", ShowSlots); return; }
        if (!Session.Begin(GetTree(), slot, c, out var error)) Message(error ?? "Não deu para entrar.", ShowSlots);
    }

    void ConfirmDelete(int slot, CharacterSave c)
    {
        Clear();
        Title($"Apagar {c.Name} ({FormName(c.CurrentForm)} nv {c.Level})?", 26);
        Title("Isso não pode ser desfeito.", 18);
        var no = Btn(_root, "Não, manter", ShowSlots);
        Btn(_root, "Sim, apagar", () => { Session.Store.Delete(Session.Account, slot); ShowSlots(); });
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Message(string text, Action back)
    {
        Clear();
        Title(text, 22);
        Btn(_root, "OK", back).CallDeferred(Control.MethodName.GrabFocus);
    }

    // Criar: forma inicial (0..3), nome e dificuldade.
    void ShowCreate(int slot)
    {
        Clear();
        Title("Criar Digimon", 36);
        Title("Escolha o Digimon", 18);
        var forms = new HBoxContainer();
        forms.AddThemeConstantOverride("separation", 8);
        _root.AddChild(forms);
        int chosen = -1;
        var picks = new List<Button>();
        for (int d = 0; d < 4; d++)
        {
            int form = d;
            bool ok = Playable(FormModel(d));
            var b = Btn(forms, FormName(d) + (ok ? "" : "\n(sem modelo ainda)"), () => { chosen = form; foreach (var p in picks) p.ButtonPressed = p == picks[form]; }, ok);
            b.ToggleMode = true;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            picks.Add(b);
            if (ok && chosen < 0) { chosen = d; b.ButtonPressed = true; }
        }
        Title("Nome", 18);
        var name = new LineEdit { MaxLength = NameMax, PlaceholderText = "Nome do seu Digimon", CustomMinimumSize = new Vector2(0, 44) };
        name.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(name);
        Title("Dificuldade", 18);
        var diff = new OptionButton { CustomMinimumSize = new Vector2(0, 44) };
        foreach (var t in Difficulties) diff.AddItem(t);
        _root.AddChild(diff);
        var status = Title("", 16);
        var buttons = new HBoxContainer();
        _root.AddChild(buttons);
        var create = Btn(buttons, "Criar", () =>
        {
            var n = name.Text.Trim();
            if (n.Length == 0) { status.Text = "Dê um nome ao Digimon."; name.GrabFocus(); return; }
            if (chosen < 0) { status.Text = "Escolha um Digimon."; return; }
            var save = CharacterSave.New(n, chosen, FormModel(chosen), diff.Selected, Session.NewGameStage, Session.NewGameEntry);
            Session.Store.Save(Session.Account, slot, save);
            ShowSlots();
        });
        create.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var cancel = Btn(buttons, "Cancelar", ShowSlots);
        cancel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.TextSubmitted += _ => create.EmitSignal(BaseButton.SignalName.Pressed);
        name.CallDeferred(Control.MethodName.GrabFocus);
    }

    // ---------- configurações ----------

    void ShowSettings()
    {
        Clear();
        Title("Configurações");
        var c = PortSettings.Current;
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 380) };
        _root.AddChild(tabs);

        var game = Page(tabs, "Jogo");
        Choice(game, "Câmera da dungeon", ["Perto", "Longe"], c.CameraMode, v => c.CameraMode = v);
        Choice(game, "Luz", ["Suave", "Forte"], c.LightMode, v => c.LightMode = v);
        Check(game, "Vibração do controle", c.Vibration, v => c.Vibration = v);
        Note(game, "Textos do jogo: inglês (a versão lida).");

        var video = Page(tabs, "Gráfico");
        var res = PortConfig.Resolutions.ToList();
        int cur = res.FindIndex(r => r.W == c.Width && r.H == c.Height);
        if (cur < 0) { res.Insert(0, (c.Width, c.Height)); cur = 0; }
        Choice(video, "Resolução (janela)", res.Select(r => $"{r.W} × {r.H}").ToArray(), cur, v => (c.Width, c.Height) = res[v]);
        Check(video, "Tela cheia", c.Fullscreen, v => c.Fullscreen = v);
        Check(video, "VSync", c.VSync, v => c.VSync = v);
        Slider(video, "Escala da interface", 0.5f, 2f, 0.05f, c.UiScale, v => c.UiScale = v);

        var sound = Page(tabs, "Som");
        Slider(sound, "Volume geral", 0, 1, 0.05f, c.MasterVolume, v => c.MasterVolume = v);
        Slider(sound, "Música", 0, 1, 0.05f, c.MusicVolume, v => c.MusicVolume = v);
        Slider(sound, "Efeitos", 0, 1, 0.05f, c.EffectsVolume, v => c.EffectsVolume = v);

        var keys = Page(tabs, "Controles");
        foreach (var (what, key) in Controls) Note(keys, $"{what}: {key}");

        var buttons = new HBoxContainer();
        _root.AddChild(buttons);
        var apply = Btn(buttons, "Aplicar", () => { PortSettings.Save(); PortSettings.Apply(GetTree().Root); });
        apply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var back = Btn(buttons, "Voltar", () => { PortSettings.Save(); PortSettings.Apply(GetTree().Root); ShowTitle(); });
        back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabs.GetTabBar().CallDeferred(Control.MethodName.GrabFocus);
    }

    // Teclas que o cliente usa hoje (Fatia*.cs). Remapear fica para depois.
    static readonly (string, string)[] Controls =
    [
        ("Andar", "WASD / setas / analógico"),
        ("Atacar", "J / botão esquerdo do mouse / X"),
        ("Técnica", "K / 1..6 / botão direito"),
        ("Pular", "Espaço"),
        ("Menu de status", "Tab / Start"),
        ("Confirmar", "J / Enter / A"),
        ("Cancelar", "K / Backspace / B"),
        ("Equipar o último item", "E"),
        ("Voltar ao início da fase", "F5"),
        ("Trocar conjunto de câmera", "F1"),
        ("Gravar e voltar ao menu", "Esc"),
    ];

    static VBoxContainer Page(TabContainer tabs, string title)
    {
        var page = new VBoxContainer { Name = title };
        page.AddThemeConstantOverride("separation", 10);
        tabs.AddChild(page);
        return page;
    }

    static void Note(Container page, string text) => page.AddChild(new Label { Text = text });

    static void Choice(Container page, string label, string[] options, int selected, Action<int> set)
    {
        var row = Row(page, label);
        var o = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var t in options) o.AddItem(t);
        o.Selected = Math.Clamp(selected, 0, options.Length - 1);
        o.ItemSelected += i => set((int)i);
        row.AddChild(o);
    }

    static void Check(Container page, string label, bool value, Action<bool> set)
    {
        var b = new CheckButton { Text = label, ButtonPressed = value };
        b.Toggled += v => set(v);
        page.AddChild(b);
    }

    static void Slider(Container page, string label, float min, float max, float step, float value, Action<float> set)
    {
        var row = Row(page, label);
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var shown = new Label { Text = $"{value:0.00}", CustomMinimumSize = new Vector2(56, 0) };
        s.ValueChanged += v => { set((float)v); shown.Text = $"{v:0.00}"; };
        row.AddChild(s);
        row.AddChild(shown);
    }

    static HBoxContainer Row(Container page, string label)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(240, 0) });
        page.AddChild(row);
        return row;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Cancelar (Esc / B) volta ao título de qualquer tela.
        if (e.IsActionPressed("ui_cancel") && _root != null) { ShowTitle(); GetViewport().SetInputAsHandled(); }
    }
}
