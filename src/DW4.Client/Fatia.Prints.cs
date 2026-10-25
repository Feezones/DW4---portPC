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

    void TickPrints(float dt)
    {
        if (_printAt.Count == 0) return;
        _printClock += dt;
        if (_printClock < _printAt.Peek()) return;
        var t = _printAt.Dequeue();
        DirAccess.MakeDirRecursiveAbsolute("user://prints");
        var path = $"user://prints/{_stageName}_{t:0}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[print] {ProjectSettings.GlobalizePath(path)}");
    }
}
