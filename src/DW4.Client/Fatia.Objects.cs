using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Objetos de cenário gerados pelos scripts (MODELCTRL nome índice GENERATE "V&lt;pos&gt;/V&lt;rot&gt;") e as caixas dos geradores
/// GBOX (geradas pelo gerador, Fatia.Enemies.cs). Modelos rígidos (KIND_OBJECT sem esqueleto, animados por igTransformSequence) desenhados como os atores do HUD,
/// de &lt;fatia&gt;\objetos, gerado por:
///   dw4 igb-objetos ndmw.afs 179 &lt;fatia&gt;\objetos g_d1door g_ddamagefloor g_box n_recorderdng=23790
///   dw4 igb-seq ndmw.afs 179 &lt;fatia&gt;\objetos\&lt;nome&gt; --ator=&lt;nome&gt;   (um por objeto)
/// Levantamento: docs/gameplay/fase4-objetos-dng1.md.
/// </summary>
public partial class Fatia
{
    sealed class WorldObject : IBeModel
    {
        public required string Name { get; init; }
        public int Index { get; init; }
        public required HudActor Actor { get; init; }
        public Dictionary<string, List<SeqCommand>> Sequences { get; init; } = [];
        public Dictionary<string, List<SeqCommand>> Actions { get; init; } = [];
        public StaticBody3D? Body { get; set; }
        public Generator? Gen { get; init; }
        public int Hp { get; set; }
        public int BulletKind { get; set; }
        public bool Dead { get; set; }
        public bool Erased { get; private set; }
        public string? Sequence { get; private set; }
        float _from, _to, _frame;
        bool _loop, _finished = true;

        /// <summary>
        /// "SQ_WAIT" ou "SQ_WAIT/ON" (SETLABEL): primeira ACTION do bloco e o JOINTSET dela (quadros de 60 Hz; "LP" repete).
        /// HIPÓTESE (simplificação): só a primeira ACTION; SQ_DDMG termina apagando o objeto (o jogo segue para SQ_ERAS).
        /// </summary>
        public void SetSequence(string spec)
        {
            var parts = spec.Split('/');
            string seq = parts[0];
            string? label = parts.Length > 1 ? parts[1] : null;
            Sequence = seq;
            if (seq == "SQ_ERAS") { Erased = true; return; }
            var action = ActionAnimations.FirstAction(Sequences, seq, label);
            var joint = action != null && Actions.TryGetValue(action, out var cmds) ? cmds.FirstOrDefault(c => c.Kind == "JOINTSET") : null;
            if (joint == null) return;
            _from = joint.F(1);
            _to = joint.F(2);
            _loop = joint.S(3) == "LP";
            _frame = _from;
            _finished = false;
        }

        public void Tick(float dt)
        {
            if (!_finished)
            {
                _frame += dt * 60;
                if (_frame >= _to)
                {
                    if (_loop && _to > _from) _frame = _from + (_frame - _to) % (_to - _from);
                    else { _frame = _to; _finished = true; if (Sequence == "SQ_DDMG") Erased = true; }
                }
            }
            Actor.Time = _frame / 60f;
            Actor.Update();
        }
    }

    // FATO (generatorN #184): as caixas GBOX-01..05 são o inimigo da linha 370 da tabela enemy (MODEL "g_box", HPMAX 100).
    const int GboxHp = 100;
    // FATO: caixa do HIT do g_box (−7, 0, −7)..(7, 10, 7). HIPÓTESE: também é a caixa de corpo.
    static readonly Vector3 GboxSize = new(14, 10, 14);

    HudData? _objectData;
    bool _objectDataTried;
    readonly List<WorldObject> _objects = [];
    readonly HashSet<WorldObject> _objectsHitThisAction = [];

    HudData? ObjectData()
    {
        if (_objectDataTried) return _objectData;
        _objectDataTried = true;
        return _objectData = HudData.Load(Path.Combine(_dir, "objetos"), _problems, "objetos.json",
            "falta objetos.json (dw4 igb-objetos ndmw.afs 179 <fatia>\\objetos g_d1door g_ddamagefloor g_box n_recorderdng=23790): objetos só no texto");
    }

    WorldObject? Spawn(string name, int index, Vector3 pos, Vector3 rotDeg, Generator? gen = null)
    {
        if (ObjectData() is not { } data || !data.Actors.TryGetValue(name, out int root)) return null;
        var actor = new HudActor(data, root, slot: 0);
        var seqDir = Path.Combine(_dir, "objetos", name);
        var seqFile = Directory.Exists(seqDir) ? Directory.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        var json = seqFile != null ? File.ReadAllText(seqFile) : null;
        var o = new WorldObject
        {
            Name = name, Index = index, Actor = actor, Gen = gen,
            Sequences = json != null ? SeqCommand.LoadAll(json) : [], Actions = json != null ? SeqCommand.LoadActions(json) : [],
        };
        AddChild(actor.Root);
        // HIPÓTESE: rotação do GENERATE em graus no eixo do mundo (0/±90/180 nos scripts; o −90° do Numemon já bate assim).
        actor.Root.Position = pos;
        actor.Root.RotationDegrees = rotDeg;
        o.SetSequence("SQ_WAIT");
        _objects.Add(o);
        return o;
    }

    WorldObject? SpawnBox(Vector3 at, float rotDeg, Generator gen)
    {
        if (Spawn("g_box", 0, at, new Vector3(0, rotDeg, 0), gen) is not { } box) return null;
        box.Hp = _db != null && _db.Contains("enemy") ? _db["enemy"].Int(gen.EnemyRow, "HPMAX") : GboxHp;
        box.SetSequence("SQ_ENTR");
        var body = new StaticBody3D { Position = at + new Vector3(0, GboxSize.Y / 2, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = GboxSize } });
        AddChild(body);
        box.Body = body;
        return box;
    }

    bool DeliverToObjects(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name == null) return false;
        if (m.Command == "GENERATE")
        {
            if (m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } p)
            {
                var rot = m.Arg(1) is { Kind: 'V', Numbers.Length: 3 } r ? new Vector3((float)r.Numbers[0], (float)r.Numbers[1], (float)r.Numbers[2]) : Vector3.Zero;
                Spawn(m.Name, m.Index, new Vector3((float)p.Numbers[0], (float)p.Numbers[1], (float)p.Numbers[2]), rot);
            }
            return false;
        }
        var targets = _objects.Where(o => o.Name == m.Name && (m.Index < 0 || o.Index == m.Index)).ToList();
        foreach (var o in targets)
        {
            if (m.Command == "SETLABEL" && m.Text(0) is { } label) o.SetSequence(label);
            else if (m.Command.StartsWith("SQ_", StringComparison.Ordinal)) o.SetSequence(m.Command);
        }
        return false;
    }

    void TickObjects(float dt)
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            var o = _objects[i];
            o.Tick(dt);
            if (!o.Erased) continue;
            o.Actor.Root.QueueFree();
            o.Body?.QueueFree();
            _objects.RemoveAt(i);
        }
    }

    // Golpe físico do jogador contra as caixas: roda o hit_<nome>.lua no contexto do objeto (FATO: hit_g_box.lua tira 100
    // de HP se o golpe não for técnica 40040..40099 e põe SQ_DDMG); com HP ≤ 0, o gerador "zera" e roda o _callLua (gboxNN.lua).
    void CheckObjectHits()
    {
        if (_runner == null || _stage == null) return;
        foreach (var h in _runner.ActiveHitBoxes().Where(h => h.Type == 10000))
        {
            var c = BoxCenter(h);
            foreach (var o in _objects.Where(o => o.Body != null && !o.Dead && !_objectsHitThisAction.Contains(o)).ToList())
            {
                var p = o.Actor.Root.Position;
                if (Math.Abs(c.X - p.X) > h.Size.X / 2 + GboxSize.X / 2 || Math.Abs(c.Z - p.Z) > h.Size.Z / 2 + GboxSize.Z / 2
                    || c.Y + h.Size.Y / 2 < p.Y || c.Y - h.Size.Y / 2 > p.Y + GboxSize.Y) continue;
                _objectsHitThisAction.Add(o);
                o.BulletKind = h.Type;
                var script = $"hit_{o.Name}.lua";
                if (_stage.Has(script)) _stage.RunAs(script, o);
                if (o.Hp <= 0)
                {
                    o.Dead = true;
                    o.Body?.QueueFree();
                    o.Body = null;
                    ScriptLog($"{o.Gen?.Name}: {o.Name} destruída");
                    GeneratedDied(o.Gen, o);
                }
            }
        }
    }
}
