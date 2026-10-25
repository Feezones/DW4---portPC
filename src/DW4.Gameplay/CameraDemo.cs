using System.Numerics;
using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>
/// Câmera de demo (beCameraCtrlInfo → beCameraDemoData, ex.: "entry" do acmr_pack, ndmw #182), amostrada a 60 Hz por
/// <c>dw4 igb-camera-demo</c>. FATO: duração, fov horizontal e o caminho de igTransform. HIPÓTESE: entre os quadros gravados,
/// interpolação linear; a demo para no último quadro (ISDEMOCAMERA passa a valer 1, ver docs/gameplay/fase4-objetos-dng1.md).
/// </summary>
public sealed class CameraDemo
{
    public sealed record Frame(float Time, Vector3 Position, Vector3 Forward, Vector3 Up);

    public required string Name { get; init; }
    public required float Duration { get; init; }
    public required float FovHorizontalDeg { get; init; }
    public required IReadOnlyList<Frame> Frames { get; init; }

    public static CameraDemo FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        static Vector3 V(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        var frames = r.GetProperty("frames").EnumerateArray()
            .Select(f => new Frame(f.GetProperty("t").GetSingle(), V(f.GetProperty("position")), V(f.GetProperty("forward")), V(f.GetProperty("up"))))
            .ToList();
        if (frames.Count == 0) throw new InvalidDataException("demo de câmera sem quadros");
        return new CameraDemo
        {
            Name = r.GetProperty("name").GetString() ?? "",
            Duration = r.GetProperty("duration").GetSingle(),
            FovHorizontalDeg = r.GetProperty("fovHorizontalDeg").GetSingle(),
            Frames = frames,
        };
    }

    /// <summary>Pose no tempo <paramref name="t"/> (segundos desde o início; presa ao primeiro e ao último quadro).</summary>
    public Frame Sample(float t)
    {
        if (t <= Frames[0].Time) return Frames[0];
        if (t >= Frames[^1].Time) return Frames[^1];
        int hi = 1;
        while (Frames[hi].Time < t) hi++;
        var a = Frames[hi - 1];
        var b = Frames[hi];
        float k = (t - a.Time) / (b.Time - a.Time);
        return new Frame(t, Vector3.Lerp(a.Position, b.Position, k), Vector3.Normalize(Vector3.Lerp(a.Forward, b.Forward, k)),
            Vector3.Normalize(Vector3.Lerp(a.Up, b.Up, k)));
    }
}
