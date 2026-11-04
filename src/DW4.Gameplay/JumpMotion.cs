namespace DW4.Gameplay;

/// <summary>
/// Movimento vertical do pulo e da queda pelos MOVE das sequências do jogador.
/// FATO (Guilmon #857, SQ_JUMP de cada estilo): MOVE "Jump" 1,44 6,3 → ACTION JUMPS1 → WCHECK "TurnY" → MOVE "Fall" 1,44 6,3 →
/// ACTION JUMPS2 → WCHECK "Landing" → MOVE "Stop" 1 4 → ACTION JUMPS3. O SQ_FALL é o mesmo sem a parte do "Jump".
/// FATO (executor do MOVE, ver MotionChannels): "Jump" v n = canal Y no modo 4, começa em +v e freia v / (0,5·n²) por quadro²
/// até 0, quando o canal desliga; "Fall" v n = modo 1, acelera para baixo com a mesma conta até −v. Pico v·n² / 4 ≈ 14,3
/// unidades com 1,44 6,3, que passa o degrau de 10 do pedestal do chefe no stagbs01 (chão y 20, topo y 30).
/// HIPÓTESE: unidades por quadro a 30 Hz. "TurnY" = a velocidade vertical chegou a 0; "Landing" = tocou o chão.
/// </summary>
public sealed class JumpMotion
{
    const float Fps = SequenceRunner.FramesPerSecond;

    float _accel, _maxFall;
    bool _rising;

    /// <summary>Velocidade vertical em unidades por segundo (positiva = subindo).</summary>
    public float VelocityY { get; private set; }
    /// <summary>Um MOVE "Jump"/"Fall" está valendo (até o "Stop" do pouso).</summary>
    public bool Active { get; private set; }

    /// <summary>Aceleração do canal em unidades por quadro²: v / (0,5·n²) (0x8030A1F4).</summary>
    static float Accel(SeqCommand move) => move.F(2) == 0 ? 0 : move.F(1) / (0.5f * move.F(2) * move.F(2));

    /// <summary>Aplica um MOVE da sequência; devolve true se era de pulo ou queda.</summary>
    public bool Apply(SeqCommand move, float currentVelocityY = 0)
    {
        switch (move.S(0))
        {
            case "Jump":
                _accel = Accel(move) * Fps * Fps;
                _maxFall = float.PositiveInfinity;
                VelocityY = move.F(1) * Fps;
                _rising = true;
                Active = true;
                return true;
            case "Fall":
                _accel = Accel(move) * Fps * Fps;
                _maxFall = move.F(1) * Fps;
                _rising = false;
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
        if (!Active) return VelocityY;
        VelocityY = Math.Max(VelocityY - _accel * dt, -_maxFall);
        // O canal do "Jump" desliga em 0: fica no alto até o "Fall" da sequência.
        if (_rising && VelocityY <= 0) { VelocityY = 0; _rising = false; }
        return VelocityY;
    }

    /// <summary>Encostou no teto (subindo): passa a cair.</summary>
    public void HitCeiling() { if (VelocityY > 0) { VelocityY = 0; _rising = false; } }

    public void Stop() { Active = false; _rising = false; VelocityY = 0; }

    /// <summary>Altura máxima de um MOVE "Jump" v n (unidades): v² / 2a com a = v / (0,5·n²).</summary>
    public static float PeakHeight(SeqCommand jump) => jump.F(1) * jump.F(2) * jump.F(2) / 4;
}
