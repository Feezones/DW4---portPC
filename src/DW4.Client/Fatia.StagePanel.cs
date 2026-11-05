using System;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Painel especial da fase e o medidor dele (fase do barco, stag0103). Levantamento: docs/gameplay/fase-barco.md.
/// FATO (start.lua, first.lua e hit_p_*.lua da stag0103):
///   - MODELCTRL i_stpnlsp04 / i_stpnlsp05 0 GENERATE "V&lt;-296,225,0&gt;" e depois SQ_OPEN: a moldura com o desenho do barco e
///     o medidor "boathp" no alto da tela;
///   - METERCTRL boathp 0 SETPERCENT "F&lt;g_meta&gt;" no começo e a cada golpe no jogador.
/// </summary>
public partial class Fatia
{
    bool DeliverToStagePanel(BeMessage m)
    {
        if (_overlay == null) return false;
        if (m.Class == "MODELCTRL" && m.Name is { } name && name.StartsWith("i_stpnlsp", StringComparison.Ordinal))
        {
            if (m.Command == "GENERATE" && m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } p)
                return _overlay.ShowStagePanel(name, new Vector3((float)p.Numbers[0], (float)p.Numbers[1], (float)p.Numbers[2]));
            // SQ_OPEN / SQ_WAIT: o painel já está aberto desde o GENERATE (DECISÃO DO PORT, ver HudOverlay.ShowStagePanel).
            return _overlay.HasStagePanel(name);
        }
        if (m.Class == "METERCTRL" && m.Command == "SETPERCENT" && m.Name is { } meter && !meter.StartsWith("bshp", StringComparison.Ordinal)
            && _overlay.HasMeter(meter) && m.Arg(0) is { Numbers.Length: > 0 } pc)
        {
            _overlay.SetStageMeter(meter, (float)pc.Numbers[0]);
            return true;
        }
        return false;
    }
}
