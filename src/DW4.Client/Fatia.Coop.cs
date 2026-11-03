using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Gameplay;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Combate em grupo (etapa 6d, ADR 0003). DECISÃO DO RESPONSÁVEL (2026-11-03): inimigos, dano e drops são compartilhados só
/// entre os membros da mesma party na mesma fase; quem não é do grupo vê os outros andando, mas tem os próprios inimigos.
///
/// Como funciona (DECISÃO DO PORT, não é o código do original, que era em tela dividida num só GameCube):
///  · o servidor manda o grupo (<see cref="CoopGroup"/>); o primeiro membro que está nesta fase é o anfitrião;
///  · o anfitrião roda geradores e inimigos como no jogo sozinho e manda: inimigo novo (<see cref="EnemySpawn"/>), posições ~10
///    por segundo (<see cref="EnemySync"/>), sequência começada (<see cref="EnemySeq"/>), morte (<see cref="EnemyKilled"/>), corpo
///    sumido (<see cref="EnemyRemoved"/>) e onda vencida (<see cref="Wipeout"/>);
///  · os outros não geram inimigos (caixas e barris continuam de cada um): mostram cópias, tocam as mesmas sequências (o golpe do
///    inimigo acerta quem está na frente dele em cada tela) e mandam os próprios golpes (<see cref="EnemyHit"/>, dano já calculado
///    com os status de quem bateu);
///  · NINZU (GameRam.Ninzu) = membros na fase: mais inimigos por onda (_geneCount) e a parte do EXP (P2EEXPUP, quem matou leva
///    mais); cada membro sorteia o próprio drop;
///  · a IA mira o membro mais perto; se o anfitrião sai, o próximo assume e as cópias viram inimigos de verdade.
/// Limites conhecidos (v1): chefes com script de acerto (hit_f_*.lua) rodam o script em quem bate e o anfitrião só recebe o dano;
/// o BIT ainda não é dividido; quem assume o meio de uma onda pode não contar direito o que já tinha nascido.
/// </summary>
public partial class Fatia
{
    // Membros do grupo nesta fase (o primeiro é o anfitrião); vazio ou 1 = sozinho.
    int[] _coopMembers = [];
    int _coopHost;
    bool _coopGuest;
    int _coopNextId;
    float _coopSyncTimer;
    // Ao entrar na fase online, espera o grupo chegar antes de gerar inimigos (senão nasce e some logo depois).
    float _coopSettle = 1f;
    readonly Dictionary<int, Enemy> _byNetId = [];

    bool CoopOn => _coopMembers.Length > 1;

    // Quem é o anfitrião agora (chamado a cada quadro e antes de tratar cada pacote do grupo).
    void UpdateCoopRole()
    {
        var g = Online.State == Online.Status.Online ? Online.Group : null;
        int me = Online.MyId;
        int[] members = g == null ? [] : g.Members.Where(id => id == me || (Online.Remotes.TryGetValue(id, out var r)
            && r.State.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase))).ToArray();
        // Grupo de outra fase (acabei de trocar e o servidor ainda não viu): sozinho até chegar o novo.
        if (!members.Contains(me) || members.Length < 2) members = [];
        var added = members.Where(id => id != me && !_coopMembers.Contains(id)).ToList();
        _coopMembers = members;
        _coopHost = members.Length > 0 ? members[0] : 0;
        Ram.Ninzu = Math.Max(1, members.Length);
        bool guest = _coopHost != 0 && _coopHost != me;
        if (guest != _coopGuest)
        {
            _coopGuest = guest;
            if (guest) BecomeGuest(); else BecomeHost();
        }
        // Alguém chegou: o anfitrião manda os inimigos vivos (quem já tinha recebido só atualiza).
        if (!guest && added.Count > 0)
            foreach (var e in _enemies.Where(e => !e.Dead && !e.Killed).ToList()) SendSpawn(e);
    }

    void TickCoop(float dt)
    {
        if (_coopSettle > 0) _coopSettle -= dt;
        UpdateCoopRole();
        if (!CoopOn || _coopGuest) return;
        _coopSyncTimer -= dt;
        if (_coopSyncTimer > 0) return;
        _coopSyncTimer = 0.1f;
        var poses = _enemies.Where(e => !e.Dead).Select(e =>
        {
            var p = e.Body.GlobalPosition;
            return new EnemyPose(e.NetId, MathF.Round(p.X, 2), MathF.Round(p.Y, 2), MathF.Round(p.Z, 2), MathF.Round(e.Yaw, 3), e.Hp);
        }).ToList();
        for (int i = 0; i < poses.Count; i += CoopCodec.MaxPoses)
            Online.SendCoop(new EnemySync(poses.Skip(i).Take(CoopCodec.MaxPoses).ToArray()), false);
    }

    // Outro membro é o anfitrião: os inimigos daqui somem (os dele chegam pelo EnemySpawn).
    void BecomeGuest()
    {
        foreach (var e in _enemies.Where(e => !e.Proxy).ToList())
        {
            if (e.Gen != null && e.Gen.Alive.Remove(e)) e.Gen.Spawned = Math.Max(0, e.Gen.Spawned - 1);
            RemoveEnemy(e);
        }
        ScriptLog($"grupo: inimigos do anfitrião ({_coopHost})");
    }

    // Virei o anfitrião (o de antes saiu da fase ou da party): as cópias viram inimigos de verdade, com IA.
    void BecomeHost()
    {
        foreach (var e in _enemies.Where(e => e.Proxy))
        {
            e.Proxy = false;
            e.Started = false;
            e.Brain?.Release();
            if (e.Killed && !e.Rewarded) e.Killed = e.Hp <= 0;
        }
        ScriptLog(CoopOn ? "grupo: anfitrião" : "grupo: sozinho");
    }

    int NewNetId() => (Online.MyId << 16) | (++_coopNextId & 0xFFFF);

    void SendSpawn(Enemy e)
    {
        if (!CoopOn || _coopGuest || e.Proxy) return;
        var p = e.Body.GlobalPosition;
        Online.SendCoop(new EnemySpawn(e.NetId, e.Kind.Row, p.X, p.Y, p.Z, e.Yaw, e.Hp, e.Gen?.Name ?? ""), true);
    }

    void CoopSequence(Enemy e, string seq, string style)
    {
        if (CoopOn && !_coopGuest && !e.Proxy) Online.SendCoop(new EnemySeq(e.NetId, seq, style), true);
    }

    void CoopRemoved(Enemy e)
    {
        _byNetId.Remove(e.NetId);
        if (CoopOn && !_coopGuest && !e.Proxy) Online.SendCoop(new EnemyRemoved(e.NetId), true);
    }

    void CoopWipeout(Generator g)
    {
        if (CoopOn && !_coopGuest) Online.SendCoop(new Wipeout(g.Name), true);
    }

    // Gerar inimigo aqui? Não quando é o anfitrião quem gera, nem no primeiro segundo da fase online (esperando o grupo).
    bool CoopWaitsForHost => _coopGuest || (_coopSettle > 0 && Online.State is Online.Status.Online or Online.Status.Connecting);

    void RemoveEnemy(Enemy e)
    {
        e.Dead = true;
        e.Body.QueueFree();
        _enemies.Remove(e);
        _byNetId.Remove(e.NetId);
        if (_lastEnemy == e) _lastEnemy = null;
    }

    /// <summary>
    /// Inimigo derrotado por <paramref name="killer"/> (id online; o próprio jogador fora do online). O anfitrião (ou quem joga
    /// sozinho) dá o EXP e o drop e avisa o grupo; numa cópia, só marca e espera o anfitrião confirmar.
    /// </summary>
    void EnemyDefeated(Enemy e, int killer, bool drop = true)
    {
        if (e.Killed) return;
        e.Killed = true;
        if (e.Proxy) { e.ProxyKillWait = 1.5f; return; }
        if (CoopOn) Online.SendCoop(new EnemyKilled(e.NetId, killer, drop), true);
        e.Rewarded = true;
        GainExp(e, killer == Online.MyId);
        if (drop) DropItem(e);
    }

    /// <summary>
    /// Golpe do jogador numa cópia: o dano já saiu daqui (número na tela); o anfitrião aplica e decide a reação. true = era cópia
    /// (quem chamou não mexe na IA).
    /// </summary>
    bool ProxyStruck(Enemy e, int hpBefore, string reaction)
    {
        if (!e.Proxy) return false;
        Online.SendCoop(new EnemyHit(e.NetId, Math.Max(0, hpBefore - e.Hp), reaction), true);
        return true;
    }

    // Membro mais perto de um ponto (a IA do anfitrião mira nele; o gerador "radar" liga com ele).
    Vector3 NearestMember(Vector3 from)
    {
        var best = _player.GlobalPosition;
        if (!CoopOn) return best;
        float d = from.DistanceSquaredTo(best);
        foreach (var id in _coopMembers)
            if (_avatars.TryGetValue(id, out var a) && from.DistanceSquaredTo(a.Root.GlobalPosition) is var dd && dd < d)
                (best, d) = (a.Root.GlobalPosition, dd);
        return best;
    }

    void OnCoop(int from, object msg)
    {
        if (_stage == null) return;
        UpdateCoopRole();
        bool fromHost = _coopGuest && from == _coopHost;
        switch (msg)
        {
            case EnemySpawn s when fromHost:
            {
                var pos = new Vector3(s.X, s.Y, s.Z);
                if (_byNetId.TryGetValue(s.NetId, out var known)) { (known.NetPos, known.NetYaw, known.Hp) = (pos, s.Yaw, s.Hp); break; }
                var gen = s.Gen.Length > 0 ? _generators.GetValueOrDefault(s.Gen) : null;
                if (SpawnEnemy(s.Row, pos, s.Yaw, gen, netId: s.NetId) is not { } e) { ScriptLog($"grupo: não sei mostrar o inimigo da linha {s.Row}"); break; }
                e.Hp = s.Hp;
                if (gen != null) { gen.Spawned++; gen.Alive.Add(e); }
                break;
            }
            case EnemySync y when fromHost:
                foreach (var p in y.Poses)
                    if (_byNetId.TryGetValue(p.NetId, out var e))
                        (e.NetPos, e.NetYaw, e.Hp) = (new Vector3(p.X, p.Y, p.Z), p.Yaw, p.Hp);
                break;
            case EnemySeq q when fromHost && _byNetId.TryGetValue(q.NetId, out var e) && e.Runner != null:
                e.Motion.SequenceChanged();
                e.Started = e.Runner.Start(q.Sequence, q.Style.Length > 0 ? q.Style : null) || StartEnemySequence(e, q.Sequence);
                e.Serial = -1;
                break;
            case EnemyHit h when !_coopGuest && _byNetId.TryGetValue(h.NetId, out var e) && !e.Proxy && !e.Dead && !e.Killed:
            {
                int dmg = Math.Clamp(h.Damage, 0, 999999);
                e.Hp = Math.Max(0, e.Hp - dmg);
                if (dmg > 0) Damage(e.Body, dmg, onPlayer: false, dmg.ToString(), Colors.LightSkyBlue);
                if (e.Hp <= 0) EnemyDefeated(e, from);
                string? reaction = h.Reaction.Length > 0 ? h.Reaction : e.Hp <= 0 && e.Kind.Sequences.ContainsKey("SQ_DDMG") ? "SQ_DDMG" : null;
                if (reaction != null && e.Brain != null && e.Runner != null)
                {
                    e.Brain.Force(reaction);
                    e.Started = false;
                    if (_avatars.TryGetValue(from, out var a))
                    {
                        var to = a.Root.GlobalPosition - e.Body.GlobalPosition;
                        e.Yaw = MathF.Atan2(to.X, to.Z); // vira para quem bateu (como no golpe do jogador)
                    }
                }
                break;
            }
            case EnemyKilled k when fromHost && _byNetId.TryGetValue(k.NetId, out var e) && !e.Rewarded:
                e.Killed = true;
                e.Rewarded = true;
                GainExp(e, k.KillerId == Online.MyId);
                if (k.Drop) DropItem(e);
                break;
            case EnemyRemoved r when fromHost && _byNetId.TryGetValue(r.NetId, out var e):
                e.Gen?.Alive.Remove(e);
                RemoveEnemy(e);
                break;
            case Wipeout w when fromHost && _generators.TryGetValue(w.Generator, out var g) && !g.Finished:
                g.Finished = true;
                g.Active = false;
                ScriptLog($"{g.Name}: onda vencida pelo grupo ({g.CallLua ?? "sem script"})");
                if (g.CallLua != null && _stage.Has(g.CallLua)) _stage.Run(g.CallLua);
                break;
        }
    }

    // Cópia de um inimigo do anfitrião: anda para a posição recebida e toca a sequência que ele mandou (as caixas de golpe da
    // sequência acertam o jogador daqui).
    void UpdateProxy(Enemy e, float dt)
    {
        var pos = e.Body.GlobalPosition;
        float step = Math.Min(1f, dt * 10f);
        e.Body.GlobalPosition = pos.DistanceTo(e.NetPos) > 40 ? e.NetPos : pos.Lerp(e.NetPos, step);
        e.Yaw = Mathf.LerpAngle(e.Yaw, e.NetYaw, step);
        if (e.Model != null) e.Model.Rotation = new Vector3(0, e.Yaw, 0);
        // Matei aqui, mas o anfitrião não confirmou (o golpe dele chegou antes do meu, por exemplo): volta a levar golpe.
        if (e.Killed && !e.Rewarded && (e.ProxyKillWait -= dt) <= 0 && e.Hp > 0) e.Killed = false;
        if (e.Runner != null && e.Started)
        {
            e.Runner.Tick(dt, false);
            if (e.Runner.Running) { TickEnemyAction(e); return; }
            e.Started = false;
        }
        bool moving = pos.DistanceSquaredTo(e.NetPos) > 0.04f;
        var want = moving ? e.Kind.Run : e.Kind.Wait;
        if (e.Anim != null && want != null && e.Anim.HasAnimation(want) && e.Anim.CurrentAnimation != want) e.Anim.Play(want, 8 / SequenceRunner.FramesPerSecond);
    }
}
