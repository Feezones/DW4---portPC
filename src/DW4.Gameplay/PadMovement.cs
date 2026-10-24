using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Movimento pelo direcional no plano XZ, em passo fixo.
/// PROVISÓRIO: o jogo guarda <c>beModelCtrlPDMOVE</c> "Move" 0,7 / 6 e "Stop" 1 / 4 no Guilmon, mas o papel e a unidade
/// desses números ainda não foram medidos (ver docs/gameplay/fase3-levantamento.md). Os valores padrão abaixo são
/// marcadores para a fatia andar; nada aqui é declarado fiel até a comparação com o jogo.
/// </summary>
public sealed class PadMovement
{
    /// <summary>Velocidade máxima em unidades por segundo (provisória).</summary>
    public float MaxSpeed { get; set; } = 60f;
    /// <summary>Aceleração em unidades por segundo² ao segurar o direcional (provisória).</summary>
    public float Acceleration { get; set; } = 600f;
    /// <summary>Desaceleração em unidades por segundo² ao soltar (provisória).</summary>
    public float Deceleration { get; set; } = 900f;

    public Vector2 Velocity { get; private set; }
    /// <summary>Direção para onde o personagem olha, em radianos em torno de Y (0 = +Z).</summary>
    public float Facing { get; private set; }

    /// <summary>Avança um passo. <paramref name="direction"/> já está no mundo (XZ), com módulo de 0 a 1.</summary>
    public Vector2 Step(Vector2 direction, float dt)
    {
        float amount = Math.Min(direction.Length(), 1f);
        var target = amount > 0 ? Vector2.Normalize(direction) * (MaxSpeed * amount) : Vector2.Zero;
        float rate = amount > 0 ? Acceleration : Deceleration;
        var delta = target - Velocity;
        float step = rate * dt;
        Velocity = delta.Length() <= step ? target : Velocity + Vector2.Normalize(delta) * step;
        if (amount > 0) Facing = MathF.Atan2(direction.X, direction.Y);
        return Velocity;
    }

    public void Stop() => Velocity = Vector2.Zero;
}
