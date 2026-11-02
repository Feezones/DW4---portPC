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
    static readonly string Common = DataPaths.Fatia;
    static bool Playable(string model) => Fatia.HasPlayerModel(Common, model);
    const int NameMax = 12; // DECISÃO DO PORT: o limite do jogo não foi lido

    VBoxContainer _root = null!;
    TitleArt? _art;
    Theme? _gameTheme;
    CenterContainer _center = null!;
    string _stages = "";
    enum TitleState { None, Press, Opening, Menu, Confirm, Cancel }
    TitleState _title;
    int _titleLine;
    bool _movieOn;
    List<(string Name, string Model, int[] Weapons)> _shinka = [];

    public override void _Ready()
    {
        DataPaths.MigrateOldUserData();
        PortSettings.Apply(GetTree().Root);
        // Pacote do launcher: a versão publicada vai para o log (para os relatos de teste); a janela fica só "Digimon World 4".
        if (DataPaths.Version is { Length: > 0 } version) GD.Print($"[versao] {version}; dados em {DataPaths.Fatia}");
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
        var stages = DataPaths.Fases;
        var problems = new List<string>();
        _stages = stages;
        _art = TitleArt.Create(this, stages, -1, problems);
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
            if (BitmapFont.Load(System.IO.Path.Combine(stages, "fonte"))?.ToFontFile("NORMAL") is { } gf) center.Theme = _gameTheme = new Theme { DefaultFont = gf, DefaultFontSize = 26 };
        }
        else center.AddChild(_root);
        ShowTitle();
        // Abertura antes do título, uma vez por execução (a stagIS09 do jogo vem entre os logos e o título; OpeningMovie.cs).
        bool capture = OS.IsDebugBuild() && OS.GetEnvironment("DW4_TELA").Length > 0;
        if (!_openingShown && !capture && OpeningMovie.CanPlay())
        {
            _openingShown = true;
            _root.Visible = false;
            _movieOn = true;
            var movie = new OpeningMovie();
            movie.Finished += () => { _movieOn = false; _root.Visible = true; _music?.Play(); ShowTitle(); };
            AddChild(movie);
        }
        else _music?.Play();
        if (capture) CallDeferred(MethodName.CaptureScreen);
    }

    // DEPURAÇÃO (só no build Debug): DW4_TELA=titulo|vagas|criar|config abre a tela, grava a imagem da janela em
    // D:\dw4-work\previas\menu_<tela>.png depois de 4 s e fecha o port (conferir telas sem ninguém no controle).
    async void CaptureScreen()
    {
        string screen = OS.GetEnvironment("DW4_TELA");
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        switch (screen)
        {
            case "vagas": ShowSlots(); break;
            case "criar": ShowCreate(3); break;
            case "config": ShowSettings(); break;
            case var st when st.StartsWith("status", System.StringComparison.Ordinal): ShowStatusPreview(st.Length > 7 ? st[7..] : ""); break;
        }
        await ToSignal(GetTree().CreateTimer(4.0), SceneTreeTimer.SignalName.Timeout);
        var dir = OS.GetEnvironment("DW4_TELA_PASTA") is { Length: > 0 } d ? d : @"D:\dw4-work\previas";
        System.IO.Directory.CreateDirectory(dir);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(dir, $"menu_{screen.Replace(':', '_')}.png"));
        GetTree().Quit();
    }

    static bool _openingShown;
    PanelContainer? _panel;
    AudioStreamPlayer? _music;

    public override void _Process(double delta)
    {
        FitScreen();
        if (_art == null) return;
        _art.Tick((float)delta);
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
        var stages = DataPaths.Fases;
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
        // Telas de vagas e criação (MainMenu.Slots.cs).
        _screen?.QueueFree();
        (_screen, _screenContent, _bigModel, _detail, _nameEdit, _formLabel, _createStatus) = (null, null, null, null, null, null, null);
        _creating = false;
        _center.Visible = true;
        if (_art != null) _art.Visible = true;
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

    // ---------- vagas ----------
    // As janelas do jogo (j_saveareawin01..04, j_mainstatuswin…) ficaram de fora: layout próprio em MainMenu.Slots.cs.
    GameTables? _slotDb;
    int _slotCursor;
    IReadOnlyList<CharacterSave?> _slots = [];

    // HP e MP máximos da forma atual da vaga (GameTables.Player, o mesmo cálculo do jogo).
    (int Hp, int Mp) SlotStats(CharacterSave c)
    {
        try
        {
            if (_slotDb == null)
            {
                var dir = System.IO.Path.Combine(_stages, Session.NewGameStage, "db");
                var file = System.IO.Directory.Exists(dir) ? System.IO.Directory.EnumerateFiles(dir, "*_db.json").FirstOrDefault() : null;
                if (file == null) return (0, 0);
                _slotDb = GameTables.FromJson(System.IO.File.ReadAllText(file));
            }
            var ram = new DW4.Script.Be.GameRam();
            c.Apply(ram, 0);
            var forms = ram.Players[0].Forms;
            if (forms.Current < 0) return (0, 0);
            var f = forms[forms.Current];
            int weapon = c.WeaponRow >= 0 ? c.WeaponRow : (forms.Current < _shinka.Count ? _shinka[forms.Current].Weapons.FirstOrDefault(w => w >= 0, -1) : -1);
            var eq = ram.Players[0].Equipment ?? new Equipment(Math.Max(0, weapon));
            var p = _slotDb.Player(forms.Current, f.Level, eq, f.Skill, f.UpCount);
            return (p.MaxHp, p.MaxMp);
        }
        catch (Exception e) when (e is KeyNotFoundException or IndexOutOfRangeException or ArgumentException or InvalidOperationException) { return (0, 0); }
    }

    void ShowSlots()
    {
        // As vagas são da conta do launcher: sem ela (exe aberto direto), nenhuma vaga aparece.
        if (Session.Account == null) { Message("Abra o Digimon World 4 pelo launcher RE:DIGITAL, com a sua conta conectada, para ver e criar os seus Digimon.", ShowTitle); return; }
        ShowSlotsUi();
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
        Btn(_root, "Sim, apagar", () => { Session.Store.Delete(Session.Account!, slot); ShowSlots(); });
        no.CallDeferred(Control.MethodName.GrabFocus);
    }

    void Message(string text, Action back)
    {
        Clear();
        Title(text, 22);
        Btn(_root, "OK", back).CallDeferred(Control.MethodName.GrabFocus);
    }

    // Criar: forma inicial (0..3) e nome; a dificuldade de um personagem novo é sempre Normal (FATO: hostLoad.lua, sem as flags
    // 711/77). Layout próprio em MainMenu.Slots.cs.
    void ShowCreate(int slot) => ShowCreateUi(slot);

    // ---------- configurações ----------

    void ShowSettings()
    {
        Clear();
        _root.AddChild(new SettingsPanel(ShowTitle));
    }

    public override void _Input(InputEvent e) => DeviceIcons.Seen(e);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsInsideTree()) return;
        // A escolha pode trocar de cena (entrar no jogo): depois disso o menu já saiu da árvore e não tem viewport.
        if (TitleInput(e) || CreateUiInput(e)) { if (IsInsideTree()) GetViewport().SetInputAsHandled(); return; }
        if (_movieOn) return;
        // Cancelar (Esc / B) volta ao título de qualquer tela.
        if (e.IsActionPressed("ui_cancel") && _root != null) { ShowTitle(); GetViewport().SetInputAsHandled(); }
    }
}
