using System;
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
    Vector3 _dashFrom;

    void BotInput(ref Vector3 dir, ref bool attackDown)
    {
        // DW4_ANDAR="x,z": anda até esse ponto do mundo (conferir colisão), avisando a posição a cada segundo.
        // DW4_ANDAR=frente: anda 40 unidades para a frente a partir do ponto inicial (varredura das fases).
        // DW4_ALVO=carregado: segura o ataque 5 s, solta e mede o avanço do golpe carregado (log [carregado]).
        if (OS.GetEnvironment("DW4_ALVO") == "carregado")
        {
            if (!_padEnabled || !_playerCtrl || TalkWindowOpen) { attackDown = _tick % 30 < 2; return; }
            int phase = _tick % 480;
            attackDown = phase < 300;
            if (phase == 300) _dashFrom = _player.GlobalPosition;
            if (phase == 479) GD.Print($"[carregado] avanço {new Vector2(_player.GlobalPosition.X - _dashFrom.X, _player.GlobalPosition.Z - _dashFrom.Z).Length():0.0} (de {_dashFrom.Round()} a {_player.GlobalPosition.Round()}), sequência {_runner?.Sequence}");
            if (phase > 300 && _tick % 15 == 0) GD.Print($"[carregado] q{phase - 300} {_runner?.Sequence} {_runner?.Action} {_runner?.ActionFrame:0}/{_runner?.ActionLength:0} pos {_player.GlobalPosition.Round()}");
            return;
        }
        if (OS.GetEnvironment("DW4_ANDAR") == "frente")
        {
            _walkTarget ??= _spawn + new Vector3(System.MathF.Sin(_move.Facing), 0, System.MathF.Cos(_move.Facing)) * 40;
            var go2 = new Vector3(_walkTarget.Value.X - _player.GlobalPosition.X, 0, _walkTarget.Value.Z - _player.GlobalPosition.Z);
            dir = go2.Length() > 2 ? go2.Normalized() : Vector3.Zero;
            attackDown = _tick % 30 < 2 && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0 || !_padEnabled);
            if (_tick % 60 == 0) GD.Print($"[andar] {_player.GlobalPosition.Round()} chão {_player.IsOnFloor()} parede {_player.IsOnWall()} início {_spawn.Round()}");
            return;
        }
        if (OS.GetEnvironment("DW4_ANDAR") is { Length: > 0 } walk && walk.Split(",") is [var sx, var sz]
            && float.TryParse(sx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wx)
            && float.TryParse(sz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float wz))
        {
            var go = new Vector3(wx, 0, wz) - new Vector3(_player.GlobalPosition.X, 0, _player.GlobalPosition.Z);
            dir = go.Length() > 2 ? go.Normalized() : Vector3.Zero;
            attackDown = _tick % 30 < 2 && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0 || !_padEnabled);
            if (_tick % 60 == 0) GD.Print($"[andar] {_player.GlobalPosition.Round()} chão {_player.IsOnFloor()} parede {_player.IsOnWall()}");
            return;
        }
        var target = _enemies.Where(e => !e.Dead && !e.Killed && e.Body.IsInsideTree())
            .OrderBy(e => e.Body.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).FirstOrDefault();
        bool tap = _tick % 30 < 2;
        // DW4_ALVO=npc: visita cada NPC (n_*) da fase, chega perto e aperta o ataque (sonda de conversa); 10 s por NPC.
        if (OS.GetEnvironment("DW4_ALVO") == "npc") { BotTalkToNpcs(ref dir, ref attackDown); return; }
        // DW4_ALVO=objetos: bate nos objetos quebráveis mais perto (contar golpes até quebrar), um golpe por segundo.
        if (OS.GetEnvironment("DW4_ALVO") == "objetos"
            && _objects.Where(o => o.Body != null && !o.Dead && o.Hp > 0).OrderBy(o => o.Actor.Root.GlobalPosition.DistanceSquaredTo(_player.GlobalPosition)).FirstOrDefault() is { } obj)
        {
            if (!_padEnabled || !_playerCtrl || TalkWindowOpen || _choice >= 0 || _listChoice >= 0) { attackDown = _tick % 30 < 2; return; }
            var toObj = obj.Actor.Root.GlobalPosition - _player.GlobalPosition;
            if (_tick % 60 == 0) GD.Print($"[alvo-obj] {obj.Name}/{obj.Index} HP {obj.Hp} a {toObj.Length():0} (jogador {_player.GlobalPosition.Round()})");
            toObj.Y = 0;
            // O corpo do objeto (14 de largura) segura o jogador a ~10; com caixas de corpo fora da raiz (g_covercnsz05: −16..−6 em X),
            // parado a menos de 25 também bate.
            if (toObj.Length() > 12 && !(toObj.Length() < 25 && _player.GetRealVelocity().Length() < 1 && _tick % 120 >= 60)) { dir = toObj.Normalized(); return; }
            if (_runner?.Running != true) _move.Face(Mathf.Atan2(toObj.X, toObj.Z));
            attackDown = _tick % 60 < 2;
            return;
        }
        // DW4_DEPOIS="x,z": sem inimigo vivo, anda até esse ponto (cena depois da luta, ex.: o elevador do staggf03).
        if (target == null && OS.GetEnvironment("DW4_DEPOIS").Split(',') is [var ax, var az] && float.TryParse(ax, System.Globalization.CultureInfo.InvariantCulture, out float afterX)
            && float.TryParse(az, System.Globalization.CultureInfo.InvariantCulture, out float afterZ))
        {
            var go = new Vector3(afterX - _player.GlobalPosition.X, 0, afterZ - _player.GlobalPosition.Z);
            dir = go.Length() > 2 ? go.Normalized() : Vector3.Zero;
        }
        if (target == null) { attackDown = tap; return; } // sem inimigo: só confirma as falas
        // DW4_ALVO=longe: fica a ~55 do alvo, sem atacar (golpes de longe dos chefes: SQ_FIRE, investida).
        if (OS.GetEnvironment("DW4_ALVO") == "longe")
        {
            var away = _player.GlobalPosition - target.Body.GlobalPosition;
            away.Y = 0;
            float d = away.Length();
            if (d < 50 && d > 0.1f) dir = away / d; else if (d > 62) dir = -away / d;
            attackDown = tap && (TalkWindowOpen || _choice >= 0 || _listChoice >= 0 || !_padEnabled);
            return;
        }
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

    int _botNpc = -1;
    double _botNpcClock;

    void BotTalkToNpcs(ref Vector3 dir, ref bool attackDown)
    {
        var only = OS.GetEnvironment("DW4_NPC"); // nome/índice, ex.: n_digilff/1
        var npcs = _objects.Where(o => o.Name.StartsWith("n_", StringComparison.Ordinal) && !o.Dead && (only.Length == 0 || only == $"{o.Name}/{o.Index}")).ToList();
        if (_padEnabled && _playerCtrl && !TalkWindowOpen && _choice < 0 && _listChoice < 0 && !_menuOpen) _botNpcClock += 1.0 / 60; // só conta com o controle livre
        if (_botNpc < 0 || _botNpcClock > 10) { _botNpc++; _botNpcClock = 0; if (_botNpc < npcs.Count) GD.Print($"[npc-bot] -> {npcs[_botNpc].Name}/{npcs[_botNpc].Index} em {npcs[_botNpc].Actor.Root.GlobalPosition.Round()}"); }
        if (!_padEnabled || !_playerCtrl || TalkWindowOpen || _choice >= 0 || _listChoice >= 0 || _menuOpen) { attackDown = _tick % 30 < 2; return; }
        if (_botNpc >= npcs.Count) return;
        var npc = npcs[_botNpc];
        var to = npc.Actor.Root.GlobalPosition - _player.GlobalPosition;
        to.Y = 0;
        // Chega a 40 de uma vez e anda o resto com colisão (balcões seguram o jogador como no jogo).
        if (to.Length() > 40) { float ry = npc.Actor.Root.GlobalRotation.Y; var at = npc.Actor.Root.GlobalPosition + new Vector3(MathF.Sin(ry), 0, MathF.Cos(ry)) * 25; // à frente do NPC (balcão no meio)
            _player.GlobalPosition = at + Vector3.Up * 2; _player.Velocity = Vector3.Zero; return; } // na altura do chão do NPC
        if (to.Length() > 8 && _botNpcClock < 6) dir = to.Normalized();
        else if (_runner?.Running != true) _move.Face(Mathf.Atan2(to.X, to.Z));
        if (_botNpcClock >= 6 && _tick % 60 == 0) GD.Print($"[npc-bot] {npc.Name}/{npc.Index}: aperta o ataque a {to.Length():0.0}");
        attackDown = _botNpcClock >= 6 && _tick % 60 < 2;
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
