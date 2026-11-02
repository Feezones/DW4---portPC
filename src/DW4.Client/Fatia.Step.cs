using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Subir degraus baixos andando. A cápsula do jogador (raio 2,5) só passa por cima de bordas de até ~0,7 de altura com o
/// limite de chão de 45° da Godot; acima disso a borda vira parede. FATO (print do usuário, 2026-11-02): o Agumon parava no
/// fim da ponte suspensa da primeira área, onde a plataforma azul tem a borda um pouco acima das tábuas, e no jogo dá para
/// passar andando. HIPÓTESE: o jogo sobe degraus de até <see cref="StepHeight"/> (o valor exato do motor não foi medido; os
/// objetos do port já sobem até 12, Fatia.Objects.cs). As paredes da colisão das fases têm ~40 de altura, então o degrau não
/// deixa subir parede de verdade.
/// </summary>
public partial class Fatia
{
    const float StepHeight = 6f;

    /// <summary>Depois do MoveAndSlide: se bateu numa parede andando no chão, tenta subir, avançar e descer até o chão.</summary>
    void StepUp(Vector3 wanted, float dt)
    {
        if (InAirSequence || !_player.IsOnWall() || _airTime > 0.1f) return;
        wanted.Y = 0;
        if (wanted.LengthSquared() < 1f) return;
        var start = _player.GlobalTransform;
        var xf = start;
        var up = Vector3.Up * StepHeight;
        if (_player.TestMove(xf, up)) return; // teto
        xf.Origin += up;
        var dir = wanted.Normalized();
        var ahead = dir * Mathf.Max(wanted.Length() * dt, 0.5f);
        if (_player.TestMove(xf, ahead)) return; // parede de verdade (mais alta que o degrau)
        xf.Origin += ahead;
        var hit = new KinematicCollision3D();
        if (!_player.TestMove(xf, Vector3.Down * (StepHeight + 0.5f), hit)) return; // sem chão: não sobe para o vazio
        if (hit.GetNormal().Y < 0.7f) return;
        xf.Origin += hit.GetTravel();
        float rise = xf.Origin.Y - start.Origin.Y;
        if (rise < 0.05f || rise > StepHeight) return;
        _player.GlobalPosition = xf.Origin;
        _player.ApplyFloorSnap();
    }

    // DEPURAÇÃO (só no build Debug): o que segura o jogador. Aparece no texto do F3 ("parede: ...") e no log "[parede]" a cada
    // segundo parado: peça da colisão da fase (caminho do nó no colisao.gltf), corpo de objeto (nome do modelo) ou outro nó.
    string _wallInfo = "-";
    int _wallLogTick;

    void NoteWall()
    {
        if (!OS.IsDebugBuild()) return;
        if (!_player.IsOnWall()) { _wallInfo = "-"; return; }
        for (int i = 0; i < _player.GetSlideCollisionCount(); i++)
        {
            var c = _player.GetSlideCollision(i);
            if (c.GetNormal().Y > 0.7f) continue;
            var shape = c.GetColliderShape() as CollisionShape3D;
            string? path = shape == null ? null : _collisionShapes.Where(x => x.Shape == shape).Select(x => x.Path).FirstOrDefault();
            string what = path != null ? $"colisão da fase {path}" : c.GetCollider() is Node n ? $"corpo {n.Name}" : "?";
            var p = c.GetPosition();
            _wallInfo = $"{what} em ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0}) normal ({c.GetNormal().X:0.00}, {c.GetNormal().Y:0.00}, {c.GetNormal().Z:0.00}), jogador y {_player.GlobalPosition.Y:0.0}";
            if (_tick - _wallLogTick >= 60) { _wallLogTick = _tick; GD.Print($"[parede] {_stageName}: {_wallInfo}"); }
            return;
        }
    }
}
