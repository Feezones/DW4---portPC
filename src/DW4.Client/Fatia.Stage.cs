using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace DW4.Client;

/// <summary>
/// Fase carregada pelo nome (Fase 5). Pastas fora do repositório:
///   &lt;fases&gt;\&lt;fase&gt;   gerada por dw4 fase-exportar &lt;ndmw.afs&gt; &lt;area.afs&gt; &lt;fase&gt; &lt;fases&gt; (com fase.json);
///   &lt;comum&gt;          o que não é da fase: Guilmon, arma, skillup.json (a pasta da fatia antiga).
/// DW4_FASE escolhe a fase (padrão dng101b1), DW4_FASES a pasta das fases (padrão D:\dw4-work\fases) e DW4_FATIA a comum.
/// Sem &lt;fases&gt;\&lt;fase&gt;, tudo vem da pasta comum, como na fatia antiga.
/// </summary>
public partial class Fatia
{
    string _stageName = "dng101b1";
    string _common = "";
    string[] _cameraFiles = ["ndmw_0180_camera.json", "ndmw_0181_camera.json"];
    string _demoPattern = "camera-demo/ndmw_0182_<nome>.json";
    string _mapBase = "j_map101b1", _mapCursor = "j_mapcur";
    Dictionary<string, int> _musicIndex = new() { ["AM_DUNGE"] = 6, ["DG_CAVE1"] = 35 };

    void ChooseStage()
    {
        _common = OS.GetEnvironment("DW4_FATIA") is { Length: > 0 } d ? d : @"D:\dw4-work\fatia";
        _stageName = OS.GetEnvironment("DW4_FASE") is { Length: > 0 } f ? f : "dng101b1";
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        var dir = Path.Combine(stages, _stageName);
        _dir = Directory.Exists(dir) ? dir : _common;
        if (_dir == _common && _stageName != "dng101b1") _problems.Add($"falta {dir} (dw4 fase-exportar … {_stageName} {stages}): usando a dng1 da fatia");
        var manifest = Path.Combine(_dir, "fase.json");
        if (!File.Exists(manifest)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        var r = doc.RootElement;
        if (r.TryGetProperty("arquivos_camera", out var cams) && cams.ValueKind == JsonValueKind.Object)
        {
            var list = new List<string>();
            foreach (var key in new[] { "camera", "camera_afastada" })
                if (cams.TryGetProperty(key, out var c) && c.ValueKind == JsonValueKind.String) list.Add(c.GetString()!);
            _cameraFiles = [.. list];
            _demoPattern = cams.TryGetProperty("demo", out var dm) && dm.ValueKind == JsonValueKind.String ? dm.GetString()! : "";
        }
        if (r.TryGetProperty("mapa", out var map) && map.ValueKind == JsonValueKind.Object)
        {
            _mapBase = map.TryGetProperty("base", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString()! : "";
            _mapCursor = map.TryGetProperty("cursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "";
        }
        else (_mapBase, _mapCursor) = ("", "");
        if (r.TryGetProperty("musicas", out var mus) && mus.ValueKind == JsonValueKind.Object)
        {
            _musicIndex = [];
            foreach (var m in mus.EnumerateObject()) _musicIndex[m.Name] = m.Value.GetInt32();
        }
    }

    /// <summary>Subpasta da fase; se não existir, a da pasta comum (Guilmon, arma).</summary>
    string StagePath(string sub)
    {
        var here = Path.Combine(_dir, sub);
        return Directory.Exists(here) || File.Exists(here) ? here : Path.Combine(_common, sub);
    }
}
