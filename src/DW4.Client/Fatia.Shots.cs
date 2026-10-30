using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
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
    }

    Dictionary<string, List<Starter>>? _starters;
    readonly List<Shot> _enemyShots = [];
    readonly Dictionary<string, (Dictionary<string, List<SeqCommand>> Seqs, Dictionary<string, List<SeqCommand>> Acts)?> _shotSeqs = [];

    Dictionary<string, List<Starter>> Starters()
    {
        if (_starters != null) return _starters;
        _starters = [];
        var file = Path.Combine(_dir, "disparos.json");
        if (!File.Exists(file)) return _starters;
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
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
        var file = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*_seq.json").FirstOrDefault() : null;
        if (file == null) return _shotSeqs[name] = null;
        var json = File.ReadAllText(file);
        return _shotSeqs[name] = (SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
    }

    // Dispara o que a ação atual de um modelo pede: chamado a cada quadro com a ação e o quadro (nas unidades do sequenciador dele).
    void FireStarters(string model, string? action, float frame, ref string? lastAction, ref float lastFrame, Transform3D at, Fighter? owner, string ownerName,
        Func<Transform3D?> source, Func<string?> sourceSequence)
    {
        if (action == null) { lastAction = null; return; }
        if (!Starters().TryGetValue(model, out var list)) { lastAction = action; lastFrame = frame; return; }
        float from = action == lastAction ? lastFrame : -1;
        foreach (var s in list)
            if (s.Action == action && s.Frame > from && s.Frame <= frame)
                SpawnShot(s, at, owner, ownerName, source, sourceSequence);
        lastAction = action;
        lastFrame = frame;
    }

    void SpawnShot(Starter s, Transform3D at, Fighter? owner, string ownerName, Func<Transform3D?> source, Func<string?> sourceSequence)
    {
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
            Source = source, SourceSequence = sourceSequence, SourceSequenceAtSpawn = sourceSequence(), SeqDead = s.SeqDead, Lock = s.Lock, Offset = s.Offset });
    }

    HudData? _shotModels;
    bool _shotModelsTried;

    HudData? ShotModels()
    {
        if (_shotModelsTried) return _shotModels;
        _shotModelsTried = true;
        var dir = Path.Combine(_dir, "disparos");
        if (File.Exists(Path.Combine(dir, "objetos.json"))) _shotModels = HudData.Load(dir, _problems, "objetos.json", null);
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
            if (s.Seq.Motion.AnyActive && dt > 0)
            {
                var (local, yawDeg) = s.Seq.Motion.Step(dt * SequenceRunner.FramesPerSecond);
                s.Root.RotateY(yawDeg * MathF.PI / 180);
                s.Root.TranslateObjectLocal(new Vector3(local.X, local.Y, local.Z));
            }
            string? la = s.LastAction; float lf = s.LastFrame;
            var me = s;
            FireStarters(s.Name, s.Seq.Action, s.Seq.ActionTime, ref la, ref lf, s.Root.GlobalTransform, s.Owner, s.OwnerName,
                () => me.Seq.Erased ? null : me.Root.GlobalTransform, () => me.Seq.Erased ? null : me.Seq.Sequence);
            s.LastAction = la; s.LastFrame = lf;
            if (!s.Hit) CheckShotHit(s);
            if (s.Lock && s.Source?.Invoke() is { } t) s.Root.GlobalTransform = new Transform3D(t.Basis, t * s.Offset);
            bool orphan = s.SeqDead && s.SourceSequence?.Invoke() != s.SourceSequenceAtSpawn;
            if (s.Seq.Erased || !s.Seq.Running || orphan)
            {
                s.Root.QueueFree();
                _enemyShots.RemoveAt(i);
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
            if (Math.Abs(c.X - g.X) > h.Size.X / 2 + 2.5f || Math.Abs(c.Z - g.Z) > h.Size.Z / 2 + 2.5f || c.Y + h.Size.Y / 2 < g.Y || c.Y - h.Size.Y / 2 > g.Y + 7) continue;
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
