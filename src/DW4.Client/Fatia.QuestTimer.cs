using System;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Cronômetro das missões. FATO:
///  · o first.lua das masmorras de missão (dngq*) põe a COUNTER 0 em 1800, 2400 ou 3000 e abre o i_timenm0 com
///    GENERATE "V&lt;-290,217,0&gt;/SQ_OPENDOWN";
///  · o chk_i_timenm0.lua, chamado com o nome "down", tira 1 da COUNTER 0 e escreve minutos (timenm0) e segundos (timenm1); em 0
///    manda SQ_STOP. Com "up", soma 1 até 3599. Com todos os jogadores sem HP, para (SQ_STOP);
///  · o treasure*.lua do lobby paga pela COUNTER 0 que sobrou (≥ 900, 600…).
/// FATO: cada chamada com "down" tira 1 da COUNTER 0 (segundos); g_counter_min e g_counter_sec (que o first.lua da masmorra calcula
/// da COUNTER 0) são os minutos e segundos da tela. HIPÓTESE: o motor chama o chk uma vez por segundo enquanto o i_timenm0 está aberto. DECISÃO DA FATIA: o
/// tempo aparece num rótulo no alto da tela (o ator i_timenm0 não foi exportado).
/// </summary>
public partial class Fatia
{
    string? _timerCall; // "down" ou "up" enquanto o cronômetro anda
    float _timerClock;
    Label? _timerLabel;

    bool DeliverToQuestTimer(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name != "i_timenm0") return false;
        string seq = m.Command == "GENERATE" ? m.RawArgs ?? "" : m.Command;
        if (seq.Contains("DOWN", StringComparison.Ordinal)) _timerCall = "down";
        else if (seq.Contains("UP", StringComparison.Ordinal) && !seq.Contains("STOP", StringComparison.Ordinal)) _timerCall = "up";
        if (m.Command is "SQ_STOP" or "SQ_CLOSE" or "SQ_ERAS") _timerCall = null;
        _timerClock = 0;
        ShowTimer(m.Command is not ("SQ_CLOSE" or "SQ_ERAS"));
        return true;
    }

    void TickQuestTimer(float dt)
    {
        if (_timerLabel != null && _timerLabel.Visible) { int s = Math.Max(0, Ram.Counter(0)); _timerLabel.Text = $"{s / 60:00}:{s % 60:00}"; }
        if (_timerCall == null || _stage == null) return;
        _timerClock += dt;
        if (_timerClock < 1f) return;
        _timerClock -= 1f;
        if (!_stage.Has("chk_i_timenm0.lua")) { _timerCall = null; return; }
        var host = _stage.Host;
        var old = host.CallName;
        host.CallName = _timerCall;
        try { _stage.Run("chk_i_timenm0.lua"); }
        finally { host.CallName = old; }
    }

    void ShowTimer(bool show)
    {
        if (_timerLabel == null)
        {
            var layer = new CanvasLayer { Layer = 6 };
            _timerLabel = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -80, OffsetRight = 80, OffsetTop = 12, HorizontalAlignment = HorizontalAlignment.Center };
            _timerLabel.AddThemeFontSizeOverride("font_size", 34);
            _timerLabel.AddThemeConstantOverride("outline_size", 8);
            _timerLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            layer.AddChild(_timerLabel);
            AddChild(layer);
        }
        _timerLabel.Visible = show;
    }
}
