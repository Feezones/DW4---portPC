using System;
using System.IO;
using Godot;

namespace DW4.Client;

/// <summary>
/// Final do jogo. FATO:
///  · o hit_f_devl3.lua (último chefe, stagbs06) liga as flags 70, 86 e 610, e a saída matz01 da stagbs06 leva à stagIS20;
///  · a stagIS20 é a tela de créditos: o first.lua toca "STAFFROL" (ndmw #63), abre j_endback e j_endroll, espera o j_endroll
///    fechar e manda NEXTSTAGE "stagML11" com SETGENERATEPOS I&lt;0&gt; (o jogo continua depois do final).
/// DECISÃO DO PORT: a stagIS20 não tem cenário e os créditos (j_endroll) não foram exportados; o port mostra uma tela de fim
/// com a música dos créditos, que dá para pular depois de 5 s, e então segue para o lobby, entrada 0, como o jogo.
/// </summary>
public partial class Fatia
{
    const string EndingStage = "stagIS20";
    CanvasLayer? _ending;
    AudioStreamPlayer? _endingMusic;
    float _endingTime;

    bool StartEnding(string stage)
    {
        if (!stage.Equals(EndingStage, StringComparison.OrdinalIgnoreCase) || _ending != null) return false;
        ScriptLog("stagIS20: créditos (tela do port)");
        _padEnabled = false;
        _runner?.Stop();
        ClearFade();
        _ending = new CanvasLayer { Layer = 30 };
        var back = new ColorRect { Color = new Color(0, 0, 0), AnchorRight = 1, AnchorBottom = 1 };
        _ending.AddChild(back);
        var text = new Label
        {
            Text = "Digimon World 4\n\nFim\n\n\n(os créditos do jogo, j_endroll, ainda não foram exportados)\n\nEnter pula",
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            AnchorRight = 1, AnchorBottom = 1,
        };
        text.AddThemeFontSizeOverride("font_size", 30);
        _ending.AddChild(text);
        AddChild(_ending);
        foreach (var m in _music.Values) m.Stop();
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        var wav = Path.Combine(stages, "stagis20", "audio", "ndmw_0063.wav");
        if (LoadWav(wav) is { } stream)
        {
            _endingMusic = new AudioStreamPlayer { Stream = stream, Bus = PortSettings.MusicBus };
            _ending.AddChild(_endingMusic);
            _endingMusic.Finished += FinishEnding;
            _endingMusic.Play();
        }
        _endingTime = 0;
        return true;
    }

    void TickEnding(float dt)
    {
        if (_ending == null) return;
        _endingTime += dt;
        bool skip = _endingTime > 5 && (Input.IsKeyPressed(Key.Enter) || Input.IsKeyPressed(Key.Escape) || Input.IsKeyPressed(Key.J) || Input.IsJoyButtonPressed(0, JoyButton.A));
        if (skip || (_endingMusic == null && _endingTime > 10)) FinishEnding();
    }

    void FinishEnding()
    {
        if (_ending == null || _leaving) return;
        _padEnabled = true;
        _nextEntry = 0; // FATO: SETGENERATEPOS I<0> no first.lua da stagIS20
        NextStage(LobbyStage);
    }
}
