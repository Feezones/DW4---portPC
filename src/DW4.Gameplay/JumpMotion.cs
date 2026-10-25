namespace DW4.Gameplay;

/// <summary>
/// Movimento vertical do pulo e da queda pelos MOVE das sequências do jogador.
/// FATO (Guilmon #857, SQ_JUMP de cada estilo): MOVE "Jump" 1,44 6,3 → ACTION JUMPS1 → WCHECK "TurnY" → MOVE "Fall" 1,44 6,3 →
/// ACTION JUMPS2 → WCHECK "Landing" → MOVE "Stop" 1 4 → ACTION JUMPS3. O SQ_FALL é o mesmo sem a parte do "Jump".
/// HIPÓTESE (unidade não medida): "Jump" g v0 e "Fall" g vmáx, em unidades por quadro a 30 Hz. Dá pico de
/// v0² / 2g ≈ 13,8 unidades, que passa o degrau de 10 do pedestal do chefe no stagbs01 (chão y 20, topo y 30).
/// "TurnY" = a velocidade vertical virou (começou a descer); "Landing" = tocou o chão.
/// </summary>
public sealed class JumpMotion
{
    const float Fps = SequenceRunner.FramesPerSecond;

    float _gravity, _maxFall;

    /// <summary>Velocidade vertical em unidades por segundo (positiva = subindo).</summary>
    public float VelocityY { get; private set; }
    /// <summary>Um MOVE "Jump"/"Fall" está valendo (até o "Stop" do pouso).</summary>
    public bool Active { get; private set; }

    /// <summary>Aplica um MOVE da sequência; devolve true se era de pulo ou queda.</summary>
    public bool Apply(SeqCommand move, float currentVelocityY = 0)
    {
        switch (move.S(0))
        {
            case "Jump":
                _gravity = move.F(1) * Fps * Fps;
                _maxFall = move.F(2) * Fps;
                VelocityY = move.F(2) * Fps;
                Active = true;
                return true;
            case "Fall":
                _gravity = move.F(1) * Fps * Fps;
                _maxFall = move.F(2) * Fps;
                if (!Active) VelocityY = Math.Min(currentVelocityY, 0);
                Active = true;
                return true;
            default:
                Active = false;
                return false;
        }
    }

    /// <summary>Avança <paramref name="dt"/> segundos; devolve a nova velocidade vertical.</summary>
    public float Step(float dt)
    {
        if (Active) VelocityY = Math.Max(VelocityY - _gravity * dt, -_maxFall);
        return VelocityY;
    }

    /// <summary>Encostou no teto (subindo): passa a cair.</summary>
    public void HitCeiling() { if (VelocityY > 0) VelocityY = 0; }

    public void Stop() { Active = false; VelocityY = 0; }

    /// <summary>Altura máxima de um MOVE "Jump" g v0 (unidades).</summary>
    public static float PeakHeight(SeqCommand jump) => jump.F(2) * jump.F(2) / (2 * jump.F(1));
}
