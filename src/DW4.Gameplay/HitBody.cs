using System.Numerics;
using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>
/// Caixas do corpo de um ator e o teste das caixas de golpe contra elas.
/// FATO (beModelCtrlInfoDataHit "HIT" → igAABoxList, exportado pelo dw4 igb-seq em "body"): cada ator tem caixas fixas no espaço
/// dele, sem osso (Guilmon −2,5..2,5 × 0..7 × −2,5..2,5; Numemon −4..4 × 0..14 × −4..4). As caixas de golpe (HITBOX "Set") também
/// são fixas no ator de quem bate, com quadro de início e duração; o "movimento" do golpe vem de várias caixas em quadros seguidos
/// (o arco do SQ_ATCK1 do Guilmon: x −9 → 9 nos quadros 10..14). HIPÓTESE: o teste é de caixa contra caixa, com a caixa de golpe
/// girada para os eixos do alvo (o código nativo do teste não foi lido).
/// </summary>
public static class HitBody
{
    /// <summary>Caixas do corpo do json de sequências (vazio se o exportador não achou).</summary>
    public static List<(Vector3 Min, Vector3 Max)> Load(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<(Vector3, Vector3)>();
        if (!doc.RootElement.TryGetProperty("body", out var body)) return list;
        foreach (var b in body.EnumerateArray())
        {
            var f = b.EnumerateArray().Select(x => x.GetSingle()).ToArray();
            if (f.Length >= 6) list.Add((new Vector3(f[0], f[1], f[2]), new Vector3(f[3], f[4], f[5])));
        }
        return list;
    }

    /// <summary>Gira em torno de y como a Godot (Vector3.Rotated(Up, a)): x' = x·cos a + z·sen a, z' = −x·sen a + z·cos a.</summary>
    public static Vector3 RotateY(Vector3 v, float a) =>
        new(v.X * MathF.Cos(a) + v.Z * MathF.Sin(a), v.Y, -v.X * MathF.Sin(a) + v.Z * MathF.Cos(a));

    /// <summary>
    /// A caixa de golpe (centro no mundo, tamanho nos eixos de quem bate, que olha para <paramref name="attackerYaw"/>) toca alguma
    /// caixa do corpo do alvo (posição, giro e escala do alvo)?
    /// </summary>
    public static bool Hits(Vector3 hitCenter, Vector3 hitSize, float attackerYaw, Vector3 targetPos, float targetYaw, float targetScale,
        IReadOnlyList<(Vector3 Min, Vector3 Max)> body)
    {
        if (body.Count == 0 || targetScale <= 0) return false;
        var local = RotateY(hitCenter - targetPos, -targetYaw) / targetScale;
        float d = attackerYaw - targetYaw, c = MathF.Abs(MathF.Cos(d)), s = MathF.Abs(MathF.Sin(d));
        var half = new Vector3(c * hitSize.X + s * hitSize.Z, hitSize.Y, s * hitSize.X + c * hitSize.Z) / 2 / targetScale;
        foreach (var (min, max) in body)
            if (local.X + half.X >= min.X && local.X - half.X <= max.X
                && local.Y + half.Y >= min.Y && local.Y - half.Y <= max.Y
                && local.Z + half.Z >= min.Z && local.Z - half.Z <= max.Z)
                return true;
        return false;
    }
}
