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
        if (_sel != null) { _sel.Visible = false; _sel.Font = BitmapFont.Load(System.IO.Path.Combine(stages, "fonte")); }
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
        // Fora das vagas: somem as janelas e os textos delas.
        if (_slotsArt && _sel != null)
        {
            foreach (var a in new[] { "j_saveareawin01", "j_saveareawin02", "j_saveareawin03", "j_saveareawin04", "j_saveareacursorr", "j_mainstatuswin", "j_digifacewin" }) _sel.Remove(a);
            _sel.ClearTexts();
        }
        _slotsArt = false;
        if (_createArt || _sel?.Has("j_namewin01") == true)
            foreach (var a in new[] { "j_namewin01", "j_linewin01", "j_digiselcursorleft", "j_digiselcursorright" }) _sel?.Remove(a);
        _createArt = false;
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

    // ---------- vagas com as janelas do jogo ----------
    // FATO (main.dol, 0x80353494..0x80353548 e 0x8035593C..): o controle das vagas gera j_digifacewin, j_mainstatuswin,
    // j_saveareawin01..04 e o cursor j_saveareacursorr todos na mesma posição-base (a do próprio controle), então o arranjo entre
    // eles vem das malhas; a prévia (dw4 hud-previa) mostra o quadro de status com as 4 vagas embaixo, no canto de baixo à direita.
    // Rosto: SQ_<forma>OPEN (tabela de 16 formas em 0x804EA5A8); vaga vazia: SQ_NOOPEN. Cursor: SQ_LINESL00..03.
    // HIPÓTESE: a posição-base é a origem da tela; as linhas de texto (nome, forma, nível) foram postas nos campos da arte, as caixas
    // CHRSELFONT08 do pacote não trazem posição. DECISÃO DO PORT: cima/baixo/esquerda/direita andam na grade 2 × 2, confirmar entra
    // (ou cria), Delete/X apaga, Esc volta ao título.
    static readonly Vector2[] SlotText = [new(28, -103), new(180, -103), new(28, -137), new(180, -137)];
    bool _slotsArt;
    int _slotCursor;
    IReadOnlyList<CharacterSave?> _slots = [];

    static string Face(string model) => model.StartsWith("p_", StringComparison.Ordinal) ? model[2..] : model;

    bool ShowSlotsArt()
    {
        if (_sel?.Font == null) return false;
        Clear();
        _panel!.Visible = false;
        try { _slots = Session.Store.List(Session.Account); }
        catch (Exception e) when (e is IOException or ArgumentException) { GD.PrintErr($"[menu] vagas: {e.Message}"); _slots = new CharacterSave?[ICharacterStore.Slots]; }
        for (int i = 0; i < 4; i++)
            _sel.Play($"j_saveareawin0{i + 1}", i < _slots.Count && _slots[i] is { } c ? $"SQ_{Face(c.Model)}OPEN" : "SQ_NOOPEN");
        _slotsArt = true;
        _slotCursor = Math.Clamp(_slotCursor, 0, 3);
        DrawSlotsArt();
        return true;
    }

    void DrawSlotsArt()
    {
        var sel = _sel!;
        sel.ClearTexts();
        var c = _slotCursor < _slots.Count ? _slots[_slotCursor] : null;
        sel.Play("j_saveareacursorr", $"SQ_LINESL0{_slotCursor}");
        sel.Play("j_mainstatuswin", c != null ? "SQ_WAIT" : "SQ_NOWAIT");
        sel.Play("j_digifacewin", c != null ? $"SQ_{Face(c.Model)}OPEN" : "SQ_nofaceOPEN");
        for (int i = 0; i < 4; i++)
        {
            var s = i < _slots.Count ? _slots[i] : null;
            sel.Text($"vaga{i}", s != null ? s.Name : "Criar novo Digimon", SlotText[i].X, SlotText[i].Y, i == _slotCursor ? new Color(1f, 0.95f, 0.45f) : null);
        }
        sel.Text("conta", $"conta: {Session.Account}", -300, 220);
        sel.Text("ajuda", "setas escolhem, confirmar entra, Delete apaga, Esc volta", -300, -214);
        if (c == null) return;
        // Campos do j_mainstatuswin (HIPÓTESE pela arte): nível depois de "Lv.", nome e forma na linha de baixo.
        sel.Text("lv", $"{c.Level}", 46, -47);
        sel.Text("nome", c.Name, 22, -70);
        sel.Text("forma", FormName(c.CurrentForm), 130, -70);
        sel.Text("fase", c.Stage, 100, -29);
    }

    bool SlotsInput(InputEvent e)
    {
        if (!_slotsArt) return false;
        int move = e.IsActionPressed("ui_right") ? 1 : e.IsActionPressed("ui_left") ? -1 : e.IsActionPressed("ui_down") ? 2 : e.IsActionPressed("ui_up") ? -2 : 0;
        if (move != 0)
        {
            int to = _slotCursor + move;
            if (to is >= 0 and < 4 && (Math.Abs(move) == 2 || to / 2 == _slotCursor / 2)) { _slotCursor = to; DrawSlotsArt(); }
            return true;
        }
        var c = _slotCursor < _slots.Count ? _slots[_slotCursor] : null;
        if (e.IsActionPressed("ui_accept") || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Start })
        {
            _sel!.Play("j_saveareacursorr", $"SQ_LINEON0{_slotCursor}");
            int slot = _slotCursor;
            if (c != null) Play(slot, c); else ShowCreate(slot);
            return true;
        }
        if (c != null && (e is InputEventKey { Pressed: true, Keycode: Key.Delete } || e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.X }))
        {
            ConfirmDelete(_slotCursor, c);
            return true;
        }
        if (e.IsActionPressed("ui_cancel")) { ShowTitle(); return true; }
        return false;
    }

    void ShowSlots()
    {
        if (ShowSlotsArt()) return;
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
    // ---------- criação com as janelas do jogo ----------
    // FATO (main.dol 0x8035D6E0.., 0x803574E0, 0x8035D938..0x8035DA1C): o controle da criação gera j_digiselcursorleft/right e
    // j_namewin01 na mesma posição-base das vagas, e o h_<forma> por MODELCTRL GENERATE + MOVETRANS (0, 0, 0)/(0, 0, 0) + SQ_ENTR.
    // DECISÃO DO PORT: esquerda/direita trocam a forma (as setas piscam com SQ_ON00), o nome é digitado no teclado dentro do
    // j_namewin01 (o jogo usa a grade de letras j_namecursor*), Enter cria, Esc volta às vagas. A dificuldade de um personagem
    // novo é sempre Normal (FATO: hostLoad.lua, sem as flags 711/77).
    bool _createArt;
    int _createSlot, _createForm;
    string _createName = "";

    bool ShowCreateArt(int slot)
    {
        if (_sel?.Font == null) return false;
        Clear();
        _panel!.Visible = false;
        (_createArt, _createSlot, _createName) = (true, slot, "");
        _createForm = Enumerable.Range(0, 4).FirstOrDefault(d => Playable(FormModel(d)), 0);
        _sel.Play("j_namewin01", "SQ_OPEN");
        _sel.Play("j_linewin01", "SQ_LINESL00");
        _sel.Play("j_digiselcursorleft", "SQ_OPEN");
        _sel.Play("j_digiselcursorright", "SQ_OPEN");
        if (_turntable != null) _turntable.Visible = true;
        DrawCreateArt("");
        return true;
    }

    void DrawCreateArt(string status)
    {
        var sel = _sel!;
        sel.ClearTexts();
        bool ok = Playable(FormModel(_createForm));
        _turntable?.Show(FormModel(_createForm));
        sel.Text("titulo", "Criar Digimon", -300, 220);
        sel.Text("nome", _createName.Length > 0 ? _createName + "_" : "Digite o nome_", 75, -27, _createName.Length > 0 ? null : new Color(0.6f, 0.6f, 0.6f));
        sel.Text("forma", $"< {FormName(_createForm)} >" + (ok ? "" : " (sem modelo ainda)"), 20, -97);
        sel.Text("ajuda", "esquerda/direita: Digimon   teclado: nome   Enter: criar   Esc: voltar", -300, -214);
        if (status.Length > 0) sel.Text("aviso", status, -300, -190, new Color(1f, 0.6f, 0.5f));
    }

    bool CreateInput(InputEvent e)
    {
        if (!_createArt) return false;
        if (e is not InputEventKey { Pressed: true } key)
        {
            if (e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B }) { ShowSlots(); return true; }
            return false;
        }
        switch (key.Keycode)
        {
            case Key.Left or Key.Right:
                int dir = key.Keycode == Key.Right ? 1 : -1;
                _createForm = (_createForm + 4 + dir) % 4;
                _sel!.Play(dir < 0 ? "j_digiselcursorleft" : "j_digiselcursorright", "SQ_ON00");
                DrawCreateArt("");
                return true;
            case Key.Backspace:
                if (_createName.Length > 0) _createName = _createName[..^1];
                DrawCreateArt("");
                return true;
            case Key.Escape:
                ShowSlots();
                return true;
            case Key.Enter or Key.KpEnter:
                var n = _createName.Trim();
                if (n.Length == 0) { DrawCreateArt("Dê um nome ao Digimon."); return true; }
                if (!Playable(FormModel(_createForm))) { DrawCreateArt("Esse Digimon ainda não tem modelo no cliente."); return true; }
                var save = CharacterSave.New(n, _createForm, FormModel(_createForm), 0, Session.NewGameStage, Session.NewGameEntry, _createForm < _shinka.Count ? _shinka[_createForm].Weapons : null);
                Session.Store.Save(Session.Account, _createSlot, save);
                _sel!.Play("j_linewin01", "SQ_LINEON00");
                _turntable?.Enjoy();
                _createArt = false;
                GetTree().CreateTimer(1.5).Timeout += ShowSlots;
                return true;
        }
        // Letras, números e espaço pelo teclado (até NameMax caracteres).
        if (key.Unicode >= 32 && key.Unicode != 127 && _createName.Length < NameMax)
        {
            _createName += char.ConvertFromUtf32((int)key.Unicode);
            DrawCreateArt("");
        }
        return true;
    }

    void ShowCreate(int slot)
    {
        if (ShowCreateArt(slot)) return;
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
        _root.AddChild(new SettingsPanel(ShowTitle));
    }

    public override void _Input(InputEvent e) => DeviceIcons.Seen(e);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!IsInsideTree()) return;
        // A escolha pode trocar de cena (entrar no jogo): depois disso o menu já saiu da árvore e não tem viewport.
        if (TitleInput(e) || SlotsInput(e) || CreateInput(e)) { if (IsInsideTree()) GetViewport().SetInputAsHandled(); return; }
        if (_movieOn) return;
        // Cancelar (Esc / B) volta ao título de qualquer tela.
        if (e.IsActionPressed("ui_cancel") && _root != null) { ShowTitle(); GetViewport().SetInputAsHandled(); }
    }
}
