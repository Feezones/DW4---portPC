using System.Globalization;
using System.Text;

namespace DW4.Gameplay;

/// <summary>
/// Configurações do port (DECISÃO DO PORT, docs/decisoes-port-pc.md): não existem no jogo original, exceto as duas de jogo que
/// vêm da tela de opções dele (mswkMem0010..0015: "Dungeon camera" Near/Far e "Light" Soft/Hard, que vão para CAMERAMODE e
/// LIGHTMODE da GAMERAM; HIPÓTESE: 0 = a primeira opção). O texto segue o formato do ConfigFile da Godot (seções e chave=valor),
/// para o cliente gravar em user://config.cfg.
/// </summary>
public sealed class PortConfig
{
    // [jogo]
    public int CameraMode { get; set; }
    public int LightMode { get; set; }
    public bool Vibration { get; set; } = true;
    // [grafico]
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public bool Fullscreen { get; set; }
    /// <summary>0 = janela, 1 = tela cheia, 2 = janela sem borda do tamanho da tela.</summary>
    public int WindowMode { get; set; }
    /// <summary>Limite de quadros por segundo (0 = sem limite).</summary>
    public int FpsLimit { get; set; }
    public bool VSync { get; set; } = true;
    /// <summary>Escala da interface (content scale), 0,5 a 2.</summary>
    public float UiScale { get; set; } = 1f;
    // [som], 0 a 1
    public float MasterVolume { get; set; } = 1f;
    public float MusicVolume { get; set; } = 0.8f;
    public float EffectsVolume { get; set; } = 1f;
    public float VoiceVolume { get; set; } = 1f;
    // [teclado] e [controle]
    public InputBindings Bindings { get; private set; } = new();
    public static readonly int[] FpsLimits = [0, 30, 60, 120, 144, 240];
    // [conta]
    /// <summary>Conta local cujas vagas aparecem em "Jogar online" (na Fase 6, a conta do servidor).</summary>
    public string Account { get; set; } = "local";

    public static readonly (int W, int H)[] Resolutions = [(1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160)];

    /// <summary>Corrige valores fora da faixa (arquivo editado à mão).</summary>
    public void Clamp()
    {
        CameraMode = Math.Clamp(CameraMode, 0, 1);
        LightMode = Math.Clamp(LightMode, 0, 1);
        Width = Math.Clamp(Width, 640, 7680);
        Height = Math.Clamp(Height, 360, 4320);
        UiScale = Math.Clamp(UiScale, 0.5f, 2f);
        MasterVolume = Math.Clamp(MasterVolume, 0f, 1f);
        MusicVolume = Math.Clamp(MusicVolume, 0f, 1f);
        EffectsVolume = Math.Clamp(EffectsVolume, 0f, 1f);
        VoiceVolume = Math.Clamp(VoiceVolume, 0f, 1f);
        if (Fullscreen && WindowMode == 0) WindowMode = 1;
        WindowMode = Math.Clamp(WindowMode, 0, 2);
        FpsLimit = Math.Clamp(FpsLimit, 0, 1000);
        Fullscreen = WindowMode == 1;
        if (Account.Length == 0 || Account.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) Account = "local";
    }

    public string ToIni()
    {
        var sb = new StringBuilder();
        void Section(string name) => sb.Append(sb.Length > 0 ? "\n" : "").Append('[').Append(name).Append("]\n\n");
        void Put(string key, object value) => sb.Append(key).Append('=').Append(value switch
        {
            bool b => b ? "true" : "false",
            float f => f.ToString("0.0###", CultureInfo.InvariantCulture),
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture),
        }).Append('\n');
        Section("jogo");
        Put("camera", CameraMode); Put("luz", LightMode); Put("vibracao", Vibration);
        Section("grafico");
        Put("largura", Width); Put("altura", Height); Put("tela_cheia", Fullscreen); Put("modo", WindowMode); Put("vsync", VSync); Put("fps", FpsLimit); Put("escala", UiScale);
        Section("som");
        Put("geral", MasterVolume); Put("musica", MusicVolume); Put("efeitos", EffectsVolume); Put("vozes", VoiceVolume);
        foreach (var device in new[] { InputBindings.Keyboard, InputBindings.Pad })
        {
            Section(device);
            foreach (var (key, value) in Bindings.Save(device)) Put(key, value);
        }
        Section("conta");
        Put("nome", Account);
        return sb.ToString();
    }

    /// <summary>Lê o texto; chaves que faltam ou estão erradas ficam no padrão.</summary>
    public static PortConfig FromIni(string text)
    {
        var c = new PortConfig();
        string section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']') { section = line[1..^1]; continue; }
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
            switch (section, key)
            {
                case ("jogo", "camera"): c.CameraMode = Int(value, c.CameraMode); break;
                case ("jogo", "luz"): c.LightMode = Int(value, c.LightMode); break;
                case ("jogo", "vibracao"): c.Vibration = Bool(value, c.Vibration); break;
                case ("grafico", "largura"): c.Width = Int(value, c.Width); break;
                case ("grafico", "altura"): c.Height = Int(value, c.Height); break;
                case ("grafico", "tela_cheia"): c.Fullscreen = Bool(value, c.Fullscreen); if (c.Fullscreen && c.WindowMode == 0) c.WindowMode = 1; break;
                case ("grafico", "modo"): c.WindowMode = Int(value, c.WindowMode); break;
                case ("grafico", "fps"): c.FpsLimit = Int(value, c.FpsLimit); break;
                case ("som", "vozes"): c.VoiceVolume = Float(value, c.VoiceVolume); break;
                case (InputBindings.Keyboard or InputBindings.Pad, _): c.Bindings.Load(section, key, Text(value) ?? ""); break;
                case ("grafico", "vsync"): c.VSync = Bool(value, c.VSync); break;
                case ("grafico", "escala"): c.UiScale = Float(value, c.UiScale); break;
                case ("som", "geral"): c.MasterVolume = Float(value, c.MasterVolume); break;
                case ("som", "musica"): c.MusicVolume = Float(value, c.MusicVolume); break;
                case ("som", "efeitos"): c.EffectsVolume = Float(value, c.EffectsVolume); break;
                case ("conta", "nome"): c.Account = Text(value) ?? c.Account; break;
            }
        }
        c.Clamp();
        return c;
    }

    static int Int(string v, int fallback) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;
    static float Float(string v, float fallback) => float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;
    static bool Bool(string v, bool fallback) => v == "true" ? true : v == "false" ? false : fallback;
    static string? Text(string v) => v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : null;

    /// <summary>Volume linear (0..1) em dB para o AudioServer; 0 vira silêncio.</summary>
    public static float ToDb(float linear) => linear <= 0.0001f ? -80f : 20f * MathF.Log10(linear);
}
