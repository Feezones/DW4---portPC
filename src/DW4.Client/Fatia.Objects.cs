using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;
using NVector3 = System.Numerics.Vector3;

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
        /// <summary>Caixas do corpo (beModelCtrlInfoDataHit); vazio = a caixa da g_box (GboxSize).</summary>
        public List<(NVector3 Min, NVector3 Max)> BodyBoxes { get; init; } = [];
        public StaticBody3D? Body { get; set; }
        /// <summary>Chão de veículo (RideLand: beHitLandModelInfo com 0x1000, o g_boatz01) preso ao ator, com a caixa local dele.</summary>
        public StaticBody3D? Ride { get; set; }
        public Vector3 RideMin { get; set; }
        public Vector3 RideMax { get; set; }
        public Generator? Gen { get; init; }
        public int Hp { get; set; }
        (double X, double Y, double Z) IBeModel.Position => (Actor.Root.GlobalPosition.X, Actor.Root.GlobalPosition.Y, Actor.Root.GlobalPosition.Z);
        double IBeModel.Yaw => Actor.Root.Rotation.Y;
        string? IBeModel.SequenceName => Seq?.Sequence;
        string IBeModel.ModelName => Name;
        public int BulletKind { get; set; }
        public bool Dead { get; set; }
        public bool Erased => Seq?.Erased == true;
        public string? Sequence => Seq?.Sequence;
        /// <summary>Sequências do objeto (ObjectSequencer: rótulos, ações, esperas, HITBOX, JUMP, FADESET).</summary>
        public ObjectSequencer? Seq { get; set; }
        /// <summary>BoxSerial da última caixa que já acertou o jogador (um acerto por caixa ligada).</summary>
        public int HitSerial { get; set; } = -1;
        /// <summary>Personagem com esqueleto (NPC n_*, exportado em npcs&lt;nome&gt;) e a ação que está tocando.</summary>
        /// <summary>Modelo dono (GENERATE "f_apocaly/I&lt;0&gt;/…": o f_bit fica preso ao chefe e usa os atributos dele nos golpes).</summary>
        public Enemy? Owner { get; set; }
        /// <summary>IA de objeto com mapas (f_bit: MAP00 e MAP01, SQ_PHASE2 troca pelo AIMAP) e a sequência que ela pediu.</summary>
        public EnemyBrain? Brain { get; set; }
        public string? AiSequence { get; set; }
        /// <summary>FLASHSET já visto (Fatia.Flash) e ação e quadro já vistos pelos disparadores (Fatia.Shots).</summary>
        public int FlashSerial { get; set; }
        public string? ShotAction { get; set; }
        public float ShotFrame { get; set; }
        /// <summary>NPC passeando (o script ligou um mapa de IA, ex.: SQ_WALKON → AIMAP MAP01) e o ponto atual do aim_&lt;nome&gt;.lua.</summary>
        public bool Wander { get; set; }
        public Vector3? Target { get; set; }
        /// <summary>NPC no chão: o deslocamento do MOVE não atravessa parede e segue a altura do chão (DECISÃO DO PORT, sem a colisão do jogo).</summary>
        public bool Grounded { get; init; }
        public bool Blocked { get; set; }
        public AnimationPlayer? CharacterAnim { get; set; }
        public int CharacterMotionSerial { get; set; } = -1;

        /// <summary>"SQ_WAIT" ou "SQ_WAIT/ON" (SETLABEL: guarda o rótulo e começa a sequência nele).</summary>
        public void SetSequence(string spec)
        {
            if (Seq == null) return;
            var parts = spec.Split('/');
            if (parts.Length > 1) Seq.SetLabel(parts[0], parts[1]);
            Seq.Start(parts[0]);
        }

        public void Tick(float dt)
        {
            if (Seq == null) return;
            float alpha = Seq.Alpha;
            Seq.Tick(dt);
            if (Seq.Motion.AnyActive && dt > 0)
            {
                // MOVE/MOVEEX: deslocamento no espaço local do objeto e giro Y em graus (HIPÓTESE: quadros a 30 Hz, como os inimigos).
                var (local, yawDeg) = Seq.Motion.Step(dt * SequenceRunner.FramesPerSecond);
                Actor.Root.RotateY(yawDeg * MathF.PI / 180);
                if (Grounded) MoveOnGround(new Vector3(local.X, local.Y, local.Z));
                else Actor.Root.TranslateObjectLocal(new Vector3(local.X, local.Y, local.Z));
            }
            if (Seq.Alpha != alpha) Actor.SetAlpha(Seq.Alpha);
            Actor.Root.Visible = Seq.Alpha > 0;
            // DECISÃO DO PORT (parede invisível): objeto transparente (FADE em 0, ainda entrando ou sumindo) não segura o jogador.
            if (Body != null) Body.ProcessMode = Actor.Root.Visible ? Node.ProcessModeEnum.Inherit : Node.ProcessModeEnum.Disabled;
            Actor.Time = Seq.Frame / 60f;
            Actor.Update();
        }

        // Anda no plano: um raio na altura do peito barra paredes (e o jogador); outro para baixo acha o chão (degraus até 12).
        void MoveOnGround(Vector3 local)
        {
            var root = Actor.Root;
            var move = root.GlobalBasis * local;
            move.Y = 0;
            if (move.LengthSquared() < 1e-8f) return;
            var space = root.GetWorld3D().DirectSpaceState;
            var from = root.GlobalPosition + Vector3.Up * 4;
            var ahead = PhysicsRayQueryParameters3D.Create(from, from + move + move.Normalized() * 2.5f);
            if (space.IntersectRay(ahead).Count > 0) { Blocked = true; return; }
            var next = root.GlobalPosition + move;
            var down = PhysicsRayQueryParameters3D.Create(next + Vector3.Up * 12, next + Vector3.Down * 30);
            down.HitBackFaces = false;
            var floor = space.IntersectRay(down);
            if (floor.Count == 0) { Blocked = true; return; } // sem chão à frente: não anda para o vazio
            next.Y = ((Vector3)floor["position"]).Y;
            root.GlobalPosition = next;
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
        var seqFile = GameFiles.DirExists(seqDir) ? GameFiles.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        var json = seqFile != null ? GameFiles.ReadAllText(seqFile) : null;
        var o = new WorldObject
        {
            Name = name, Index = index, Actor = actor, Gen = gen,
            Actions = json != null ? SeqCommand.LoadActions(json) : [],
            BodyBoxes = json != null ? HitBody.Load(json) : [],
            Seq = json != null ? new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)) : null,
            Grounded = name.StartsWith("n_", StringComparison.Ordinal),
        };
        // f_* (f_bit): IA de luta. n_*: IA de passeio, ligada quando o script manda um AIMAP (ver UpdateNpcAi).
        if (o.Seq != null && name.StartsWith("n_", StringComparison.Ordinal)) o.Seq.ReturnToWaitAtEnd = true; // NPC: SQ_TALK sem JUMP volta ao SQ_WAIT (HIPÓTESE)
        // g_* com mapa de IA (FATO: g_cannon01 MAP00 2500/3600/6400 "NEAR": perto → SQ_FIRE) também ganha o cérebro.
        if ((name.StartsWith("f_", StringComparison.Ordinal) || name.StartsWith("n_", StringComparison.Ordinal) || name.StartsWith("g_", StringComparison.Ordinal)) && json != null && AiMap.LoadAll(json) is { Count: > 0 } maps) o.Brain = new EnemyBrain(maps, SeqCommand.LoadAll(json), () => _rng.Next(16));
        AddChild(actor.Root);
        // HIPÓTESE: rotação do GENERATE em graus no eixo do mundo (0/±90/180 nos scripts; o −90° do Numemon já bate assim).
        actor.Root.Position = pos;
        actor.Root.RotationDegrees = rotDeg;
        AttachCharacter(o);
        MakeRideLand(o, data);
        o.SetSequence("SQ_WAIT");
        // FATO (tabela enemy: g_box, g_boxfield, g_bbarrelsz01, g_woodencasez0x… com HPMAX 100; lua-trace dos hit_g_*.lua: golpe comum
        // tira 34 da pedra, 50 do arbusto, 100 da caixa, e o script manda SQ_SDMG enquanto sobra HP). Antes só a caixa de gerador
        // tinha HP; o resto começava com 0 e quebrava no primeiro golpe. HIPÓTESE: os g_* fora da tabela também têm 100 (as contas
        // dos scripts, 3 golpes na pedra e 2 no arbusto, só fecham assim).
        if (name.StartsWith("g_", StringComparison.Ordinal))
            o.Hp = _db != null && _db.Contains("enemy") && _db["enemy"].RowsWhere("MODEL", name).DefaultIfEmpty(-1).First() is int hpRow and >= 0
                ? _db["enemy"].Int(hpRow, "HPMAX") : GboxHp;
        _objects.Add(o);
        return o;
    }

    // Objetos destrutíveis gerados por gerador (g_box, e nos campos g_boxfield, g_bbarrelsz01, g_woodencasez01…).
    // HIPÓTESE: corpo 14 × 10 × 14 do g_box para todos até ler o HIT de cada um.
    WorldObject? SpawnBox(string model, Vector3 at, float rotDeg, Generator gen)
    {
        if (Spawn(model, 0, at, new Vector3(0, rotDeg, 0), gen) is not { } box) return null;
        box.Hp = _db != null && _db.Contains("enemy") ? _db["enemy"].Int(gen.EnemyRow, "HPMAX") : GboxHp;
        box.SetSequence("SQ_ENTR");
        var body = new StaticBody3D { Name = $"corpo_{model}", Position = at + new Vector3(0, GboxSize.Y / 2, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = GboxSize } });
        AddChild(body);
        box.Body = body;
        return box;
    }

    bool DeliverToObjects(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name == null) return false;
        // DECISÃO DO PORT: o Save Keeper da Central Area (stagml12 n_digilfm 0, conversa n_digilfm_00.lua → saveshop.lua → stagIS12)
        // não aparece: o port grava sozinho (docs/decisoes-port-pc.md).
        if (m.Command == "GENERATE" && m.Name == "n_digilfm" && m.Index == 0 && _stageName.Equals("stagml12", StringComparison.OrdinalIgnoreCase)) return true;
        if (m.Command == "GENERATE")
        {
            if (m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } p)
            {
                var rot = m.Arg(1) is { Kind: 'V', Numbers.Length: 3 } r ? new Vector3((float)r.Numbers[0], (float)r.Numbers[1], (float)r.Numbers[2]) : Vector3.Zero;
                var spawned = Spawn(m.Name, m.Index, new Vector3((float)p.Numbers[0], (float)p.Numbers[1], (float)p.Numbers[2]), rot);
                if (spawned != null) MakeHittable(spawned);
                // 3º valor = sequência inicial (FATO, matb03.lua da stag0103: g_shelfz06 0/1 GENERATE "V<…>/V<…>/SQ_RSET" e "…/SQ_LSET",
                // que põem o rótulo DEFR/DEFL do lado da prateleira). Antes o port ignorava e as duas ficavam no lado R.
                if (spawned != null && m.Text(2) is { } first && first.StartsWith("SQ_", StringComparison.Ordinal)) spawned.SetSequence(first);
            }
            // FATO (bit_f_apocaly.lua, hit_g_b01cpole.lua): GENERATE "f_apocaly/I<0>/V<0,30,0>/V<0,90,0>/SQ_ENTR" = dono, índice
            // do dono, posição, rotação e sequência inicial. HIPÓTESE: posição no mundo (= a do chefe no pedestal; assim as caixas
            // 70069, com y −3..−6, alcançam o jogador no topo) e o objeto acompanha o dono dali em diante.
            else if (m.Text(0) is { } ownerName && _enemies.FirstOrDefault(e => e.Kind.Model == ownerName && !e.Dead) is { } owner
                && m.Arg(2) is { Kind: 'V', Numbers.Length: 3 } at)
            {
                var rot = m.Arg(3) is { Kind: 'V', Numbers.Length: 3 } r ? new Vector3((float)r.Numbers[0], (float)r.Numbers[1], (float)r.Numbers[2]) : Vector3.Zero;
                if (Spawn(m.Name, m.Index, new Vector3((float)at.Numbers[0], (float)at.Numbers[1], (float)at.Numbers[2]), rot) is { } child)
                {
                    child.Owner = owner;
                    child.Actor.Root.Reparent(owner.Body, keepGlobalTransform: true);
                    if (m.Text(4) is { } first && first.StartsWith("SQ_", StringComparison.Ordinal)) child.SetSequence(first);
                    MakeHittable(child);
                }
            }
            // FATO (hit_g_boxfield.lua da stag0001, 3ª etapa do tutorial): MODELCTRL g_tcounter00 GENERATE sem argumentos. O g_tcounter00
            // é o relógio da etapa: SQ_WAIT espera 180 quadros, SQ_FINISH faz LUACALL "ded" (ded_g_tcounter00.lua: demo "stageend",
            // apaga o que sobrou e START GENE-a03, a 4ª etapa). Sem posição o port não gerava nada e a etapa nunca acabava (relato
            // de 2026-11-02). HIPÓTESE: sem posição, nasce na origem.
            else if (m.Args.Count == 0) Spawn(m.Name, m.Index, Vector3.Zero, Vector3.Zero); // relógio: sem corpo para golpe
            return false;
        }
        var targets = _objects.Where(o => o.Name == m.Name && (m.Index < 0 || o.Index == m.Index)).ToList();
        foreach (var o in targets)
        {
            // O analisador separa os argumentos por "/": "SQ_WAIT/ON" chega como dois; SETLABEL usa o texto inteiro.
            if (m.Command == "SETLABEL" && m.RawArgs is { Length: > 0 } label) o.SetSequence(label);
            else if (m.Command.StartsWith("SQ_", StringComparison.Ordinal)) o.SetSequence(m.Command);
            // MOVETRANS "V<pos>/V<rot em graus>": põe o objeto no lugar e na direção (fim da conversa: volta à direção de antes).
            else if (m.Command == "MOVETRANS" && m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } mp)
            {
                o.Actor.Root.GlobalPosition = new Vector3((float)mp.Numbers[0], (float)mp.Numbers[1], (float)mp.Numbers[2]);
                if (m.Arg(1) is { Kind: 'V', Numbers.Length: 3 } mr) o.Actor.Root.RotationDegrees = new Vector3((float)mr.Numbers[0], (float)mr.Numbers[1], (float)mr.Numbers[2]);
            }
        }
        return false;
    }

    // Veículo (RideLand): a colisão do beHitLandModelInfo vira um corpo preso ao ator, e quem está dentro anda junto (CarryRiders).
    // FATO (g_boatz01): a peça é um igSwitch → beCopyTransform (matriz identidade) → caixa aberta com o chão em y 0 do barco.
    void MakeRideLand(WorldObject o, HudData data)
    {
        if (!data.Lands.TryGetValue(o.Name, out var land) || !RideLand.IsVehicle(land.Kind)) return;
        var shapes = new HudActor(data, land.Node, slot: 0);
        var body = new StaticBody3D { Name = $"chao_{o.Name}" };
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue), max = -min;
        foreach (var mi in FindAll<MeshInstance3D>(shapes.Root))
        {
            var faces = mi.Mesh.GetFaces();
            if (faces.Length < 3) continue;
            // Ordem dos vértices invertida: no IGB o chão do barco é anti-horário visto de cima, e a Godot toma o horário como
            // frente. Sem inverter, o chão olhava para baixo e o raio do SnapToFloor (só faces de frente) passava pelo barco e
            // punha o jogador no leito do rio, em y 1 (relato do usuário na 0.6.13, conferido sem janela com DW4_DIAG_BARCO).
            for (int i = 0; i + 2 < faces.Length; i += 3) (faces[i + 1], faces[i + 2]) = (faces[i + 2], faces[i + 1]);
            var trimesh = new ConcavePolygonShape3D { BackfaceCollision = true }; // paredes da caixa: os dois lados seguram
            trimesh.SetFaces(faces);
            var t = mi.GlobalTransformIn(shapes.Root);
            body.AddChild(new CollisionShape3D { Shape = trimesh, Transform = t });
            foreach (var v in faces) { var w = t * v; min = min.Min(w); max = max.Max(w); }
        }
        shapes.Root.Free();
        if (body.GetChildCount() == 0) { body.Free(); return; }
        o.Actor.Root.AddChild(body);
        o.Ride = body;
        o.RideMin = min;
        o.RideMax = max;
        if (DiagRide) GD.Print($"[barco] {o.Name}: chão de veículo com {body.GetChildCount()} peça(s), caixa {min.Round()}..{max.Round()}");
    }

    // DEPURAÇÃO: DW4_DIAG_BARCO=1 mostra o chão do veículo e, a cada 30 quadros, o jogador e o veículo.
    static readonly bool DiagRide = OS.GetEnvironment("DW4_DIAG_BARCO") == "1";
    void DiagRideTick()
    {
        if (!DiagRide || _tick % 30 != 0) return;
        var p0 = _player.GlobalPosition;
        var q = PhysicsRayQueryParameters3D.Create(new Vector3(p0.X, 60, p0.Z), new Vector3(p0.X, -20, p0.Z));
        q.Exclude = [_player.GetRid()];
        q.HitBackFaces = false; // como o SnapToFloor
        var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(q);
        GD.Print($"[barco] raio: {(hit.Count > 0 ? $"{((Vector3)hit["position"]).Round()} em {((Node)hit["collider"]).Name} normal {((Vector3)hit["normal"]).Round()}" : "nada")}");
        foreach (var o in _objects.Where(x => x.Brain != null && x.Name.StartsWith("g_", StringComparison.Ordinal) && x.Actor.Root.GlobalPosition.DistanceTo(_player.GlobalPosition) < 200))
            GD.Print($"[barco] torre {o.Name}/{o.Index} a {o.Actor.Root.GlobalPosition.DistanceTo(_player.GlobalPosition):0}: {o.Sequence} ação {o.Seq?.Action} vivo {o.Seq?.Live} IA {o.AiSequence ?? "-"}");
        foreach (var o in _objects.Where(x => x.Ride != null))
            GD.Print($"[barco] q{_tick} jogador {_player.GlobalPosition.Round()} chão {_player.IsOnFloor()} | {o.Name} {o.Actor.Root.GlobalPosition.Round()} giro {o.Actor.Root.GlobalRotationDegrees.Y:0} seq {o.Sequence} preso {o.Seq?.Motion.Attached != null}");
    }

    // Jogador dentro da caixa do veículo: vai com ele (posição e giro), antes do MoveAndSlide do quadro (TickObjects roda antes).
    void CarryRiders(Dictionary<WorldObject, Transform3D> before)
    {
        foreach (var (o, old) in before)
        {
            if (!IsInstanceValid(o.Actor.Root) || !o.Actor.Root.IsInsideTree()) continue;
            var now = o.Actor.Root.GlobalTransform;
            if (now.IsEqualApprox(old)) continue;
            var local = old.AffineInverse() * _player.GlobalPosition;
            if (!RideLand.Inside(new NVector3(local.X, local.Y, local.Z), new NVector3(o.RideMin.X, o.RideMin.Y, o.RideMin.Z), new NVector3(o.RideMax.X, o.RideMax.Y, o.RideMax.Z))) continue;
            _player.GlobalPosition = now * local;
            float yaw = now.Basis.GetEuler().Y - old.Basis.GetEuler().Y;
            if (Math.Abs(yaw) is > 1e-5f and < 1f) _move.Face(_move.Facing + yaw);
        }
    }

    void TickObjects(float dt)
    {
        var riding = new Dictionary<WorldObject, Transform3D>();
        foreach (var o in _objects)
            if (o.Ride != null && o.Actor.Root.IsInsideTree()) riding[o] = o.Actor.Root.GlobalTransform;
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            var o = _objects[i];
            o.Tick(dt);
            if (o.Seq?.Motion.Attached is { } rl && AttachTarget(rl, o.Name) is { } at) o.Actor.Root.GlobalTransform = at;
            UpdateObjectAi(o, dt);
            CheckObjectFlash(o);
            while (o.Seq?.TakeLuaCallWithName() is { } lua) RunLuaCall(lua.Name, o.Name, o, lua.Call); // LUACALL das peças f_* (ex.: f_devl1f2 "bmh")
            UpdateCharacterAnim(o);
            CheckFloorHit(o);
            if (!o.Erased) continue;
            o.Actor.Root.QueueFree();
            o.Body?.QueueFree();
            _objects.RemoveAt(i);
        }
        if (riding.Count > 0) CarryRiders(riding);
        DiagRideTick();
    }

    // NPCs do lobby (n_agumon, n_leomon…): o nó do ator no IGB é um igActor, então o desenho vem do personagem exportado
    // (dw4 fase-exportar → npcs<nome>…personagem.gltf). A animação segue a ACTION da sequência do objeto (MOTIONSET).
    static readonly bool DiagNpc = OS.GetEnvironment("DW4_DIAG_NPC") == "1";

    void AttachCharacter(WorldObject o)
    {
        var dir = Path.Combine(_dir, "npcs", o.Name);
        var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "personagem.gltf", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null) return;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(GameFiles.GodotPath(file), state) != Error.Ok || doc.GenerateScene(state) is not Node3D model) { _problems.Add($"{file}: erro ao ler"); return; }
        if (!FindAll<MeshInstance3D>(model).Any()) _problems.Add($"{o.Name}: personagem sem malha (pele igSkin sem igBlendMatrixSelect, formato ainda não lido)");
        o.Actor.Root.AddChild(model);
        o.CharacterAnim = FindAll<AnimationPlayer>(model).FirstOrDefault();
        // DEPURAÇÃO (DW4_DIAG_NPC=1): o que cada NPC tem e o que recebe (T-pose dos NPCs).
        if (DiagNpc) GD.Print($"[npc] {o.Name}/{o.Index}: AnimationPlayer {(o.CharacterAnim != null ? string.Join(",", o.CharacterAnim.GetAnimationList()) : "nenhum")}, esqueletos {FindAll<Skeleton3D>(model).Count()}");
        if (o.CharacterAnim is { } ca && o.Seq != null)
            o.Seq.MotionLength = m => ca.HasAnimation(m) ? (float)ca.GetAnimation(m).Length * ObjectSequencer.FramesPerSecond : null;
    }

    // Segue o movimento (MOTIONSET) da sequência, que continua nas ações sem MOTIONSET (DELAY01 depois do WALK01).
    void UpdateCharacterAnim(WorldObject o)
    {
        if (o.CharacterAnim is not { } anim || o.Seq is not { CurrentMotion: { } motion } seq || seq.MotionSerial == o.CharacterMotionSerial) return;
        o.CharacterMotionSerial = seq.MotionSerial;
        if (DiagNpc) GD.Print($"[npc] {o.Name}/{o.Index}: {seq.Sequence} pede {motion} (laço {seq.MotionLoop}) → {(anim.HasAnimation(motion) ? "toca" : "NÃO EXISTE")}");
        if (!anim.HasAnimation(motion)) return;
        anim.GetAnimation(motion).LoopMode = seq.MotionLoop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
        anim.Play(motion, seq.MotionBlend / ObjectSequencer.FramesPerSecond);
    }

    // Golpe físico do jogador contra as caixas: roda o hit_<nome>.lua no contexto do objeto (FATO: hit_g_box.lua tira 100
    // de HP se o golpe não for técnica 40040..40099 e põe SQ_DDMG); com HP ≤ 0, o gerador "zera" e roda o _callLua (gboxNN.lua).
    void CheckObjectHits()
    {
        if (_runner == null || _stage == null) return;
        foreach (var h in _runner.ActiveHitBoxes().Where(h => h.Type is 10000 or 20000))
        {
            var c = BoxCenter(h);
            foreach (var o in _objects.Where(o => o.Body != null && !o.Dead && !_objectsHitThisAction.Contains(o)).ToList())
            {
                var p = o.Actor.Root.GlobalPosition; // global: o f_bit fica preso ao chefe
                bool touch = o.BodyBoxes.Count > 0
                    ? HitBody.Hits(N(c), h.Size, _move.Facing, N(p), o.Actor.Root.GlobalRotation.Y, o.Actor.Root.GlobalBasis.Scale.X, o.BodyBoxes)
                    : !(Math.Abs(c.X - p.X) > h.Size.X / 2 + GboxSize.X / 2 || Math.Abs(c.Z - p.Z) > h.Size.Z / 2 + GboxSize.Z / 2
                        || c.Y + h.Size.Y / 2 < p.Y || c.Y - h.Size.Y / 2 > p.Y + GboxSize.Y);
                if (!touch) continue;
                _objectsHitThisAction.Add(o);
                StrikeObject(o, h.Type);
            }
        }
    }

    // Caixa centrada em c (tamanho size) contra o corpo do objeto: as caixas do HIT do objeto, ou a caixa da g_box.
    bool ObjectTouched(WorldObject o, Vector3 c, Vector3 size)
    {
        var p = o.Actor.Root.GlobalPosition;
        return o.BodyBoxes.Count > 0
            ? HitBody.Hits(N(c), N(size), _move.Facing, N(p), o.Actor.Root.GlobalRotation.Y, o.Actor.Root.GlobalBasis.Scale.X, o.BodyBoxes)
            : !(Math.Abs(c.X - p.X) > size.X / 2 + GboxSize.X / 2 || Math.Abs(c.Z - p.Z) > size.Z / 2 + GboxSize.Z / 2
                || c.Y + size.Y / 2 < p.Y || c.Y - size.Y / 2 > p.Y + GboxSize.Y);
    }

    // Golpe do jogador num objeto: o hit_<objeto>.lua tira o HP (MDLaddHp) e escolhe SQ_SDMG ou SQ_DDMG. FATO: o golpe carregado
    // (20000) vai direto ao SQ_DDMG sem mexer no HP; o objeto quebra por ele também.
    void StrikeObject(WorldObject o, int type)
    {
        o.BulletKind = type;
        var script = $"hit_{o.Name}.lua";
        if (_stage != null && _stage.Has(script)) _stage.RunAs(script, o);
        if (DiagNpc || DiagDrop) GD.Print($"[golpe-obj] {o.Name}/{o.Index} ({o.Gen?.Name ?? "sem gerador"}): tipo {type}, HP {o.Hp}, {o.Sequence}");
        if (o.Hp > 0 && o.Sequence != "SQ_DDMG") return;
        o.Dead = true;
        o.Body?.QueueFree();
        o.Body = null;
        ScriptLog($"{o.Gen?.Name}: {o.Name} destruída");
        if (o.Gen != null) DropAt(o.Gen, o.Actor.Root.GlobalPosition);
        GeneratedDied(o.Gen, o);
    }

    // IA de objeto (f_bit): parado em SQ_WAIT, o cérebro sorteia pelo mapa (MAP00: SQ_SMN01/02; MAP01, depois do SQ_PHASE2:
    // SQ_ATK01..15 e SQ_ATKROT, FATO pelos mapas) e a sequência roda até voltar ao SQ_WAIT (JUMP "SQ_WAIT" no fim de cada ataque).
    void UpdateObjectAi(WorldObject o, float dt)
    {
        if (o.Name.StartsWith("n_", StringComparison.Ordinal)) { UpdateNpcAi(o, dt); return; }
        if (o.Brain is not { } brain || o.Seq is not { } seq || o.Dead) return;
        if (seq.TakeAiMap() is { } map) { brain.SetMap(map); brain.Release(); o.AiSequence = null; }
        if (o.AiSequence != null)
        {
            if (seq.Sequence == o.AiSequence && seq.Running) return;
            brain.Release();
            o.AiSequence = null;
        }
        var p = o.Actor.Root.GlobalPosition;
        // Interruptor de portão: reage a qualquer membro da party na fase (Fatia.PartySwitches.cs).
        var t = o.Name == "g_sbutton" ? NearestMember(p) : _player.GlobalPosition;
        // HIPÓTESE (nome da ação: "homing"): a torreta ligada (SQ_WAIT no rótulo ON, ação HMNG01) vira para o jogador no máximo o
        // giro por quadro do mapa (0,0349 = 2°); FATO: o SQ_WAIT "ON" tem MOVE "Stop" e HMNG01, e o tiro sai para a frente (+z).
        if (o.Name.StartsWith("g_", StringComparison.Ordinal) && seq.Action == "HMNG01")
        {
            var root = o.Actor.Root;
            float want = MathF.Atan2(t.X - p.X, t.Z - p.Z);
            float diff = Mathf.Wrap(want - root.GlobalRotation.Y, -MathF.PI, MathF.PI);
            float step = brain.Map.TurnPerFrame * dt * SequenceRunner.FramesPerSecond;
            root.RotateY(Math.Clamp(diff, -step, step));
        }
        if (seq.Sequence != "SQ_WAIT") return; // entrada, saída e as sequências pedidas pelos scripts mandam
        // FATO (seq): DEF/ONLOOP sem CTRL "isLive". Sem isso, os interruptores do portão da Goblin Fortress (stag0101, g_sbutton)
        // reagiam sem as duas ID Passes e, pisando num já aceso (ONLOOP), o jogador sozinho o apagava (off_g_sbutton.lua) e o
        // portão nunca abria. O botão de masmorra (g_dbutton1, ON sem isLive) também rodava o hit_ de novo a cada pisada.
        if (o.Name.StartsWith("g_", StringComparison.Ordinal) && seq.HasLiveGate && !seq.Live) { brain.Release(); return; }
        brain.Tick(dt, new System.Numerics.Vector3(p.X, p.Y, p.Z), new System.Numerics.Vector3(t.X, t.Y, t.Z), o.Actor.Root.GlobalRotation.Y);
        if (brain.External is { } same && seq.OnlyJumpsBack(same)) { brain.Release(); return; }
        if (brain.External is { } ext)
        {
            if (DiagNpc) GD.Print($"[obj-ia] {o.Name}/{o.Index}: {ext} (estava em {seq.Sequence}, ação {seq.Action})");
            o.SetSequence(ext);
            o.AiSequence = ext;
        }
    }

    // Caixas que os objetos ligam no próprio SQ (FATO: g_ddamagefloor liga HITBOX 70112 (0,0,0) (17,1,17) no ciclo ON) contra a
    // caixa de corpo do Guilmon (5 × 7 × 5). FATO: eneatk 112 = TYPE 2, TSUYOSA 5 (5% do HP máximo, sem elemento).
    // HIPÓTESE: um acerto por caixa ligada; a caixa do objeto está em eixos do objeto (rotação Y aplicada).
    void CheckFloorHit(WorldObject o)
    {
        if (o.Seq == null || o.HitSerial == o.Seq.BoxSerial || _runner?.Invincible == true || _hp <= 0) return;
        var g = _player.GlobalPosition;
        var root = o.Actor.Root;
        foreach (var h in o.Seq.ActiveHitBoxes)
        {
            if (h.Type / 10000 != 7) continue; // 7xxxx: golpe contra o jogador (como o 70004 do Numemon)
            var c = root.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, root.GlobalRotation.Y);
            if (!HitsPlayer(c, h.Size, root.GlobalRotation.Y)) continue;
            o.HitSerial = o.Seq.BoxSerial;
            _hitsTaken++;
            // Com o hit_p_<forma>.lua da fase, ele decide (como no golpe de inimigo e no projétil). FATO (stag0103): o hit_p_*.lua do
            // barco não tira HP do jogador, só do barco (g_boathp); o portão de espinhos g_factgatez06 (eneatk 129) tirava 29 do
            // Dorumon e nada do barco porque este caminho ia direto ao EnemyStrikes (relato do usuário na 0.6.15).
            if (PlayerHitScript(h.Type, o.Owner?.Kind.Stats, o.Name, root.GlobalPosition)) return;
            string? reaction = EnemyStrikes(h.Type, o.Owner?.Kind.Stats, o.Name); // f_bit: os atributos do chefe dono
            if (reaction != null && _runner != null && (_runner.Start(reaction, StyleOf(reaction)) || _runner.Start(reaction, "common")))
            {
                _wazaPending = null;
                _move.Stop();
            }
            return;
        }
    }
}
