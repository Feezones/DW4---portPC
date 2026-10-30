using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// DEPURAÇÃO (só no build Debug, não é do jogo): DW4_PRINT="5,12,20" grava a tela nesses segundos em user://prints/&lt;fase&gt;_&lt;s&gt;.png,
/// para conferir fases sem ninguém olhando (varredura com --quit-after).
/// </summary>
public partial class Fatia
{
    readonly Queue<float> _printAt = new();
    float _printClock;

    void SetupPrints()
    {
        if (!OS.IsDebugBuild()) return;
        foreach (var t in OS.GetEnvironment("DW4_PRINT").Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : -1).Where(f => f >= 0).OrderBy(f => f))
            _printAt.Enqueue(t);
    }

    // DEPURAÇÃO: DW4_ABRIR=pausa|config|status|banco|xdata|labo|loja:WPU abre essa janela aos 3 s (para o DW4_PRINT fotografar).
    bool _opened;

    void OpenForPrint()
    {
        var what = OS.GetEnvironment("DW4_ABRIR");
        if (what.StartsWith("loja:", StringComparison.Ordinal))
        {
            DeliverToShop(new DW4.Script.Be.BeMessage("SUBWND", null, -1, "GENERATE", [new DW4.Script.Be.BeArg('\0', what[5..], [])], what[5..]));
            return;
        }
        switch (what)
        {
            case "pausa": OpenPause(); break;
            case "config": OpenSettings(); break;
            case "status": OpenMenu(); break;
            case "banco": if (ShopArtReady()) OpenBankArt(); else OpenBank(); break;
            case "xdata": OpenXDataMenu(); break;
            case "labo": OpenEvolve(); break;
        }
    }

    void TickPrints(float dt)
    {
        if (_printAt.Count == 0) return;
        if (!_opened && _printClock >= 3 && OS.GetEnvironment("DW4_ABRIR").Length > 0) { _opened = true; OpenForPrint(); }
        _printClock += dt;
        if (_printClock < _printAt.Peek()) return;
        var t = _printAt.Dequeue();
        DirAccess.MakeDirRecursiveAbsolute("user://prints");
        var path = $"user://prints/{_stageName}_{t:0}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[print] {ProjectSettings.GlobalizePath(path)}");
        if (_printAt.Count == 0 && OS.GetEnvironment("DW4_ABRIR").Length > 0) GetTree().Quit(); // captura de janela: fecha sozinho
    }
}
