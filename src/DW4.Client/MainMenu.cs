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
/// Abrir com DW4_FASE definido (depuração) pula o menu e vai direto à fase, sem vaga. Ao abrir, toca a abertura (OpeningMovie).
/// Com a arte da stagIS10 exportada (TitleArt), o título é o do jogo: fundo animado, logotipo, "Press START button" e o menu
/// "Game Start" (→ vagas) / "Option" (→ configurações), com a troca de linha e a confirmação pelas sequências do jogo; as outras
/// telas ficam sobre o mesmo fundo, num painel com a fonte do jogo. Esc no "Press START" fecha o port.
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
    TitleArt? _art, _sel;
    ModelTurntable? _turntable;
    CenterContainer _center = null!;
    string _stages = "";
    enum TitleState { None, Press, Opening, Menu, Confirm, Cancel }
    TitleState _title;
    int _titleLine;
    bool _movieOn;
    List<(string Name, string Model, int[] Weapons)> _shinka = [];

    public override void _Ready()
    {
        PortSettings.Apply(GetTree().Root);
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_VER_MODELO").Length > 0)
        {
            CallDeferred(Node.MethodName.AddSibling, new ModelViewer());
            Visible = false;
            SetProcess(false);
            return;
        }
        if (OS.GetEnvironment("DW4_FASE").Length > 0)
        {
            CallDeferred(MethodName.GoStraight);
            return;
        }
        _shinka = LoadShinka();
        SetAnchorsPreset(LayoutPreset.FullRect);
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        var problems = new List<string>();
        _stages = stages;
        _art = TitleArt.Create(this, stages, -1, problems);
        // Fundo das telas de vagas e criação: j_chrselback (em perspectiva) e j_chrselbase da stagIS13 (dw4 telas-exportar).
        _sel = _art != null ? TitleArt.Create(this, System.IO.Path.Combine(stages, "stagis13", "telas"), "j_chrselback", -1, problems, "dw4 telas-exportar ndmw.afs 813 area.afs 650") : null;
        if (_sel != null) _sel.Visible = false;
        if (ModelTurntable.Available(stages)) _turntable = new ModelTurntable(this, stages, 0);
        // Música do título (FATO, first.lua da stagIS10: SYSgetArea 0 → AM_TITLE, senão AM_TITLE2; no beSoundInfo #806, AM_TITLE2 =
        // [0.20] = ndmw #20). DECISÃO DO PORT: toca em laço no título e nas telas de vagas, criação e configurações.
        var music = System.IO.Path.Combine(stages, "stagis10", "audio", "ndmw_0020.wav");
        if (System.IO.File.Exists(music) && Fatia.ReadWav(music, problems) is { } wav)
        {
            if (wav.LoopMode == AudioStreamWav.LoopModeEnum.Disabled) { wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward; wav.LoopBegin = 0; wav.LoopEnd = wav.Data.Length / (wav.Stereo ? 4 : 2); }
            _music = new AudioStreamPlayer { Stream = wav, Bus = PortSettings.MusicBus };
            AddChild(_music);
        }
        else problems.Add($"falta {music} (dw4 adx-wav ndmw.afs <fases>\\stagis10\\audio 20): título sem música");
        foreach (var p in problems) GD.Print($"[menu] {p}");
        if (_art == null) AddChild(new ColorRect { Color = new Color(0.05f, 0.06f, 0.1f), AnchorRight = 1, AnchorBottom = 1 });
        var center = _center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(center);
        _root = new VBoxContainer { CustomMinimumSize = new Vector2(720, 0) };
        _root.AddThemeConstantOverride("separation", 14);
        if (_art != null)
        {
            // Sobre o fundo animado: painel escuro com a fonte do jogo (a NORMAL, a das falas).
            _panel = new PanelContainer { SelfModulate = new Color(1, 1, 1, 0.92f) };
            _panel.AddChild(_root);
            center.AddChild(_panel);
            if (BitmapFont.Load(System.IO.Path.Combine(stages, "fonte"))?.ToFontFile("NORMAL") is { } gf) center.Theme = new Theme { DefaultFont = gf, DefaultFontSize = 26 };
        }
        else center.AddChild(_root);
        ShowTitle();
        // Abertura antes do título, uma vez por execução (a stagIS09 do jogo vem entre os logos e o título; OpeningMovie.cs).
        if (!_openingShown && OpeningMovie.CanPlay())
        {
            _openingShown = true;
            _root.Visible = false;
            _movieOn = true;
            var movie = new OpeningMovie();
            movie.Finished += () => { _movieOn = false; _root.Visible = true; _music?.Play(); ShowTitle(); };
            AddChild(movie);
        }
        else _music?.Play();
    }

    static bool _openingShown;
    PanelContainer? _panel;
    AudioStreamPlayer? _music;

    public override void _Process(double delta)
    {
        if (_art == null) return;
        _art.Tick((float)delta);
        if (_sel?.Visible == true) _sel.Tick((float)delta);
        _turntable?.Tick((float)delta);
        // Fim das animações de confirmação: segue para a tela pedida.
        if (_title == TitleState.Opening && !_art.Has("j_startmenu01")) { _art.Play("j_startmenu02", "SQ_OPEN"); (_title, _titleLine) = (TitleState.Menu, 0); }
        else if (_title == TitleState.Confirm && !_art.Has("j_startmenu02")) { if (_titleLine == 0) ShowSlots(); else ShowSettings(); }
        else if (_title == TitleState.Cancel && !_art.Has("j_startmenu02")) ShowTitle();
    }

    // Título do jogo: "Press START" → menu de duas linhas. Teclado, controle e mouse (clique confirma, roda troca a linha).
    bool TitleInput(InputEvent e)
    {
        if (_art == null || _title == TitleState.None || _movieOn) return false;
        bool up = e.IsActionPressed("ui_up") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp };
        bool down = e.IsActionPressed("ui_down") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown };
        bool ok = e.IsActionPressed("ui_accept") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
            || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Start };
        bool cancel = e.IsActionPressed("ui_cancel") || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right };
        switch (_title)
        {
            case TitleState.Press:
                if (cancel && e is InputEventKey) { GetTree().Quit(); return true; }
                bool any = ok || e is InputEventKey { Pressed: true, Echo: false } || e is InputEventJoypadButton { Pressed: true };
                if (!any) return false;
                _art.Play("j_startmenu01", "SQ_LINEON00");
                _title = TitleState.Opening;
                return true;
            case TitleState.Menu:
                if (down && _titleLine == 0) { _art.Play("j_startmenu02", "SQ_LINEDW01"); _titleLine = 1; return true; }
                if (up && _titleLine == 1) { _art.Play("j_startmenu02", "SQ_LINEUP00"); _titleLine = 0; return true; }
                if (ok) { _art.Play("j_startmenu02", $"SQ_LINEON0{_titleLine}"); _title = TitleState.Confirm; return true; }
                if (cancel) { _art.Play("j_startmenu02", "SQ_CANCEL"); _title = TitleState.Cancel; return true; }
                return up || down;
            default:
                return true; // animação em curso: segura a entrada
        }
    }

    void GoStraight() => GetTree().ChangeSceneToFile(Session.GameScene);

    // Nomes das formas (NAME e MODEL_NAME da shinka), lidos da pasta de trabalho: nada do jogo fica no repositório.
    static List<(string, string, int[])> LoadShinka()
    {
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        var csv = Path.Combine(stages, Session.NewGameStage, "db", "db_shinka.csv");
        if (!File.Exists(csv)) return [];
        var lines = File.ReadAllLines(csv);
        var head = lines[0].TrimStart('\uFEFF').Split(';');
        int name = Array.IndexOf(head, "NAME"), model = Array.IndexOf(head, "MODEL_NAME");
        if (name < 0 || model < 0) return [];
        // FATO (GAMEDATAINIT): INI_WEAPON1..3 são as armas iniciais; a primeira fica equipada.
        var weapons = new[] { "INI_WEAPON1", "INI_WEAPON2", "INI_WEAPON3" }.Select(w => Array.IndexOf(head, w)).ToArray();
        return lines.Skip(1).Select(l => l.Split(';')).Where(c => c.Length > Math.Max(name, model))
            .Select(c => (c[name], c[model], weapons.Select(i => i >= 0 && i < c.Length && int.TryParse(c[i], out var r) ? r : -1).ToArray())).ToList();
    }

    string FormName(int d) => d >= 0 && d < _shinka.Count ? _shinka[d].Name : $"Digimon {d}";
    string FormModel(int d) => d >= 0 && d < _shinka.Count ? _shinka[d].Model : "";

    // ---------- telas ----------

    void Clear()
    {
        foreach (var c in _root.GetChildren()) { _root.RemoveChild(c); c.QueueFree(); }
        // Fora do título: some o logotipo e os menus do jogo; o fundo animado fica.
        if (_art != null)
        {
            if (_art.Has("j_startlogo")) _art.Play("j_startlogo", "SQ_CLOSE");
            _art.Remove("j_startmenu01");
            _art.Remove("j_startmenu02");
        }
        _title = TitleState.None;
        if (_panel != null) _panel.Visible = true;
        // Telas do port sobre o fundo da escolha de personagem do jogo; o Digimon girando só na criação.
        if (_sel != null)
        {
            _art!.Visible = false;
            _sel.Visible = true;
            if (!_sel.Has("j_chrselback")) _sel.Play("j_chrselback", "SQ_OPEN");
            if (!_sel.Has("j_chrselbase")) _sel.Play("j_chrselbase", "SQ_OPEN");
        }
        if (_turntable != null) _turntable.Visible = false;
        _center.AnchorLeft = 0;
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
        if (_art != null)
        {
            _panel!.Visible = false;
            _art.Visible = true;
            if (_sel != null) _sel.Visible = false;
            if (!_art.Has("j_startback")) _art.Play("j_startback", "SQ_OPEN");
            _art.Play("j_startlogo", "SQ_OPEN");
            _art.Play("j_startmenu01", "SQ_OPEN");
            _title = TitleState.Press;
            return;
        }
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
        // FATO (hostLoad.lua): a dificuldade é escolhida a cada começo, até o máximo liberado pelas flags da vaga.
        var ram = new DW4.Script.Be.GameRam();
        c.Apply(ram, 0);
        if (ram.DifficultyMax == 0) { c.Difficulty = 0; Enter(slot, c); return; }
        Clear();
        Title("Select Difficulty", 30);
        Button? first = null;
        for (int d = 0; d <= ram.DifficultyMax; d++)
        {
            int diff = d;
            var b = Btn(_root, Difficulties[d], () => { c.Difficulty = diff; Enter(slot, c); });
            if (d == Math.Min(c.Difficulty, ram.DifficultyMax)) first = b;
        }
        Btn(_root, "Voltar", ShowSlots);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Enter(int slot, CharacterSave c)
    {
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
            var b = Btn(forms, FormName(d) + (ok ? "" : "\n(sem modelo ainda)"), () => { chosen = form; foreach (var p in picks) p.ButtonPressed = p == picks[form]; _turntable?.Show(FormModel(form)); }, ok);
            b.ToggleMode = true;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            picks.Add(b);
            if (ok && chosen < 0) { chosen = d; b.ButtonPressed = true; }
        }
        // Digimon escolhido girando à esquerda; o painel vai para a direita.
        if (_turntable != null && chosen >= 0)
        {
            _turntable.Visible = true;
            _turntable.Show(FormModel(chosen));
            _center.AnchorLeft = 0.42f;
        }
        Title("Nome", 18);
        var name = new LineEdit { MaxLength = NameMax, PlaceholderText = "Nome do seu Digimon", CustomMinimumSize = new Vector2(0, 44) };
        name.AddThemeFontSizeOverride("font_size", 22);
        _root.AddChild(name);
        Title("Dificuldade", 18);
        var diff = new OptionButton { CustomMinimumSize = new Vector2(0, 44) };
        foreach (var t in Difficulties) diff.AddItem(t);
        // FATO (hostLoad.lua): sem as flags 711/77, só Normal. Personagem novo não tem flags.
        diff.SetItemDisabled(1, true);
        diff.SetItemDisabled(2, true);
        _root.AddChild(diff);
        Title("Hard e Very Hard são liberadas pelo progresso do personagem.", 14);
        var status = Title("", 16);
        var buttons = new HBoxContainer();
        _root.AddChild(buttons);
        var create = Btn(buttons, "Criar", () =>
        {
            var n = name.Text.Trim();
            if (n.Length == 0) { status.Text = "Dê um nome ao Digimon."; name.GrabFocus(); return; }
            if (chosen < 0) { status.Text = "Escolha um Digimon."; return; }
            _turntable?.Enjoy();
            var save = CharacterSave.New(n, chosen, FormModel(chosen), diff.Selected, Session.NewGameStage, Session.NewGameEntry, chosen < _shinka.Count ? _shinka[chosen].Weapons : null);
            Session.Store.Save(Session.Account, slot, save);
            // Com o modelo de escolha, deixa o dcngC01 (SQ_ENJOY) tocar antes de voltar às vagas.
            if (_turntable?.Visible == true) { _root.ProcessMode = ProcessModeEnum.Disabled; GetTree().CreateTimer(1.5).Timeout += () => { _root.ProcessMode = ProcessModeEnum.Inherit; ShowSlots(); }; }
            else ShowSlots();
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
        ("Técnica", "1..6 direto; K / botão direito / Y usam a selecionada; roda do mouse / LB-RB trocam"),
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
        if (TitleInput(e)) { GetViewport().SetInputAsHandled(); return; }
        if (_movieOn) return;
        // Cancelar (Esc / B) volta ao título de qualquer tela.
        if (e.IsActionPressed("ui_cancel") && _root != null) { ShowTitle(); GetViewport().SetInputAsHandled(); }
    }
}
