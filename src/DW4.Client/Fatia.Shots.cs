using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using DW4.Script.Be;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Projéteis e golpes à distância dos inimigos pelos disparadores de ação do jogo (disparos.json, dw4 igb-disparos: beActionStarterInfo).
/// FATO: o modelo X nasce quando o modelo alvo toca a ação, com a sequência e o deslocamento do disparador (ex.: f_luce ACTN06 →
/// x_035factn06b; este, no quadro 90 do ATCK01 → x_035factn06a, o míssil: MOVE "Move" 1 6, HITBOX 70080 por 1000 quadros, some no
/// fim do ATCK01). Os x_* rodam no ObjectSequencer (disparos/&lt;nome&gt;) e também disparam os seus (cadeias).
/// HIPÓTESES: X nasce no quadro do disparador, no alvo + deslocamento no espaço do alvo e virado como ele; a caixa 7xxxx fere o
/// jogador uma vez (eneatk da linha do código, atacante = o inimigo dono) e o projétil vai para SQ_BREK (ou some). O LUACALL "bht"
/// (bht_x_*.lua, perseguição) ainda não roda. DECISÃO DO PORT: os efeitos visuais (y_*, partículas) não existem; o projétil com
/// caixa aparece como uma esfera brilhante.
/// </summary>
public partial class Fatia
{
    // FATO (campos da classe no main.dol, 0x804D2EC8): _message (sequência de X), _delay (quadro), _offset, _isSeqDead (b6),
    // _isPositionLock (b7). HIPÓTESES: SeqDead = X some quando a sequência de quem disparou acaba; Lock = X acompanha quem disparou.
    sealed record Starter(string Effect, string Action, string Sequence, float Frame, Vector3 Offset, bool SeqDead, bool Lock);

    sealed class Shot
    {
        public required string Name { get; init; }
        public required Node3D Root { get; init; }
        public required ObjectSequencer Seq { get; init; }
        public HudActor? Actor { get; init; }
        public Fighter? Owner { get; init; }
        public string OwnerName { get; init; } = "";
        /// <summary>Índice da instância (MDLisLive "x_…", I): o menor livre entre os vivos com o mesmo nome (HIPÓTESE).</summary>
        public int Index { get; init; }
        public bool Hit { get; set; }
        public string? LastAction { get; set; }
        public float LastFrame { get; set; }
        public Func<Transform3D?>? Source { get; init; }
        public Func<string?>? SourceSequence { get; init; }
        public string? SourceSequenceAtSpawn { get; init; }
        public bool SeqDead { get; init; }
        public bool Lock { get; init; }
        public Vector3 Offset { get; init; }
        /// <summary>Onde nasceu (DIAG: DW4_DIAG_TIRO=1 mostra quanto o tiro do jogador andou até sumir).</summary>
        public Vector3 Born { get; init; }
        /// <summary>Projétil do jogador (caixa 10000, ex. x_100gunv01 da pistola): acerta inimigos, não o jogador.</summary>
        public bool FromPlayer { get; init; }
        /// <summary>LUACALL de batida armado pela sequência (ex. "bht" "BHit" do x_bltnumemisz01): roda ao tocar a colisão.</summary>
        public (string Name, string Call)? HitLua { get; set; }
    }

    // Contexto do bht_<tiro>.lua: MDLsetSeq vai para a sequência do tiro (FATO: bht_x_bltnumemisz01.lua = MDLsetSeq "SQ_HIDE").
    sealed class ShotBeModel(Shot s) : IBeModel
    {
        public int Index => s.Index;
        public int Hp { get => 0; set { } }
        public int BulletKind => 0;
        public string ModelName => s.Name;
        public string? SequenceName => s.Seq.Sequence;
        public void SetSequence(string name) => s.Seq.Start(name);
    }

    /// <summary>
    /// Tiro batendo na colisão (cenário, objetos com corpo e o chão de veículo). FATO (seq): os tiros armam um LUACALL com nome de
    /// chamada "BHit" ou "WHit" (x_bltnumemisz01: "bht" "BHit"; bht_x_bltnumemisz01.lua = MDLsetSeq "SQ_HIDE", que some com FADESET
    /// HIDE e vai ao SQ_ERAS). HIPÓTESE: "BHit"/"WHit" = bater em colisão de chão/parede (beHitLand), e o chão de veículo do barco
    /// (beHitLandModelInfo 0x1405) conta como colisão. Antes o port não armava o evento e os mísseis atravessavam o barco.
    /// FATO: nada liga o tiro à vida do barco (só o hit_p_*.lua mexe no g_boathp): bater no casco só apaga o tiro (escolha do
    /// usuário, 2026-11-06: igual ao original).
    /// </summary>
    void CheckShotLand(Shot s, Vector3 from)
    {
        while (s.Seq.TakeLuaCallWithName() is { } call)
            if (call.Call.EndsWith("Hit", StringComparison.Ordinal)) s.HitLua = call;
        if (IsPodMissile(s)) return; // DECISÃO DO PORT (Fatia.Missile.cs): o míssil dos pods passa pelo casco do barco
        if (s.HitLua is not { } hit || s.Hit || s.Seq.Erased) return;
        var to = s.Root.GlobalPosition;
        if (from.DistanceSquaredTo(to) < 1e-6f) return;
        var q = PhysicsRayQueryParameters3D.Create(from, to);
        q.Exclude = [_player.GetRid()];
        q.CollisionMask = 1; // cenário e veículo; não as paredes só de inimigo (EnemyLayer) nem os parapeitos (EdgeLayer)
        var r = GetWorld3D().DirectSpaceState.IntersectRay(q);
        // Tiro do jogador: passa pelas paredes invisíveis, pelo chão de veículo e pelas caixas (Fatia.ShotScenery.cs); depois de
        // passar, segue o mesmo trecho para não furar uma parede de verdade logo atrás.
        for (int pass = 0; s.FromPlayer && pass < 4 && r.Count > 0 && (Node)r["collider"] is StaticBody3D through; pass++)
        {
            var at = (Vector3)r["position"];
            bool lands = PlayerShotLands(through, at);
            if (DiagShot) GD.Print($"[tiro] {s.Name} {(lands ? "bateu em" : "passou por")} {through.Name} em {at.Round()} normal {((Vector3)r["normal"]).Snapped(0.01f)} peça {ShotShapePath(through, (int)r["shape"])}");
            if (lands) break;
            var next = at + (to - from).Normalized() * 0.05f;
            if (next.DistanceSquaredTo(to) < 1e-4f || (to - next).Dot(to - from) <= 0) return;
            q.From = next;
            r = GetWorld3D().DirectSpaceState.IntersectRay(q);
            if (pass == 3) return;
        }
        if (r.Count == 0 || (Node)r["collider"] is not StaticBody3D body) return;
        s.HitLua = null;
        s.Hit = true; // bateu: a caixa dele não pega mais o jogador (do outro lado da parede)
        if (DiagRide) GD.Print($"[barco] {s.Name} bateu em {body.Name} em {((Vector3)r["position"]).Round()}");
        var script = $"{hit.Name}_{s.Name}.lua";
        if (_stage?.Has(script) == true) _stage.RunAs(script, new ShotBeModel(s));
        else if (!s.Seq.Start("SQ_BREK")) s.Seq.Start("SQ_ERAS");
    }

    string ShotShapePath(StaticBody3D body, int shapeIndex)
    {
        var shape = body.ShapeOwnerGetOwner(body.ShapeFindOwner(shapeIndex)) as CollisionShape3D;
        return shape == null ? "?" : _collisionShapes.Where(x => x.Shape == shape).Select(x => x.Path).FirstOrDefault() ?? shape.Name;
    }

    Dictionary<string, List<Starter>>? _starters;
    readonly List<Shot> _enemyShots = [];
    readonly Dictionary<string, (Dictionary<string, List<SeqCommand>> Seqs, Dictionary<string, List<SeqCommand>> Acts)?> _shotSeqs = [];

    Dictionary<string, List<Starter>> Starters()
    {
        if (_starters != null) return _starters;
        _starters = [];
        var file = Path.Combine(_dir, "disparos.json");
        if (!GameFiles.Exists(file)) return _starters;
        using var doc = JsonDocument.Parse(GameFiles.ReadAllText(file));
        foreach (var d in doc.RootElement.GetProperty("disparos").EnumerateArray())
        {
            string effect = d.GetProperty("nome").GetString() ?? "";
            if (!effect.StartsWith("x_", StringComparison.Ordinal)) continue; // y_*: só efeito visual
            foreach (var t in d.GetProperty("alvos").EnumerateArray())
            {
                string model = t.GetProperty("modelo").GetString() ?? "";
                if (!_starters.TryGetValue(model, out var list)) _starters[model] = list = [];
                foreach (var a in t.GetProperty("acoes").EnumerateArray())
                {
                    var o = a.GetProperty("deslocamento");
                    list.Add(new Starter(effect, a.GetProperty("acao").GetString() ?? "", a.GetProperty("sequencia").GetString() ?? "",
                        a.GetProperty("quadro").GetSingle(), new Vector3(o[0].GetSingle(), o[1].GetSingle(), o[2].GetSingle()),
                        a.TryGetProperty("b6", out var b6) && b6.GetBoolean(), a.TryGetProperty("b7", out var b7) && b7.GetBoolean()));
                }
            }
        }
        return _starters;
    }

    (Dictionary<string, List<SeqCommand>> Seqs, Dictionary<string, List<SeqCommand>> Acts)? ShotSeqs(string name)
    {
        if (_shotSeqs.TryGetValue(name, out var known)) return known;
        var dir = Path.Combine(_dir, "disparos", name);
        var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "*_seq.json").FirstOrDefault() : null;
        if (file == null) return _shotSeqs[name] = null;
        var json = GameFiles.ReadAllText(file);
        return _shotSeqs[name] = (SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
    }

    // Dispara o que a ação atual de um modelo pede: chamado a cada quadro com a ação e o quadro (nas unidades do sequenciador dele).
    void FireStarters(string model, string? action, float frame, ref string? lastAction, ref float lastFrame, Transform3D at, Fighter? owner, string ownerName,
        Func<Transform3D?> source, Func<string?> sourceSequence, bool fromPlayer = false)
    {
        if (action == null) { lastAction = null; return; }
        if (!Starters().TryGetValue(model, out var list)) { lastAction = action; lastFrame = frame; return; }
        float from = action == lastAction ? lastFrame : -1;
        foreach (var s in list)
            if (s.Action == action && s.Frame > from && s.Frame <= frame && (!fromPlayer || PlayerBullet(s.Effect) || ChargeEffect(s.Effect)))
                SpawnShot(s, at, owner, ownerName, source, sourceSequence, fromPlayer);
        lastAction = action;
        lastFrame = frame;
    }

    // Do jogador só saem os x_* com caixa de golpe dele (FATO: entre os x_100*, só o x_100gunv01 tem HITBOX 10000; os outros são
    // rastros de golpe, e as técnicas x_200* já saem pelo ReleaseTechnique).
    bool PlayerBullet(string effect) =>
        ShotSeqs(effect) is { } seqs && seqs.Seqs.Values.Any(cmds => cmds.Any(c => c.Kind == "HITBOX" && c.I(1) == 10000));

    // Efeitos do golpe carregado (FATO, disparos.json, beActionStarterInfo de todos os p_*): x_100charge01 nas poses de carregar
    // (KATC?1 → SQ_S01/SQ_D01… nos quadros 6 e 18; KATC?2 → SQ_KIRIKAE, o clarão de "carregado", e SQ_OWARI em laço nos quadros 0
    // e 12) e o x_100<estilo>k01 na soltura (KATC?3, quadro 0). Todos com b6 (somem quando a sequência do jogador muda) e b7 (presos
    // ao jogador). Sem caixa de golpe: só visual. Antes o filtro acima deixava sair só o tiro da pistola e a carga não aparecia.
    static bool ChargeEffect(string effect) =>
        effect == "x_100charge01" || effect.StartsWith("x_100", StringComparison.Ordinal) && effect.EndsWith("k01", StringComparison.Ordinal);

    void SpawnShot(Starter s, Transform3D at, Fighter? owner, string ownerName, Func<Transform3D?> source, Func<string?> sourceSequence, bool fromPlayer = false)
    {
        if (DiagNpc) GD.Print($"[tiro] {ownerName}: {s.Effect} ({s.Action})");
        if (_enemyShots.Count > 200 || ShotSeqs(s.Effect) is not { } seqs) return;
        var seq = new ObjectSequencer(seqs.Seqs, seqs.Acts);
        if (!seq.Start(s.Sequence) && !seq.Start(seqs.Seqs.Keys.FirstOrDefault(k => k.StartsWith("SQ_ATCK", StringComparison.Ordinal)) ?? "")) return;
        var root = new Node3D { Name = s.Effect };
        AddChild(root);
        root.GlobalTransform = new Transform3D(at.Basis, at * s.Offset);
        bool hurts = seqs.Seqs.Values.Any(cmds => cmds.Any(c => c.Kind == "HITBOX" && c.I(1) / 10000 == 7));
        // HIPÓTESE: o projétil com caixa sai mirando o jogador (o míssil do f_luce nasce 45 acima e só anda para a frente; os bits
        // b6/b7 do disparador não foram lidos). Frente do modelo = +Z.
        var aim = _player.GlobalPosition + Vector3.Up * 4 - root.GlobalPosition;
        if (hurts && !s.Lock && aim.LengthSquared() > 1e-4f) root.GlobalBasis = Basis.LookingAt(aim.Normalized(), Vector3.Up, true);
        // Modelo do jogo (disparos/objetos.json, igb-objetos dos x_*); sem ele, a esfera de antes no que fere.
        HudActor? actor = null;
        if (ShotModels() is { } models && models.Actors.TryGetValue(s.Effect, out int shotRoot))
        {
            actor = new HudActor(models, shotRoot, slot: 0);
            root.AddChild(actor.Root);
        }
        else if (hurts)
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 1.5f, Height = 3 },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.55f, 0.25f), EmissionEnabled = true, Emission = new Color(1f, 0.4f, 0.1f) },
            });
        int index = 0;
        while (_enemyShots.Any(o => o.Name == s.Effect && o.Index == index)) index++;
        _enemyShots.Add(new Shot { Name = s.Effect, Root = root, Seq = seq, Owner = owner, OwnerName = ownerName, Index = index, Actor = actor,
            Source = source, SourceSequence = sourceSequence, SourceSequenceAtSpawn = sourceSequence(), SeqDead = s.SeqDead, Lock = s.Lock, Offset = s.Offset, FromPlayer = fromPlayer, Born = root.GlobalPosition });
    }

    HudData? _shotModels;
    bool _shotModelsTried;

    HudData? ShotModels()
    {
        if (_shotModelsTried) return _shotModels;
        _shotModelsTried = true;
        var dir = Path.Combine(_dir, "disparos");
        if (GameFiles.Exists(Path.Combine(dir, "objetos.json"))) _shotModels = HudData.Load(dir, _problems, "objetos.json", null);
        return _shotModels;
    }

    void TickShots(float dt)
    {
        // Inimigos disparam pela ação do SequenceRunner (quadros a 30 Hz, como os WAITCHECK deles).
        foreach (var e in _enemies.Where(e => !e.Dead && e.Started && e.Runner?.Running == true))
        {
            string? la = e.ShotAction; float lf = e.ShotFrame;
            var at = new Transform3D(new Basis(Vector3.Up, e.Yaw), e.Body.GlobalPosition);
            var src = e;
            FireStarters(e.Kind.Model, e.Runner!.Action, e.Runner.ActionFrame, ref la, ref lf, at, e.Kind.Stats, e.Kind.Name,
                () => src.Dead ? null : new Transform3D(new Basis(Vector3.Up, src.Yaw), src.Body.GlobalPosition),
                () => src.Dead || !src.Started ? null : src.Runner?.Sequence);
            e.ShotAction = la; e.ShotFrame = lf;
        }
        // Jogador: tiro da arma pela ação (FATO: x_100gunv01 na VATCG1 do p_vmon, quadro 10, deslocamento (0, 6,5, 1); quadros do
        // SequenceRunner, como os dos inimigos).
        if (_runner?.Running == true && _hp > 0)
        {
            string? la = _playerShotAction; float lf = _playerShotFrame;
            var at = new Transform3D(new Basis(Vector3.Up, _move.Facing), _player.GlobalPosition);
            // HIPÓTESE: o quadro do disparador do jogador é de 60 Hz (a VATCG1 dura 0,217 s = 6,5 quadros de 30 Hz e o x_100gunv01
            // sai no quadro 10; em 60 Hz, 0,167 s, dentro da ação).
            FireStarters(PlayerModel, _runner.Action, _runner.ActionFrame * 2, ref la, ref lf, at, _pf, PlayerModel,
                () => new Transform3D(new Basis(Vector3.Up, _move.Facing), _player.GlobalPosition), () => _runner?.Sequence, fromPlayer: true);
            _playerShotAction = la; _playerShotFrame = lf;
        }
        // Objetos também disparam pela ação (FATO: x_bltcannon01 na BLST01 do g_cannon01, deslocamento (0, 7,5, 1)).
        foreach (var o in _objects.Where(o => !o.Dead && o.Seq != null && !o.Erased).ToList())
        {
            string? la = o.ShotAction; float lf = o.ShotFrame;
            var src = o;
            FireStarters(o.Name, o.Seq!.Action, o.Seq.ActionTime, ref la, ref lf, o.Actor.Root.GlobalTransform, o.Owner?.Kind.Stats, o.Name,
                () => src.Dead || src.Erased ? null : src.Actor.Root.GlobalTransform, () => src.Dead || src.Erased ? null : src.Seq?.Sequence);
            o.ShotAction = la; o.ShotFrame = lf;
        }
        for (int i = _enemyShots.Count - 1; i >= 0; i--)
        {
            var s = _enemyShots[i];
            float alpha = s.Seq.Alpha;
            s.Seq.Tick(dt);
            if (s.Actor is { } act)
            {
                // Mesmo esquema dos objetos: JOINTSET dá o tempo das igTransformSequence; FADESET, a transparência.
                if (s.Seq.Alpha != alpha) act.SetAlpha(s.Seq.Alpha);
                act.Root.Visible = s.Seq.Alpha > 0;
                act.Time = s.Seq.Frame / 60f;
                act.Update();
            }
            SteerMissile(s, dt);
            var before = s.Root.GlobalPosition;
            if (s.Seq.Motion.AnyActive && dt > 0)
            {
                // HIPÓTESE (relato "o tiro da Shot Pistol é lento"): o tiro do jogador anda no relógio do sequenciador de objetos (60 Hz, o
                // mesmo dos WAITCHECK "Frame" dele); os de inimigo continuam a 30 Hz.
                var (local, yawDeg) = s.Seq.Motion.Step(dt * (s.FromPlayer ? ObjectSequencer.FramesPerSecond : SequenceRunner.FramesPerSecond));
                s.Root.RotateY(yawDeg * MathF.PI / 180);
                s.Root.TranslateObjectLocal(new Vector3(local.X, local.Y, local.Z));
            }
            // Tiro do jogador: o acerto em inimigo/objeto vem antes da batida (a caixa g_box tem corpo na camada 1 e o tiro batia nele
            // sem dar dano). DIAG: DW4_DIAG_TIRO=1 mostra onde o tiro do jogador bateu ou passou.
            if (s.FromPlayer && !s.Hit) CheckPlayerShotHit(s);
            CheckShotLand(s, before);
            string? la = s.LastAction; float lf = s.LastFrame;
            var me = s;
            FireStarters(s.Name, s.Seq.Action, s.Seq.ActionTime, ref la, ref lf, s.Root.GlobalTransform, s.Owner, s.OwnerName,
                () => me.Seq.Erased ? null : me.Root.GlobalTransform, () => me.Seq.Erased ? null : me.Seq.Sequence);
            s.LastAction = la; s.LastFrame = lf;
            if (!s.Hit && !s.FromPlayer) CheckShotHit(s);
            if (s.Lock && s.Source?.Invoke() is { } t) s.Root.GlobalTransform = new Transform3D(t.Basis, t * s.Offset);
            bool orphan = s.SeqDead && s.SourceSequence?.Invoke() != s.SourceSequenceAtSpawn;
            // A carga solta antes da hora (o SQ_ATCK1 para sem trocar de nome) ou parada (Stop): o brilho (b6) não fica no jogador.
            if (s.FromPlayer && s.Name == "x_100charge01" && _runner?.Running != true) orphan = true;
            if (s.Seq.Erased || !s.Seq.Running || orphan)
            {
                if (DiagShot && s.FromPlayer && PlayerBullet(s.Name))
                    GD.Print($"[tiro] {s.Name} sumiu em {s.Root.GlobalPosition.Round()} depois de {s.Born.DistanceTo(s.Root.GlobalPosition):0} ({(orphan ? "órfão" : s.Seq.Erased ? "apagado" : "sequência acabou")}, {(s.Hit ? "bateu" : "sem batida")}, seq {s.Seq.Sequence})");
                s.Root.QueueFree();
                _enemyShots.RemoveAt(i);
            }
        }
    }

    // Efeito x_* preso ao jogador (sem caixa de golpe), como os disparadores fazem: ex. o brilho de subida no level up.
    void PlayerEffect(string effect, string sequence)
    {
        var at = new Transform3D(new Basis(Vector3.Up, _move.Facing), _player.GlobalPosition);
        SpawnShot(new Starter(effect, "", sequence, 0, new Vector3(0, 1, 0), false, true), at, _pf, PlayerModel,
            () => new Transform3D(new Basis(Vector3.Up, _move.Facing), _player.GlobalPosition), () => _runner?.Sequence);
    }

    string? _playerShotAction;
    float _playerShotFrame;

    // Tiro do jogador contra os inimigos: mesma conta do golpe físico (PlayerStrikes, código 10000) e some no primeiro acerto
    // (HIPÓTESE: o bht do x_100gunv01 apaga o tiro ao acertar, como o SQ_BREK/SQ_ERAS dos tiros de inimigo).
    void CheckPlayerShotHit(Shot s)
    {
        foreach (var h in s.Seq.ActiveHitBoxes.Where(h => h.Type == 10000))
        {
            var c = s.Root.GlobalTransform * new Vector3(h.Position.X, h.Position.Y, h.Position.Z);
            if (BreakMissiles(c, new Vector3(h.Size.X, h.Size.Y, h.Size.Z)) > 0) { s.Hit = true; s.Seq.Start("SQ_ERAS"); return; }
            foreach (var e in _enemies.ToList())
            {
                if (e.Dead || e.Killed || (e.Started && e.Runner?.Invincible == true)) continue;
                var p = e.Body.GlobalPosition;
                bool touch = e.Kind.BodyBoxes.Count > 0
                    ? HitBody.Hits(N(c), h.Size, s.Root.GlobalRotation.Y, N(p), e.Yaw, e.Model?.Scale.X ?? 1, e.Kind.BodyBoxes)
                    : Math.Abs(c.X - p.X) <= h.Size.X / 2 + e.HitHalf && Math.Abs(c.Z - p.Z) <= h.Size.Z / 2 + e.HitHalf && c.Y + h.Size.Y / 2 >= p.Y && c.Y - h.Size.Y / 2 <= p.Y + e.HitHeight;
                if (!touch) continue;
                s.Hit = true;
                _hits++;
                _lastEnemy = e;
                int hpBefore = e.Hp;
                if (UsesHitScript(e)) { StrikeWithScript(e, h.Type); ProxyStruck(e, hpBefore, ""); }
                else
                {
                    string reaction = PlayerStrikes(e, h.Type);
                    if (!ProxyStruck(e, hpBefore, reaction) && e.Brain != null && e.Runner != null) { e.Brain.Force(reaction); e.Started = false; }
                }
                s.Seq.Start("SQ_ERAS");
                return;
            }
            // Objetos também levam o tiro (FATO: hit_g_rock01z01.lua pergunta BLTisPlayerBullet).
            foreach (var o in _objects.Where(o => o.Body != null && !o.Dead).ToList())
            {
                // Caixas do HIT do objeto quando ele tem (a jaula g_numeprisonz01 tem 20 × 20 × 20; a caixa da g_box só 14 × 10 × 14).
                if (!ObjectTouched(o, c, new Vector3(h.Size.X, h.Size.Y, h.Size.Z))) continue;
                s.Hit = true;
                StrikeObject(o, h.Type);
                s.Seq.Start("SQ_ERAS");
                return;
            }
        }
    }

    void CheckShotHit(Shot s)
    {
        if (_runner?.Invincible == true || _pf == null || _hp <= 0) return;
        var g = _player.GlobalPosition;
        foreach (var h in s.Seq.ActiveHitBoxes.Where(h => h.Type / 10000 == 7))
        {
            var c = s.Root.GlobalTransform * new Vector3(h.Position.X, h.Position.Y, h.Position.Z);
            if (!HitsPlayer(c, h.Size, s.Root.GlobalRotation.Y)) continue;
            s.Hit = true;
            _hitsTaken++;
            if (!PlayerHitScript(h.Type, s.Owner, s.OwnerName, s.Root.GlobalPosition))
            {
                var reaction = EnemyStrikes(h.Type, s.Owner, s.OwnerName);
                if (reaction != null && _runner != null && (_runner.Start(reaction, StyleOf(reaction)) || _runner.Start(reaction, "common"))) _move.Stop();
            }
            if (!s.Seq.Start("SQ_BREK")) s.Seq.Start("SQ_ERAS");
            return;
        }
    }
}
