using System.Numerics;

namespace DW4.Gameplay;

/// <summary>Um volume de câmera: caixa de ativação e matrizes A e B do jogo (em linhas, translação na última linha).</summary>
public sealed record CameraZone(int Index, Vector3 Min, Vector3 Max, Matrix4x4 A, Matrix4x4 B)
{
    public bool Contains(Vector3 p) =>
        p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

    /// <summary>Recuo da câmera ao longo do Z local (translação de A).</summary>
    public float Distance => A.M43;
}

/// <summary>
/// Câmera por volumes da fase (beCameraBoxData, ver docs/gameplay/fase3-levantamento.md).
/// FATO: cada volume tem caixa, A = translação (0, 0, d) e B = rotação (inclinação, às vezes guinada) com translação em Y.
/// HIPÓTESE (a medir no jogo): a cadeia é câmera = A · B · Comum · alvo, com vetores em linha como no Alchemy; o volume 0
/// (na origem) é o padrão quando o alvo não está em nenhum outro; a troca de volume é um corte seco; quando o alvo sai de
/// todos os volumes, o último continua valendo.
/// </summary>
public sealed class CameraZones
{
    readonly List<CameraZone> _zones;
    readonly Matrix4x4 _common;

    public CameraZones(IEnumerable<CameraZone> zones, Matrix4x4 common)
    {
        _zones = zones.OrderBy(z => z.Index).ToList();
        _common = common;
        if (_zones.Count == 0) throw new ArgumentException("nenhum volume de câmera", nameof(zones));
    }

    public IReadOnlyList<CameraZone> Zones => _zones;

    /// <summary>Volume para o alvo: o primeiro (fora o 0) que o contém; senão o atual; senão o 0.</summary>
    public int Select(Vector3 target, int current = -1)
    {
        foreach (var z in _zones)
            if (z.Index != 0 && z.Contains(target)) return z.Index;
        return current >= 0 ? current : _zones[0].Index;
    }

    /// <summary>Posição da câmera e ponto para onde ela olha (o pivô), em coordenadas do mundo.</summary>
    public (Vector3 Position, Vector3 LookAt) Pose(int zone, Vector3 target)
    {
        var z = _zones.First(x => x.Index == zone);
        var toWorld = z.B * _common * Matrix4x4.CreateTranslation(target);
        var pivot = Vector3.Transform(Vector3.Zero, toWorld);
        var position = Vector3.Transform(Vector3.Zero, z.A * toWorld);
        return (position, pivot);
    }

    /// <summary>Lê o JSON gravado por <c>dw4 igb-camera</c> (matrizes com 16 floats em linhas).</summary>
    public static CameraZones FromJson(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        static float[] F(System.Text.Json.JsonElement e) => e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
        static Matrix4x4 M(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        var zones = root.GetProperty("boxes").EnumerateArray().Select(b =>
        {
            var min = F(b.GetProperty("min"));
            var max = F(b.GetProperty("max"));
            return new CameraZone(b.GetProperty("index").GetInt32(), new Vector3(min[0], min[1], min[2]), new Vector3(max[0], max[1], max[2]),
                M(F(b.GetProperty("a"))), M(F(b.GetProperty("b"))));
        });
        var common = root.TryGetProperty("common", out var c) ? M(F(c)) : Matrix4x4.Identity;
        return new CameraZones(zones, common);
    }
}
