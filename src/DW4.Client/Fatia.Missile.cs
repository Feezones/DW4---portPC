using System;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Míssil dos pods de Numemon (x_bltnumemisz01, stag0103). DECISÃO DO PORT (pedido do usuário em 2026-11-06, diferente do
/// original; docs/decisoes-port-pc.md):
///   - não bate no casco do barco (passa por ele, como no jogo; o evento "BHit" vale para os outros tiros);
///   - persegue de leve: só enquanto voa (SQ_WAIT), vira o rumo (yaw) na direção do membro da party mais perto, no máximo
///     <see cref="MissileTurnDegPerSec"/> graus por segundo, sem mudar a velocidade (MOVE "Move" 1 1) nem o tempo de vida (240
///     quadros da caixa 70108);
///   - pode ser quebrado: golpe de perto (10000/20000), tiro de arma e técnica que encostam na caixa do míssil o mandam para o
///     SQ_HIDE (o mesmo do bht_x_bltnumemisz01.lua: some com FADESET HIDE; o efeito y_cannon01 dessa ação é só visual e o port
///     não desenha os y_*), sem dano no jogador.
/// FATO (seq): a caixa de golpe do míssil é (0, −3, 0) com tamanho (4, 7, 4); a caixa para quebrar é a mesma.
/// </summary>
public partial class Fatia
{
    const string PodMissile = "x_bltnumemisz01";
    const float MissileTurnDegPerSec = 75f;
    static readonly Vector3 MissileBoxCenter = new(0, -3, 0), MissileBoxSize = new(4, 7, 4);

    static bool IsPodMissile(Shot s) => s.Name == PodMissile && !s.FromPlayer;

    void SteerMissile(Shot s, float dt)
    {
        if (!IsPodMissile(s) || s.Hit || s.Seq.Erased || s.Seq.Sequence != "SQ_WAIT" || dt <= 0) return;
        var p = s.Root.GlobalPosition;
        var t = NearestMember(p); // em grupo: o membro mais perto (quem roda os objetos decide, como a IA)
        var to = t - p;
        if (to.X * to.X + to.Z * to.Z < 1) return;
        var fwd = s.Root.GlobalBasis.Z; // o MOVE "Move" anda no +z local
        float diff = Mathf.Wrap(MathF.Atan2(to.X, to.Z) - MathF.Atan2(fwd.X, fwd.Z), -MathF.PI, MathF.PI);
        float step = Mathf.DegToRad(MissileTurnDegPerSec) * dt;
        s.Root.GlobalRotate(Vector3.Up, Math.Clamp(diff, -step, step));
        if (DiagRide && _tick % 20 == 0) GD.Print($"[barco] míssil {s.Index} a {to.Length():0} do alvo, desvio {Mathf.RadToDeg(diff):0}°");
    }

    /// <summary>Golpe do jogador (caixa centrada em <paramref name="c"/>) quebra os mísseis que tocar; devolve quantos.</summary>
    int BreakMissiles(Vector3 c, Vector3 size)
    {
        int n = 0;
        foreach (var s in _enemyShots.Where(x => IsPodMissile(x) && !x.Hit && !x.Seq.Erased && x.Seq.Sequence == "SQ_WAIT"))
        {
            var m = s.Root.GlobalPosition + MissileBoxCenter;
            if (Math.Abs(c.X - m.X) > (size.X + MissileBoxSize.X) / 2 || Math.Abs(c.Y - m.Y) > (size.Y + MissileBoxSize.Y) / 2
                || Math.Abs(c.Z - m.Z) > (size.Z + MissileBoxSize.Z) / 2) continue;
            s.Hit = true; // não pega mais o jogador
            if (!s.Seq.Start("SQ_HIDE")) s.Seq.Start("SQ_ERAS");
            if (DiagRide) GD.Print($"[barco] míssil quebrado em {s.Root.GlobalPosition.Round()}");
            n++;
        }
        return n;
    }
}
