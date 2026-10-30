using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Pulo e queda do jogador pelas sequências do próprio Guilmon (SQ_JUMP, SQ_FALL): o runner toca JUMPS1/2/3 e espera
/// "TurnY" (começou a descer) e "Landing" (tocou o chão); o JumpMotion aplica os MOVE "Jump"/"Fall". Ver docs/gameplay/fase5-pulo.md.
/// </summary>
public partial class Fatia
{
    // FATO (main.dol, controle do jogador perto de 0x8034CB00): o pedido de SQ_JUMP sai do bit 0x2000 da máscara de botões
    // apertados do jogo. DESCONHECIDO: qual botão físico do GC vira esse bit (a tabela de conversão não foi achada).
    // DECISÃO DO PORT PC: Espaço no teclado; no controle, o botão de baixo (A no layout Xbox), provisório até achar o original.
    const JoyButton JumpPadButton = JoyButton.A;
    // HIPÓTESE da fatia: sem chão por esse tempo (fora de um pulo), começa o SQ_FALL; evita cair em cada degrau de rampa.
    const float FallDelay = 0.2f;

    readonly JumpMotion _jump = new();
    bool _jumpHeld;
    float _airTime, _airControl = 1;

    bool InAirSequence => _runner is { Running: true, Sequence: "SQ_JUMP" or "SQ_FALL" };

    void SetupJump(SequenceRunner runner)
    {
        runner.Condition = w => w switch
        {
            "TurnY" => _jump.VelocityY <= 0,
            "Landing" => _player.IsOnFloor() && _jump.VelocityY <= 0,
            _ => true,
        };
        runner.MoveStarted = m => { if (InAirSequence) _jump.Apply(m, _player.Velocity.Y); };
        // PDMOVE "Move" 0,7 6 no começo do SQ_JUMP: HIPÓTESE = controle no ar a 70% da velocidade de andar.
        runner.Command = c =>
        {
            if (c.Kind == "PDMOVE") _airControl = c.S(0) == "Move" ? c.F(1) : 0;
        };
    }

    static bool JumpDown() => PortInput.Down("pulo");

    // Sem pulo com menu aberto (o _PhysicsProcess nem chega aqui), com o controle travado pelo script, caído ou durante um golpe.
    bool TryStartAir(bool jumpPressed)
    {
        if (_runner == null || _runner.Running) return false;
        string? seq = jumpPressed && _player.IsOnFloor() ? "SQ_JUMP"
            : !_player.IsOnFloor() && _airTime >= FallDelay && _player.Velocity.Y < 0 ? "SQ_FALL" : null;
        if (seq == null) return false;
        _airControl = 1;
        return _runner.Start(seq, StyleOf(seq));
    }

    // Velocidade vertical do quadro: MOVE "Jump"/"Fall" da sequência no ar, senão a gravidade da fatia.
    float VerticalVelocity(float vy, float dt)
    {
        if (!InAirSequence && _jump.Active) _jump.Stop(); // golpe, dano ou cena interromperam o pulo
        if (_jump.Active)
        {
            if (_player.IsOnCeiling()) _jump.HitCeiling();
            return _jump.Step(dt);
        }
        return _player.IsOnFloor() ? -1f : vy - Gravity * dt;
    }
}
