using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Chão de veículo (barco da stag0103): o objeto traz a própria colisão (beHitLandModelInfo) e o jogador anda com ele.
/// FATO (exportação da stag0103 e stag0203): o tipo @10 do beHitLandModelInfo é 0x402 nos objetos quebráveis (g_box, g_rock…),
/// 0x401 no g_drailscaffold, 0x1405 no g_boatz01 e 0x1403 no g_soukouz02, os dois que andam presos a um trilho r_* (MOVERL).
/// A colisão do g_boatz01 é uma caixa aberta 34 × 100 × 46 (chão em y 0, quatro paredes viradas para dentro).
/// HIPÓTESE: o bit 0x1000 marca "veículo": quem está dentro da caixa vai junto quando o objeto anda.
/// </summary>
public static class RideLand
{
    public const int VehicleBit = 0x1000;

    public static bool IsVehicle(int kind) => (kind & VehicleBit) != 0;

    /// <summary>
    /// Chão parado do objeto (tipo baixo 1, como o "piso" da colisão da fase; 0x401 no g_dskyscaffold e no g_drailscaffold).
    /// FATO (dng103f2): os andaimes g_dskyscaffold sobre o buraco do matt05 só têm chão no beHitLandModelInfo (a colisão da fase
    /// traz as quatro paredes dos pilares, sem tampa). HIPÓTESE: o jogador fica em cima de qualquer objeto com esse tipo.
    /// Os quebráveis (0x402) já têm corpo próprio (SpawnBox/MakeHittable) e ficam de fora.
    /// </summary>
    public static bool IsFloor(int kind) => !IsVehicle(kind) && (kind & 0xFF) == 1;

    /// <summary>Ponto (no espaço do veículo) dentro da caixa da colisão, com folga nas laterais e abaixo do chão.</summary>
    public static bool Inside(Vector3 local, Vector3 min, Vector3 max, float margin = 2f) =>
        local.X >= min.X - margin && local.X <= max.X + margin
        && local.Z >= min.Z - margin && local.Z <= max.Z + margin
        && local.Y >= min.Y - 10f && local.Y <= max.Y;
}
