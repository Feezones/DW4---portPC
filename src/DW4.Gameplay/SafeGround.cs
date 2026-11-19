using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Última plataforma pisada antes de uma queda (DECISÃO DO PORT, onde o backup chip fica; relato do usuário de 2026-11-05: caindo
/// num buraco e morrendo lá, o chip ficava lá embaixo). Pousar mais de <c>pitDrop</c> abaixo do ponto em que saiu do chão é
/// queda: o ponto guardado fica o de antes, até o jogador voltar a pisar perto daquela altura. Andar no chão (escada, rampa)
/// atualiza normalmente.
/// </summary>
public sealed class SafeGround(float pitDrop)
{
    Vector3? _leftAt;
    bool _inPit;

    public Vector3? Safe { get; private set; }

    public void Step(Vector3 pos, bool onFloor)
    {
        if (!onFloor) { _leftAt ??= pos; return; }
        if (_leftAt is { } from && pos.Y < from.Y - pitDrop) _inPit = true;
        _leftAt = null;
        if (_inPit && Safe is { } safe && pos.Y < safe.Y - pitDrop) return;
        _inPit = false;
        Safe = pos;
    }
}
