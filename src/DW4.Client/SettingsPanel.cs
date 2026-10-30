using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Tela de Configurações do port (abas Gráfico, Som e Controles), usada no menu inicial ("Option") e no menu de pausa do jogo.
/// "Aplicar" e "Voltar" gravam em user://config.cfg e aplicam na hora (janela, áudio, mapa de controles); <c>applied</c> deixa
/// quem abriu aplicar o resto (no jogo, a câmera da dungeon).
/// </summary>
public partial class SettingsPanel : VBoxContainer
{
    readonly Action _onBack;
    readonly Action? _applied;

    public SettingsPanel(Action onBack, Action? applied = null)
    {
        _onBack = onBack;
        _applied = applied;
        CustomMinimumSize = new Vector2(720, 0);
        AddThemeConstantOverride("separation", 14);
        Build();
    }

    public SettingsPanel() : this(() => { }) { }

    void Apply()
    {
        PortSettings.Save();
        PortSettings.Apply(GetTree().Root);
        _applied?.Invoke();
    }

    static Button Btn(Container parent, string text, Action click)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 48) };
        b.AddThemeFontSizeOverride("font_size", 22);
        b.Pressed += click;
        parent.AddChild(b);
        return b;
    }

    // Configurações do port em três abas (DECISÃO DO PORT, docs/decisoes-port-pc.md), gravadas em user://config.cfg.
    void Build()
    {
        var heading = new Label { Text = "Configurações", HorizontalAlignment = HorizontalAlignment.Center };
        heading.AddThemeFontSizeOverride("font_size", 40);
        AddChild(heading);
        var c = PortSettings.Current;
        var tabs = new TabContainer { CustomMinimumSize = new Vector2(0, 430) };
        AddChild(tabs);

        var video = Page(tabs, "Gráfico");
        var res = PortConfig.Resolutions.ToList();
        int cur = res.FindIndex(r => r.W == c.Width && r.H == c.Height);
        if (cur < 0) { res.Insert(0, (c.Width, c.Height)); cur = 0; }
        Choice(video, "Resolução (janela)", res.Select(r => $"{r.W} × {r.H}").ToArray(), cur, v => (c.Width, c.Height) = res[v]);
        Choice(video, "Modo", ["Janela", "Tela cheia", "Sem borda"], c.WindowMode, v => { c.WindowMode = v; c.Fullscreen = v == 1; });
        Check(video, "VSync", c.VSync, v => c.VSync = v);
        var fps = PortConfig.FpsLimits;
        Choice(video, "Limite de FPS", fps.Select(f => f == 0 ? "Sem limite" : $"{f}").ToArray(), Math.Max(0, Array.IndexOf(fps, c.FpsLimit)), v => c.FpsLimit = fps[v]);
        Slider(video, "Escala da interface", 0.5f, 2f, 0.05f, c.UiScale, v => c.UiScale = v);
        Choice(video, "Anti-aliasing", ["Desligado", "FXAA", "MSAA 2x", "MSAA 4x", "MSAA 8x", "TAA"], c.AntiAliasing, v => c.AntiAliasing = v);
        Choice(video, "Sombras", ["Original (sem sombra)", "Baixa", "Média", "Alta"], c.Shadows, v => c.Shadows = v);
        Slider(video, "Campo de visão (graus)", 35, 75, 1, c.Fov, v => c.Fov = (int)v);
        Slider(video, "Resolução interna (FSR abaixo de 1)", 0.5f, 2f, 0.05f, c.RenderScale, v => c.RenderScale = v);
        Choice(video, "Acelerar cenas (segurando)", ["2x", "3x", "4x"], c.FastForward - 2, v => c.FastForward = v + 2);
        // As duas opções da tela de opções do jogo (mswkMem0010..0015 → CAMERAMODE e LIGHTMODE da GAMERAM).
        Choice(video, "Luz", ["Suave", "Forte"], c.LightMode, v => c.LightMode = v);
        Choice(video, "Câmera da dungeon", ["Perto", "Longe"], c.CameraMode, v => c.CameraMode = v);

        var sound = Page(tabs, "Som");
        Slider(sound, "Volume geral", 0, 1, 0.05f, c.MasterVolume, v => c.MasterVolume = v);
        Slider(sound, "Música", 0, 1, 0.05f, c.MusicVolume, v => c.MusicVolume = v);
        Slider(sound, "Efeitos", 0, 1, 0.05f, c.EffectsVolume, v => c.EffectsVolume = v);
        Slider(sound, "Vozes", 0, 1, 0.05f, c.VoiceVolume, v => c.VoiceVolume = v);

        var controls = Page(tabs, "Controles");
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 340), SizeFlagsVertical = SizeFlags.ExpandFill };
        controls.AddChild(scroll);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(list);
        _bindStatus = new Label { Text = "Clique numa ação e aperte a nova tecla ou botão (Esc cancela).", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        controls.AddChild(_bindStatus);
        Check(controls, "Vibração do controle", c.Vibration, v => c.Vibration = v);
        _bindButtons.Clear();
        foreach (var (device, title) in new[] { (InputBindings.Keyboard, "Teclado e mouse"), (InputBindings.Pad, "Controle") })
        {
            var head = new HBoxContainer();
            var label = new Label { Text = title, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            label.AddThemeFontSizeOverride("font_size", 22);
            head.AddChild(label);
            var restore = new Button { Text = "Restaurar padrão" };
            string dev = device;
            restore.Pressed += () => { c.Bindings.RestoreDefaults(dev); PortInput.Apply(c.Bindings); RefreshBindings(); _bindStatus!.Text = $"{title}: padrão restaurado."; };
            head.AddChild(restore);
            list.AddChild(head);
            foreach (var a in InputBindings.Actions)
            {
                var row = new HBoxContainer();
                row.AddChild(new Label { Text = a.Label, CustomMinimumSize = new Vector2(300, 0) });
                var chips = new HBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
                chips.AddThemeConstantOverride("separation", 6);
                row.AddChild(chips);
                _bindChips[(dev, a.Id)] = chips;
                var b = new Button { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                var action = a;
                b.Pressed += () =>
                {
                    _capture = (dev, action.Id, action.Label);
                    _bindStatus!.Text = $"Aperte {(dev == InputBindings.Pad ? "um botão do controle" : "uma tecla ou botão do mouse")} para \"{action.Label}\" (Esc cancela).";
                    b.Text = "...";
                };
                row.AddChild(b);
                list.AddChild(row);
                _bindButtons[(dev, a.Id)] = b;
            }
        }
        RefreshBindings();

        var buttons = new HBoxContainer();
        AddChild(buttons);
        var apply = Btn(buttons, "Aplicar", () => { Apply(); });
        apply.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var back = Btn(buttons, "Voltar", () => { _capture = null; Apply(); _onBack(); });
        back.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tabs.GetTabBar().CallDeferred(Control.MethodName.GrabFocus);
    }

    // Troca de controles: a ação esperando a nova tecla/botão, os botões de cada ligação e a linha de aviso.
    (string Device, string Action, string Label)? _capture;
    readonly Dictionary<(string, string), Button> _bindButtons = [];
    readonly Dictionary<(string, string), HBoxContainer> _bindChips = [];
    ButtonNames.Kind _shownPad = ButtonNames.Kind.Xbox;
    Label? _bindStatus;

    void RefreshBindings()
    {
        var b = PortSettings.Current.Bindings;
        foreach (var ((device, action), button) in _bindButtons)
        {
            if (!IsInstanceValid(button)) continue;
            var list = b.Get(device, action);
            var kind = device == InputBindings.Pad ? DeviceIcons.PadKind : ButtonNames.Kind.Keyboard;
            button.Text = list.Count == 0 ? "(nenhuma)  trocar" : "trocar";
            // Ícones desenhados pelo port, com os nomes do aparelho (Xbox, PlayStation, Nintendo…) para o controle.
            if (_bindChips.TryGetValue((device, action), out var chips) && IsInstanceValid(chips))
            {
                foreach (var old in chips.GetChildren()) old.QueueFree();
                foreach (var binding in list) chips.AddChild(DeviceIcons.Chip(binding, kind));
            }
        }
    }

    // Antes da interface: com uma ação esperando, a próxima tecla/botão vira a ligação dela (avisando de quem foi tirada).
    public override void _Input(InputEvent e)
    {
        DeviceIcons.Seen(e);
        if (DeviceIcons.PadKind != _shownPad) { _shownPad = DeviceIcons.PadKind; RefreshBindings(); } // outro controle: troca os ícones
        if (_capture is not { } cap) return;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            _capture = null;
            RefreshBindings();
            _bindStatus!.Text = "Troca cancelada.";
            GetViewport().SetInputAsHandled();
            return;
        }
        if (PortInput.FromEvent(e, cap.Device) is not { } binding)
        {
            if (e is InputEventKey or InputEventMouseButton or InputEventJoypadButton) GetViewport().SetInputAsHandled();
            return;
        }
        var bindings = PortSettings.Current.Bindings;
        var taken = bindings.Set(cap.Device, cap.Action, binding);
        PortInput.Apply(bindings);
        _capture = null;
        RefreshBindings();
        string names = string.Join(", ", taken.Select(t => InputBindings.Actions.First(a => a.Id == t).Label));
        _bindStatus!.Text = $"\"{cap.Label}\": {InputBindings.Describe(binding)}" + (taken.Count > 0 ? $". Conflito: tirada de \"{names}\"." : ".");
        GetViewport().SetInputAsHandled();
    }

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

}
