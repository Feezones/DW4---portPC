using System.IO;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Configurações do port (PortConfig) gravadas em user://config.cfg e aplicadas à janela e ao áudio. DECISÃO DO PORT: o jogo
/// original não tem essa tela; ver docs/decisoes-port-pc.md.
/// </summary>
public static class PortSettings
{
    const string File = "user://config.cfg";
    public const string MusicBus = "Musica", EffectsBus = "Efeitos", VoiceBus = "Vozes";

    static PortConfig? s_current;
    public static PortConfig Current => s_current ??= Load();

    static PortConfig Load()
    {
        var path = ProjectSettings.GlobalizePath(File);
        try { return System.IO.File.Exists(path) ? PortConfig.FromIni(System.IO.File.ReadAllText(path)) : new PortConfig(); }
        catch (IOException) { return new PortConfig(); }
    }

    public static void Save()
    {
        Current.Clamp();
        System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(File), Current.ToIni());
    }

    /// <summary>Janela (modo, tamanho, vsync, limite de FPS, escala da interface), volumes dos barramentos e o mapa de controles.</summary>
    public static void Apply(Window root)
    {
        var c = Current;
        ApplyLanguage(c);
        if (DisplayServer.GetName() == "headless") { PortInput.Apply(c.Bindings); return; } // sem janela: só o mapa de controles
        // DEPURAÇÃO (só no build Debug): DW4_JANELA=1024x768 abre em janela desse tamanho (capturas em outras proporções).
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_JANELA").Split("x") is [var jw, var jh] && int.TryParse(jw, out int dw) && int.TryParse(jh, out int dh))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
            DisplayServer.WindowSetSize(new Vector2I(dw, dh));
        }
        else
        switch (c.WindowMode)
        {
            case 1:
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case 2:
                // Sem borda: janela do tamanho da tela, sem moldura (troca de programa sem mudar o modo de vídeo).
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
                int screen = DisplayServer.WindowGetCurrentScreen();
                DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition(screen));
                DisplayServer.WindowSetSize(DisplayServer.ScreenGetSize(screen));
                break;
            default:
                if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed) DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                DisplayServer.WindowSetSize(new Vector2I(c.Width, c.Height));
                break;
        }
        Engine.MaxFps = c.FpsLimit;
        // Anti-aliasing, resolução interna (FSR abaixo de 100%) e o tamanho do mapa de sombras da luz direcional.
        root.Msaa3D = c.AntiAliasing switch { 2 => Viewport.Msaa.Msaa2X, 3 => Viewport.Msaa.Msaa4X, 4 => Viewport.Msaa.Msaa8X, _ => Viewport.Msaa.Disabled };
        root.ScreenSpaceAA = c.AntiAliasing == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
        root.UseTaa = c.AntiAliasing == 5;
        root.Scaling3DScale = c.RenderScale;
        root.Scaling3DMode = c.RenderScale < 0.999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        RenderingServer.DirectionalShadowAtlasSetSize(c.Shadows switch { 1 => 1024, 2 => 2048, 3 => 4096, _ => 1024 }, true);
        DisplayServer.WindowSetVsyncMode(c.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        root.ContentScaleFactor = c.UiScale;
        AudioServer.SetBusVolumeDb(0, PortConfig.ToDb(c.MasterVolume));
        AudioServer.SetBusVolumeDb(Bus(MusicBus), PortConfig.ToDb(c.MusicVolume));
        AudioServer.SetBusVolumeDb(Bus(EffectsBus), PortConfig.ToDb(c.EffectsVolume));
        AudioServer.SetBusVolumeDb(Bus(VoiceBus), PortConfig.ToDb(c.VoiceVolume));
        PortInput.Apply(c.Bindings);
    }

    static bool s_translationTried;

    /// <summary>
    /// Idioma dos textos do jogo. A tradução vem de &lt;fatia&gt;\idioma\pt-br.json (DW4_FATIA, padrão D:\dw4-work\fatia), fora do
    /// repositório como os textos originais; sem o arquivo, o português mostra o inglês.
    /// </summary>
    public static void ApplyLanguage(PortConfig c)
    {
        GameText.Language = (GameLanguage)c.Language;
        if (GameText.Language == GameLanguage.English || s_translationTried) return;
        s_translationTried = true;
        string fatia = OS.GetEnvironment("DW4_FATIA") is { Length: > 0 } d ? d : @"D:\dw4-work\fatia";
        var file = Path.Combine(fatia, "idioma", "pt-br.json");
        try
        {
            if (!System.IO.File.Exists(file)) GD.PushWarning($"falta {file}: o português mostra o inglês");
            else if (!GameText.Load(System.IO.File.ReadAllText(file))) GD.PushWarning($"{file} não é um JSON válido");
        }
        catch (IOException e) { GD.PushWarning($"{file}: {e.Message}"); }
    }

    /// <summary>Índice do barramento (cria com saída no Master se ainda não existe).</summary>
    public static int Bus(string name)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i >= 0) return i;
        AudioServer.AddBus();
        i = AudioServer.BusCount - 1;
        AudioServer.SetBusName(i, name);
        AudioServer.SetBusSend(i, "Master");
        return i;
    }
}
