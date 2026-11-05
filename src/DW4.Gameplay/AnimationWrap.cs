using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Amostra final que fecha o ciclo das animações Enbaya.
/// FATO (varredura 2026-11-03 dos personagem.gltf de D:\dw4-work\fases, 38 modelos de inimigo): em toda trnsIN (surgir do chão)
/// a última amostra, 1/60 ou 1/30 s depois da penúltima, repete a primeira pose. No Goburimon (e_goburi) o main_Trans_Rotate1 sobe
/// de −3 a 5,7 e na última amostra volta a −3; no Ogremon (e_ogre) volta de 10,85 a −0,11; no Numemon (e_nume) de 0 a −10.
/// Tocada uma vez ("OS"), o Godot para na última amostra e o inimigo fica com a pose de antes de surgir, embaixo da terra, até
/// outra animação entrar; com a SQ_SILENT (ACTION NDSP, sem MOTIONSET) do jogador longe, fica assim de vez (relato do usuário,
/// Ogremon e Goburimon, 2026-11-03).
/// HIPÓTESE: o motor conta quadros inteiros a 30 Hz e a amostra no fim da duração (= quadro 0 do ciclo seguinte) só serve para
/// a repetição ("LP"); o quadro final de uma "OS" é a penúltima amostra.
/// </summary>
public static class AnimationWrap
{
    /// <summary>Distância (unidades do jogo) a partir da qual a volta ao início é um salto e não o fim natural de um ciclo.</summary>
    public const float PositionJump = 1f;

    /// <summary>Ângulo (radianos) a partir do qual a volta ao início é um salto. ~20°.</summary>
    public const float RotationJump = 0.35f;

    /// <summary>A última amostra é a primeira repetida, depois de um salto desde a penúltima.</summary>
    public static bool IsWrapJump(Vector3 first, Vector3 previous, Vector3 last) =>
        Vector3.Distance(first, last) < 1e-3f && Vector3.Distance(previous, last) > PositionJump;

    /// <inheritdoc cref="IsWrapJump(Vector3, Vector3, Vector3)"/>
    public static bool IsWrapJump(Quaternion first, Quaternion previous, Quaternion last) =>
        Angle(first, last) < 1e-3f && Angle(previous, last) > RotationJump;

    /// <summary>A última amostra fecha o ciclo só se vier logo depois da penúltima (meio quadro ou um quadro a 30 Hz).</summary>
    public static bool IsClosingGap(double previousTime, double lastTime) =>
        lastTime - previousTime is > 0 and <= 1.0 / SequenceRunner.FramesPerSecond + 1e-4;

    static float Angle(Quaternion a, Quaternion b)
    {
        float dot = MathF.Min(1f, MathF.Abs(Quaternion.Dot(Quaternion.Normalize(a), Quaternion.Normalize(b))));
        return 2f * MathF.Acos(dot);
    }
}
