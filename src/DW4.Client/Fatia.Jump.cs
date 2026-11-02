using System;
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
    float _airStill, _airLastY, _chargedTime;
    float _airTime, _airControl = 1;

    // FATO (SQ_ATCKTM de cada estilo: MOVE "Jump" 1 3,9, "Push" 2,3 11, WCHECK "TurnY", MOVE "Fall" 1,44 6,3, WCHECK "Landing",
    // MOVE "Stop" 1 4): o golpe carregado é um salto com avanço. Antes só o SQ_JUMP/SQ_FALL aplicavam os MOVE, e o carregado saía
    // parado no lugar (relato de 2026-11-02).
    bool InAirSequence => _runner is { Running: true, Sequence: "SQ_JUMP" or "SQ_FALL" or SequenceRunner.ChargedSequence };

    // Avanço do golpe carregado pelos canais de movimento do jogo (MotionChannels: "Push" = Z modo 4, começa no máximo e freia até 0;
    // "Stop" = Z modo 2). Unidades por quadro de 30 Hz, como os dos inimigos.
    readonly MotionChannels _dash = new();
    bool Dashing => _runner is { Running: true, Sequence: SequenceRunner.ChargedSequence } && _dash.AnyActive;

    void SetupJump(SequenceRunner runner)
    {
        runner.Condition = w => w switch
        {
            "TurnY" => _jump.VelocityY <= 0,
            // DECISÃO DO PORT (segurança, relato de 2026-11-02: Dorumon preso no ar encostado numa mureta depois do carregado): parado
            // no ar sem descer por 0,25 s (apoiado numa borda que a física não chama de chão) também conta como pouso.
            "Landing" => (_player.IsOnFloor() || _airStill >= 0.25f) && _jump.VelocityY <= 0,
            _ => true,
        };
        runner.MoveStarted = m =>
        {
            if (!InAirSequence) return;
            _jump.Apply(m, _player.Velocity.Y);
            if (_runner?.Sequence == SequenceRunner.ChargedSequence && m.S(0) is "Push" or "Stop") _dash.Apply(m);
        };
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
        // Preso no ar: quanto tempo a sequência de ar ficou sem descer (o "Landing" acima usa) e tempo-limite do golpe carregado
        // (o original dura menos de 1,5 s; com 3 s, volta ao controle livre em vez de prender o jogador).
        float y = _player.GlobalPosition.Y;
        _airStill = InAirSequence && !_player.IsOnFloor() && Math.Abs(y - _airLastY) < 0.02f ? _airStill + dt : 0;
        _airLastY = y;
        _chargedTime = _runner is { Running: true, Sequence: SequenceRunner.ChargedSequence } ? _chargedTime + dt : 0;
        if (_chargedTime > 3f) { _runner!.Stop(); _jump.Stop(); _dash.Stop(); _chargedTime = 0; }
        if (!InAirSequence && _jump.Active) _jump.Stop(); // golpe, dano ou cena interromperam o pulo
        if (_jump.Active)
        {
            if (_player.IsOnCeiling()) _jump.HitCeiling();
            return _jump.Step(dt);
        }
        return _player.IsOnFloor() ? -1f : vy - Gravity * dt;
    }
}
