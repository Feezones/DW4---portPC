using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// DEPURAÇÃO (só no build Debug, não é do jogo): DW4_BOT=1 joga sozinho para conferir fases sem ninguém no controle — anda até o
/// inimigo vivo mais perto, ataca a menos de 7 unidades e confirma as falas (um toque a cada 0,5 s). A cada 5 s grava no log
/// "[bot]" com FPS, quadro mais lento, HP, inimigos, objetos e a fase.
/// </summary>
public partial class Fatia
{
    readonly bool _bot = OS.IsDebugBuild() && OS.GetEnvironment("DW4_BOT") == "1";
    double _botClock, _botWorst;
    int _botFrames;

    Vector3? _walkTarget;

    void BotInput(ref Vector3 dir, ref bool attackDown)
    {
        // DW4_ANDAR="x,z": anda até esse ponto do mundo (conferir colisão), avisando a posição a cada segundo.
        // DW4_ANDAR=frente: anda 40 unidades para a frente a partir do ponto inicial (varredura das fases).
        if (OS.GetEnvironment("DW4_ANDAR") == "frente")
        {
            _walkTarget ??= _spawn + new Vector3(System.MathF.Sin(_move.Facing), 0, System.MathF.Cos(_move.Facing)) * 40;
            var go2 = new Vector3(_walkTarget.Value.X - _player.GlobalPosition.X, 0, _walkTarget.Value.Z - _player.GlobalPosition.Z);
            dir = go2.Length() > 2 ? go2.Normalized() : Vector3.Zero;
            attackDown = _tick % 30 < 2 && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0);
            if (_tick % 60 == 0) GD.Print($"[andar] {_player.GlobalPosition.Round()} chão {_player.IsOnFloor()} parede {_player.IsOnWall()} início {_spawn.Round()}");
            return;
        }
        if (OS.GetEnvironment("DW4_ANDAR") is { Length: > 0 } walk && walk.Split(",") is [var sx, var sz]
            && float.TryParse(sx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wx)
            && float.TryParse(sz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wz))
        {
            var go = new Vector3(wx, 0, wz) - new Vector3(_player.GlobalPosition.X, 0, _player.GlobalPosition.Z);
            dir = go.Length() > 2 ? go.Normalized() : Vector3.Zero;
            attackDown = _tick % 30 < 2 && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0);
            if (_tick % 60 == 0) GD.Print($"[andar] {_player.GlobalPosition.Round()} chão {_player.IsOnFloor()} parede {_player.IsOnWall()}");
            return;
        }
        var target = _enemies.Where(e => !e.Dead && !e.Killed && e.Body.IsInsideTree())
            .OrderBy(e => e.Body.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).FirstOrDefault();
        bool tap = _tick % 30 < 2;
        if (target == null) { attackDown = tap; return; } // sem inimigo: só confirma as falas
        var to = target.Body.GlobalPosition - _player.GlobalPosition;
        to.Y = 0;
        if (to.Length() > 7) { dir = to.Normalized(); attackDown = tap && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0); }
        else
        {
            // Atacando o jogador não vira (o golpe para o movimento): só ataca virado para o alvo; senão, só vira.
            if (_runner?.Running != true) _move.Face(Mathf.Atan2(to.X, to.Z));
            dir = Vector3.Zero;
            attackDown = _tick % 15 < 2;
        }
    }

    void TickBotStats(double dt)
    {
        if (!_bot) return;
        _botClock += dt;
        _botFrames++;
        _botWorst = System.Math.Max(_botWorst, dt);
        if (_botClock < 5) return;
        GD.Print($"[bot] {_stageName}: {_botFrames / _botClock:0} FPS, quadro mais lento {_botWorst * 1000:0} ms, HP {_hp}/{_pf?.MaxHp ?? 0}, inimigos {_enemies.Count(e => !e.Dead)}, objetos {_objects.Count}, projéteis {_enemyShots.Count} [{string.Join(",", _enemyShots.GroupBy(s => s.Name).Select(g => $"{g.Key}×{g.Count()}"))}], golpes {_hitsTaken}, EXP {_exp}, nível {_level}");
        var near = _enemies.Where(e => !e.Dead).OrderBy(e => e.Body.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).FirstOrDefault();
        if (near != null)
            GD.Print($"[bot]   alvo {near.Kind.Model} HP {near.Hp}/{near.MaxHp} em {near.Body.GlobalPosition.Round()} (jogador {_player.GlobalPosition.Round()}), sequência {near.Runner?.Sequence ?? "-"}, ação {near.Runner?.Action ?? "-"} {near.Runner?.ActionFrame ?? 0:0}/{near.Runner?.ActionLength ?? 0:0}, anim {(near.Anim?.CurrentAnimation ?? "-")}, corpo {near.HitHalf:0.#}×{near.HitHeight:0.#}, iniciado {near.Started}, invencível {near.Runner?.Invincible == true}");
        _botClock = 0;
        _botFrames = 0;
        _botWorst = 0;
    }
}
