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

    void BotInput(ref Vector3 dir, ref bool attackDown)
    {
        var target = _enemies.Where(e => !e.Dead && !e.Killed && e.Body.IsInsideTree())
            .OrderBy(e => e.Body.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).FirstOrDefault();
        bool tap = _tick % 30 < 2;
        if (target == null) { attackDown = tap; return; } // sem inimigo: só confirma as falas
        var to = target.Body.GlobalPosition - _player.GlobalPosition;
        to.Y = 0;
        if (to.Length() > 7) { dir = to.Normalized(); attackDown = tap && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0); }
        else { dir = to.Normalized() * 0.05f; attackDown = _tick % 15 < 2; }
    }

    void TickBotStats(double dt)
    {
        if (!_bot) return;
        _botClock += dt;
        _botFrames++;
        _botWorst = System.Math.Max(_botWorst, dt);
        if (_botClock < 5) return;
        GD.Print($"[bot] {_stageName}: {_botFrames / _botClock:0} FPS, quadro mais lento {_botWorst * 1000:0} ms, HP {_hp}/{_pf?.MaxHp ?? 0}, inimigos {_enemies.Count(e => !e.Dead)}, objetos {_objects.Count}, EXP {_exp}, nível {_level}");
        _botClock = 0;
        _botFrames = 0;
        _botWorst = 0;
    }
}
