using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;
using NVector3 = System.Numerics.Vector3;

namespace DW4.Client;

/// <summary>
/// Vários inimigos, os geradores de onda (GENE-nn, GBOX-nn) e os gatilhos de área (mat*.lua) da dng1.
/// Dados de &lt;fatia&gt;, fora do repositório: geradores.json (dw4 igb-geradores ndmw.afs 184 &lt;fatia&gt;) e gatilhos.json
/// (dw4 igb-gatilhos ndmw.afs 179 &lt;fatia&gt;). Levantamento: docs/gameplay/fase4-objetos-dng1.md.
/// Sem esses arquivos, a fatia volta ao Numemon fixo do GENE-05 que renasce (decisão da fatia anterior).
/// </summary>
public partial class Fatia
{
    sealed class Enemy : IBeModel
    {
        public required CharacterBody3D Body { get; init; }
        public required EnemyKind Kind { get; init; }
        public Node3D? Model { get; init; }
        public AnimationPlayer? Anim { get; init; }
        /// <summary>Meia largura e altura do corpo para o golpe do jogador, pela caixa da malha (mínimo 4 × 14, o do Numemon).</summary>
        public float HitHalf { get; set; } = 4;
        /// <summary>Ação e quadro já vistos pelos disparadores (Fatia.Shots).</summary>
        public string? ShotAction { get; set; }
        public float ShotFrame { get; set; }
        /// <summary>Ação e quadro já vistos pelo pisca de dano (Fatia.Flash).</summary>
        public string? FlashAction { get; set; }
        public float FlashFrame { get; set; }
        /// <summary>LUACALL de evento armado (nome de chamada terminado em "Hit", ex. "wft" "WHit"): roda quando bater na parede.</summary>
        public (string Name, string Call)? HitLua { get; set; }
        /// <summary>CTRL "toWait" da sequência em andamento: a IA pode trocar de golpe enquanto ela roda (ver UpdateEnemy).</summary>
        public bool AiCanCut { get; set; }
        public float HitHeight { get; set; } = 14;
        public EnemyBrain? Brain { get; set; }
        public SequenceRunner? Runner { get; set; }
        /// <summary>Canais do MOVE/MOVEEX das sequências (FATO, executor do jogo; ver MotionChannels).</summary>
        public MotionChannels Motion { get; } = new();
        /// <summary>Subiu por um MOVEEX no eixo Y: sem gravidade até voltar ao chão (HIPÓTESE).</summary>
        public bool Airborne { get; set; }
        public float Yaw { get; set; }
        public bool Started { get; set; }
        public int Serial { get; set; }
        public HashSet<HitBox> HitThisAction { get; } = [];
        public int Hp { get; set; }
        public bool Dead { get; set; }
        public bool Killed { get; set; }
        /// <summary>Fatia sem geradores: renasce no ponto depois de 5 s (DECISÃO DA FATIA).</summary>
        public bool Respawns { get; init; }
        public float RespawnTimer { get; set; }
        public Vector3 Home { get; init; }
        public Generator? Gen { get; init; }
        /// <summary>Combate em grupo (Fatia.Coop.cs): número do inimigo no grupo; Proxy = cópia de um inimigo do anfitrião.</summary>
        public int NetId { get; set; }
        public bool Proxy { get; set; }
        public Vector3 NetPos { get; set; }
        public float NetYaw { get; set; }
        /// <summary>EXP e drop já dados por esta morte.</summary>
        public bool Rewarded { get; set; }
        public float ProxyKillWait { get; set; }
        /// <summary>Última vez (s) que a sincronia do anfitrião citou esta cópia.</summary>
        public double ProxySeen { get; set; }
        // IBeModel (hit_<modelo>.lua do chefe roda no contexto dele).
        public int Index => 0;
        public int BulletKind { get; set; }
        public int MaxHp => Kind.Stats?.MaxHp ?? 0;
        public string ModelName => Kind.Model;
        (double X, double Y, double Z) IBeModel.Position => (Body.GlobalPosition.X, Body.GlobalPosition.Y, Body.GlobalPosition.Z);
        double IBeModel.Yaw => Yaw;
        int IBeModel.RecNo => Kind.Row;
        string? IBeModel.SequenceName => Started && Runner?.Running == true ? Runner.Sequence : "SQ_WAIT";
        /// <summary>Rótulo guardado por sequência (SETLABEL "SQ_SDMG/common", LABELCTRL "FLOAT" "SQ_WAIT").</summary>
        public Dictionary<string, string> Labels { get; } = [];
        /// <summary>MDLsetSeq / MODELCTRL nome SQ_X: interrompe a IA com a sequência.</summary>
        public void SetSequence(string name)
        {
            if (Brain == null) return;
            Brain.Force(name);
            Started = false;
        }
    }

    /// <summary>
    /// Gerador (beGeneraterData). FATO: posições por vaga (ponto + deslocamento, ou globais), rotação por vaga, _geneCount
    /// (total) e _geneMaxCount (ao mesmo tempo) por número de jogadores, inimigo pela linha da tabela enemy, _callLua.
    /// HIPÓTESES (o tratador do GENERATER não foi lido): START ativa e repõe um por quadro até o máximo vivo e o total;
    /// STOP para de gerar e guarda o progresso; STOPREVERS para e some com os vivos (o progresso volta a zero);
    /// "wipeout" = o _callLua roda quando o total foi gerado e todos morreram.
    /// </summary>
    sealed class Generator
    {
        public required string Name { get; init; }
        public required Vector3[] Positions { get; init; }
        public required float[] RotYDeg { get; init; }
        /// <summary>_geneCount e _geneMaxCount por número de jogadores (1..4); vale a coluna do NINZU atual (GameRam.Ninzu).</summary>
        public required int[] CountBy { get; init; }
        public required int[] MaxBy { get; init; }
        public required Func<int> Players { get; init; }
        public int Count => ByPlayers(CountBy);
        public int MaxCount => ByPlayers(MaxBy);
        int ByPlayers(int[] v) => v.Length == 0 ? 0 : v[Math.Clamp(Players(), 1, v.Length) - 1];
        public int EnemyRow { get; init; }
        public string? CallLua { get; init; }
        /// <summary>_startModeStr ("direct", "radar", "timer"…), _startData e _genePoint.</summary>
        public string StartMode { get; init; } = "direct";
        public float StartData { get; init; }
        public Vector3 Point { get; init; }
        /// <summary>Ligado (START, ou sozinho quando nenhum script da fase cita o gerador) mas esperando o modo de início.</summary>
        public bool Armed { get; set; }
        public bool Active { get; set; }
        public int Spawned { get; set; }
        public bool Finished { get; set; }
        public List<object> Alive { get; } = [];
    }

    sealed class MatVolume
    {
        public required string Name { get; init; }
        public Vector3 Min { get; init; }
        public Vector3 Max { get; init; }
        public bool Run { get; set; } = true;
        public bool Inside { get; set; }
    }

    /// <summary>
    /// Tipo de inimigo (modelo da coluna MODEL da tabela enemy): cena, sequências, ações, mapas de IA e o lutador da linha.
    /// Carregado uma vez por modelo; cada inimigo gera a própria cena a partir dele.
    /// </summary>
    sealed class EnemyKind
    {
        public required string Model { get; init; }
        public required int Row { get; init; }
        public GltfDocument? Doc { get; init; }
        public GltfState? State { get; init; }
        /// <summary>Inimigo sem esqueleto (ex.: f_luce2): nó do ator no objetos.json usado como modelo; −1 = personagem glTF.</summary>
        public int RigidRoot { get; init; } = -1;
        public Fighter? Stats { get; init; }
        public string? Json { get; init; }
        public Dictionary<string, List<SeqCommand>> Sequences { get; init; } = [];
        public Dictionary<string, List<SeqCommand>> Actions { get; init; } = [];
        /// <summary>Caixas do corpo (beModelCtrlInfoDataHit, FATO); vazio = caixa pela malha (HitHalf/HitHeight).</summary>
        public List<(NVector3 Min, NVector3 Max)> BodyBoxes { get; init; } = [];
        public string? Wait { get; set; }
        public string? Run { get; set; }
        public string? Hurt { get; set; }
        public float Walk { get; init; } = 0.24f;
        public string Name => Model;
    }

    readonly List<Enemy> _enemies = [];
    readonly HashSet<Enemy> _enemiesHitThisAction = [];
    readonly Dictionary<string, Generator> _generators = [];
    readonly List<MatVolume> _volumes = [];
    readonly Dictionary<int, EnemyKind?> _enemyKinds = [];
    Enemy? _lastEnemy;

    // Pasta do modelo: <fase>\inimigos\<modelo> (dw4 fase-exportar); a fatia antiga tinha <fatia>\numemon.
    string? EnemyDir(string model)
    {
        var dir = Path.Combine(_dir, "inimigos", model);
        if (GameFiles.DirExists(dir)) return dir;
        // FATO (varredura 2026-10-25): alguns campos usam inimigos cujo IGB não está entre os da fase (e_mummy na stag0204,
        // e_mecha3/e_megadra na stag0403). HIPÓTESE: o modelo é o mesmo exportado em outra fase; usa a primeira que tiver.
        var stagesRoot = Path.GetDirectoryName(_dir);
        if (stagesRoot != null && GameFiles.DirExists(stagesRoot))
            foreach (var other in GameFiles.EnumerateDirectories(stagesRoot).OrderBy(d => d, StringComparer.Ordinal))
                if (GameFiles.DirExists(Path.Combine(other, "inimigos", model))) return Path.Combine(other, "inimigos", model);
        dir = Path.Combine(_dir, "numemon");
        return model == "e_nume" && GameFiles.DirExists(dir) ? dir : null;
    }

    EnemyKind? Kind(int row)
    {
        if (_enemyKinds.TryGetValue(row, out var known)) return known;
        string model = _db != null && _db.Contains("enemy") ? _db["enemy"].Text(row, "MODEL") : "e_nume";
        // Mesmo modelo em outra linha (ex.: e_nume 12/13/636): cena e sequências iguais, lutador da linha.
        var same = _enemyKinds.Values.FirstOrDefault(k => k?.Model == model);
        EnemyKind? kind = null;
        if (same != null) kind = new EnemyKind
        {
            Model = model, Row = row, Doc = same.Doc, State = same.State, RigidRoot = same.RigidRoot, Stats = _db?.Enemy(row), Json = same.Json,
            Sequences = same.Sequences, Actions = same.Actions, Wait = same.Wait, Run = same.Run, Hurt = same.Hurt, Walk = same.Walk,
        };
        else kind = LoadEnemyKind(model, row);
        _enemyKinds[row] = kind;
        return kind;
    }

    EnemyKind? LoadEnemyKind(string model, int row)
    {
        var root = EnemyDir(model);
        var file = root != null ? GameFiles.EnumerateFiles(root, "personagem.gltf", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null) return LoadRigidEnemyKind(model, row);
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(GameFiles.GodotPath(file), state) != Error.Ok) { _problems.Add($"{file}: erro ao ler"); return null; }
        var seqFile = GameFiles.EnumerateFiles(root!, "*_seq.json", SearchOption.AllDirectories).FirstOrDefault();
        string? json = seqFile != null ? GameFiles.ReadAllText(seqFile) : null;
        if (json == null) _problems.Add($"falta o *_seq.json de {model}: sem IA");
        var seqs = json != null ? SeqCommand.LoadAll(json) : [];
        var acts = json != null ? SeqCommand.LoadActions(json) : [];
        if (json != null && AiMap.LoadAll(json).Count == 0) _problems.Add($"o *_seq.json de {model} não tem mapas de IA");
        float walk = 0.24f;
        // Parado e andando: SQ_WAIT → ação → MOTIONSET; SQ_WALK → ação e MOVE "Move" n.
        if (seqs.TryGetValue("SQ_WALK", out var w) && w.FirstOrDefault(c => c.Kind == "MOVE" && c.S(0) == "Move") is { } mv) walk = mv.F(1);
        return new EnemyKind
        {
            Model = model, Row = row, Doc = doc, State = state, Stats = _db?.Enemy(row), Json = json, Sequences = seqs, Actions = acts, Walk = walk,
            BodyBoxes = json != null ? HitBody.Load(json) : [],
            Wait = ActionAnimations.FirstAction(seqs, "SQ_WAIT", null) is { } wa ? ActionAnimations.Motion(acts, wa) : null,
            Run = ActionAnimations.FirstAction(seqs, "SQ_WALK", null) is { } ra ? ActionAnimations.Motion(acts, ra) : null,
        };
    }

    // Inimigo sem esqueleto (exportado como objeto rígido: objetos.json + objetos\<modelo>\*_seq.json).
    EnemyKind? LoadRigidEnemyKind(string model, int row)
    {
        if (ObjectData() is not { } data || !data.Actors.TryGetValue(model, out int root))
        {
            _problems.Add($"falta inimigos\\{model}\\personagem.gltf (dw4 fase-exportar)");
            return null;
        }
        var seqDir = Path.Combine(_dir, "objetos", model);
        var seqFile = GameFiles.DirExists(seqDir) ? GameFiles.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        string? json = seqFile != null ? GameFiles.ReadAllText(seqFile) : null;
        return new EnemyKind
        {
            Model = model, Row = row, RigidRoot = root, Stats = _db?.Enemy(row), Json = json,
            Sequences = json != null ? SeqCommand.LoadAll(json) : [], Actions = json != null ? SeqCommand.LoadActions(json) : [],
            BodyBoxes = json != null ? HitBody.Load(json) : [],
        };
    }

    Enemy? SpawnEnemy(int row, Vector3 pos, float yaw, Generator? gen, bool respawns = false, int netId = 0)
    {
        if (Kind(row) is not { } kind) return null;
        Node3D? model = kind.RigidRoot >= 0 && ObjectData() is { } od ? new HudActor(od, kind.RigidRoot, slot: 0).Root
            : kind.Doc?.GenerateScene(kind.State!) as Node3D;
        if (model == null) return null;
        if (netId == 0) pos = FreeSpawnPoint(pos, gen);
        // Caixa de corpo do Numemon (igAABox do HIT): 8 × 14 × 8, FATO; HIPÓTESE: a mesma para os outros até ler o HIT de cada um.
        var body = new CharacterBody3D { Position = pos, FloorSnapLength = 4f, CollisionMask = 1 | CollisionKinds.EdgeLayer | CollisionKinds.EnemyLayer };
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 4f, Height = 14f }, Position = new Vector3(0, 7, 0) });
        body.AddChild(model);
        model.Rotation = new Vector3(0, yaw, 0);
        AddChild(body);
        var e = new Enemy { Body = body, Model = model, Kind = kind, Anim = FindAll<AnimationPlayer>(model).FirstOrDefault(), Yaw = yaw, Hp = kind.Stats?.MaxHp ?? 0, Gen = gen, Home = pos, Respawns = respawns };
        if (kind.Json != null)
        {
            e.Runner = new SequenceRunner(kind.Sequences, a => EnemyAnimFrames(e, a)) { CarryHitBoxes = true };
            e.Runner.Command = c => EnemyCommand(e, c);
            e.Runner.MoveStarted = m => e.Motion.Apply(m);
            var maps = AiMap.LoadAll(kind.Json);
            if (maps.Count > 0) e.Brain = new EnemyBrain(maps, kind.Sequences, () => _rng.Next(16));
            // FATO (e_goburi, f_luce, f_belvam): o SQ_ENTR é a entrada (TRNS01, MUTEKI) e põe o AIMAP inicial (MAP00/MAP02) e os
            // rótulos (LABELCTRL). HIPÓTESE: todo inimigo nasce por ele; sem isso o f_luce começava no 1º mapa do arquivo (MAP08,
            // mísseis) e nunca chegava às fases de voo e pouso.
            if (e.Brain != null && kind.Sequences.ContainsKey("SQ_ENTR")) e.Brain.Force("SQ_ENTR");
        }
        if (e.Anim != null)
        {
            var names = e.Anim.GetAnimationList();
            foreach (var n in names) TrimWrapSample(e.Anim.GetAnimation(n));
            kind.Wait ??= Pick(names, "wait01", "wait");
            kind.Hurt ??= Pick(names, "Sdmg01", "dmg");
            foreach (var n in new[] { kind.Wait, kind.Run })
                if (n != null && e.Anim.HasAnimation(n)) e.Anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
            if (kind.Wait != null && e.Anim.HasAnimation(kind.Wait)) e.Anim.Play(kind.Wait);
        }
        // Tamanho do corpo pela caixa da malha no espaço do modelo (escala do nó incluída). HIPÓTESE: a malha em repouso aproxima o
        // corpo de colisão do jogo; os chefes grandes (f_belvam) passam muito dos 8 × 14 × 8 do Numemon.
        var boxes = FindAll<MeshInstance3D>(model).Where(mi => mi.Mesh != null).Select(mi => model.GlobalTransform.AffineInverse() * mi.GlobalTransform * mi.Mesh.GetAabb()).ToList();
        if (boxes.Count > 0)
        {
            var all = boxes.Aggregate((a, b) => a.Merge(b));
            var s = model.Scale;
            e.HitHalf = Math.Max(4, Math.Min(all.Size.X * s.X, all.Size.Z * s.Z) * 0.35f);
            e.HitHeight = Math.Max(14, all.Size.Y * s.Y);
        }
        // Combate em grupo: netId ≠ 0 = cópia de um inimigo do anfitrião; senão, um número novo e o aviso ao grupo.
        (e.NetId, e.Proxy, e.NetPos, e.NetYaw) = (netId != 0 ? netId : NewNetId(), netId != 0, pos, yaw);
        _byNetId[e.NetId] = e;
        _enemies.Add(e);
        SendSpawn(e);
        return e;
    }

    // FATO (stag0104, geradores.json do #637): GENE-07/08 (ondas que ligam os anéis do portão, gene07/08.lua) usam as mesmas vagas
    // dos avulsos GENE-09..016 (ex.: GENE-07 vaga 1 = GENE-09 em (135, 120, -235)). Os dois nasciam no mesmo ponto, as cápsulas se
    // empurravam para dentro do chão e os dois caíam para sempre: a onda nunca acabava e o portão não abria (relato 2026-11-06).
    // DECISÃO DO PORT: quem nasceria dentro de outro inimigo vivo nasce ao lado (2 corpos de distância, direção fixa pelo gerador).
    Vector3 FreeSpawnPoint(Vector3 pos, Generator? gen)
    {
        const float Gap = 9f; // 2 × raio 4 da cápsula + folga
        bool Taken(Vector3 p) => _enemies.Any(o => !o.Dead && Mathf.Abs(o.Body.GlobalPosition.Y - p.Y) < 14
            && new Vector2(o.Body.GlobalPosition.X - p.X, o.Body.GlobalPosition.Z - p.Z).Length() < Gap);
        if (!Taken(pos)) return pos;
        float start = gen != null ? (gen.Name.Aggregate(0, (h, c) => h * 31 + c) & 7) * MathF.PI / 4 : 0;
        for (int ring = 1; ring <= 2; ring++)
            for (int k = 0; k < 8; k++)
            {
                float a = start + k * MathF.PI / 4;
                var p = pos + new Vector3(MathF.Sin(a), 0, MathF.Cos(a)) * Gap * ring;
                if (!Taken(p)) return p;
            }
        return pos;
    }

    // Tira a amostra final que repete a primeira pose (AnimationWrap): sem isso a trnsIN acabava com o inimigo embaixo da terra.
    static void TrimWrapSample(Animation a)
    {
        if (a.HasMeta("dw4_wrap")) return;
        a.SetMeta("dw4_wrap", true);
        bool jump = false;
        double end = -1;
        for (int t = 0; t < a.GetTrackCount(); t++)
        {
            int k = a.TrackGetKeyCount(t);
            if (k < 3) continue;
            double prevTime = a.TrackGetKeyTime(t, k - 2), lastTime = a.TrackGetKeyTime(t, k - 1);
            if (!AnimationWrap.IsClosingGap(prevTime, lastTime) || Math.Abs(lastTime - a.Length) > 1e-3) continue;
            end = Math.Max(end, prevTime);
            jump |= a.TrackGetType(t) switch
            {
                Animation.TrackType.Position3D => AnimationWrap.IsWrapJump(N(a.TrackGetKeyValue(t, 0).AsVector3()),
                    N(a.TrackGetKeyValue(t, k - 2).AsVector3()), N(a.TrackGetKeyValue(t, k - 1).AsVector3())),
                Animation.TrackType.Rotation3D => AnimationWrap.IsWrapJump(Q(a.TrackGetKeyValue(t, 0).AsQuaternion()),
                    Q(a.TrackGetKeyValue(t, k - 2).AsQuaternion()), Q(a.TrackGetKeyValue(t, k - 1).AsQuaternion())),
                _ => false,
            };
        }
        if (!jump || end <= 0) return;
        for (int t = 0; t < a.GetTrackCount(); t++)
            for (int k = a.TrackGetKeyCount(t) - 1; k >= 0 && a.TrackGetKeyTime(t, k) > end + 1e-4; k--)
                a.TrackRemoveKey(t, k);
        a.Length = (float)end;
        static System.Numerics.Quaternion Q(Quaternion q) => new(q.X, q.Y, q.Z, q.W);
    }

    string? EnemyAnimName(Enemy e, string action) =>
        ActionAnimations.Motion(e.Kind.Actions, action) is { } motion ? e.Anim?.GetAnimationList().FirstOrDefault(n => n == motion) : null;

    // Bloco do rótulo guardado (LABELCTRL/SETLABEL) de uma sequência: a ACTION e o MOVE "Move" dele. Sem rótulo guardado: nada (usa o padrão).
    static (string? Action, float? Move) IdleBlock(Enemy e, string seq)
    {
        if (!e.Labels.TryGetValue(seq, out var label) || !e.Kind.Sequences.TryGetValue(seq, out var cmds)) return (null, null);
        int at = cmds.FindIndex(c => c.Kind == "LABEL" && c.S(0) == label);
        if (at < 0) return (null, null);
        var block = cmds.Skip(at + 1).TakeWhile(c => c.Kind != "LABEL").ToList();
        return (block.FirstOrDefault(c => c.Kind == "ACTION")?.S(0), block.FirstOrDefault(c => c.Kind == "MOVE" && c.S(0) == "Move")?.F(1));
    }

    // FATO (e_blosso): SQ_AGRDMG faz LABELCTRL "SWON" "SQ_WAIT" e JUMP "SQ_LDMG"; o SQ_LDMG volta com JUMP "SQ_WAIT", e o bloco SWON do
    // SQ_WAIT é só JUMP "SQ_SWON" (fúria: MAP01, rótulos ANGER, giro). A IA do cliente não roda o SQ_WAIT do jogo, então o salto do
    // rótulo guardado vem daqui: o bloco do rótulo cujo primeiro comando é JUMP para outra sequência.
    static string? LabelJump(Enemy e, string seq)
    {
        if (!e.Labels.TryGetValue(seq, out var label) || !e.Kind.Sequences.TryGetValue(seq, out var cmds)) return null;
        int at = cmds.FindIndex(c => c.Kind == "LABEL" && c.S(0) == label);
        if (at < 0 || at + 1 >= cmds.Count || cmds[at + 1].Kind != "JUMP") return null;
        var to = cmds[at + 1].S(0);
        return to != seq && e.Kind.Sequences.ContainsKey(to) ? to : null;
    }

    float? EnemyAnimFrames(Enemy e, string action) => ActionAnimations.Length(e.Kind.Actions, action, motion =>
        e.Anim != null && e.Anim.HasAnimation(motion) ? (float)e.Anim.GetAnimation(motion).Length * SequenceRunner.FramesPerSecond : null);

    bool Vulnerable(Enemy e) => !e.Dead && !e.Killed && !(e.Started && e.Runner?.Invincible == true);

    // ---------- geradores ----------

    void LoadStageData()
    {
        var genFile = Path.Combine(_dir, "geradores.json");
        if (GameFiles.Exists(genFile))
        {
            using var doc = JsonDocument.Parse(GameFiles.ReadAllText(genFile));
            foreach (var g in doc.RootElement.GetProperty("generators").EnumerateArray())
            {
                static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
                // Contagens por número de jogadores (1..4): no online, o grupo de combate (Fatia.Coop.cs) põe o NINZU.
                var gen = new Generator
                {
                    Name = g.GetProperty("name").GetString()!,
                    Positions = g.GetProperty("positions").EnumerateArray().Select(V).ToArray(),
                    RotYDeg = g.GetProperty("rotYDeg").EnumerateArray().Select(x => x.GetSingle()).ToArray(),
                    CountBy = g.GetProperty("count").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                    MaxBy = g.GetProperty("maxCount").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                    Players = () => Ram.Ninzu,
                    EnemyRow = g.GetProperty("enemyRow").GetInt32(),
                    CallLua = g.TryGetProperty("callLua", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() : null,
                    StartMode = g.TryGetProperty("startMode", out var sm) ? sm.GetString() ?? "direct" : "direct",
                    StartData = g.TryGetProperty("startData", out var sd) ? sd.GetSingle() : 0,
                    // Sem "point" (exportação antiga): a primeira vaga (FATO: igual ao ponto nos geradores de posição relativa da stag0101).
                    Point = g.TryGetProperty("point", out var pt) ? V(pt) : g.GetProperty("positions").EnumerateArray().Select(V).FirstOrDefault(),
                };
                _generators[gen.Name] = gen;
            }
            ArmUnreferencedGenerators();
        }
        else if (_dir == _common) _problems.Add("falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo");
        var matFile = Path.Combine(_dir, "gatilhos.json");
        if (GameFiles.Exists(matFile))
        {
            using var doc = JsonDocument.Parse(GameFiles.ReadAllText(matFile));
            foreach (var v in doc.RootElement.GetProperty("volumes").EnumerateArray())
            {
                var mn = v.GetProperty("min");
                var mx = v.GetProperty("max");
                _volumes.Add(new MatVolume
                {
                    Name = v.GetProperty("name").GetString()!,
                    Min = new Vector3(mn[0].GetSingle(), mn[1].GetSingle(), mn[2].GetSingle()),
                    Max = new Vector3(mx[0].GetSingle(), mx[1].GetSingle(), mx[2].GetSingle()),
                });
            }
        }
        else _problems.Add("falta gatilhos.json (dw4 igb-gatilhos ndmw.afs 179 <fatia>): sem gatilhos de área");
    }

    // FATO (main.dol 0x802FD658..: tratador do GENERATER com START, STOP, STOPREVERS, RESET, RESTART, PERFECTDROP, ONFREEZE,
    // OFFREEZE; 0x802FCF80: a atualização só roda com o byte +0x14 ligado, que o START liga). Na stag0101 nenhum script cita
    // GENE-00..13, GENE-17/18/20/23..29/31..52 nem os barris e caixotes GBRL/GWCS, e o jogo tem Goburimons e caixas ali (relato do
    // usuário, 2026-11-02); os citados são as ondas de sala (dng1: matg01..07 → START) e os 16 épicos de um mesmo ponto, um por
    // forma (first.lua). FATO (stag0101): quase todo gerador "direct" de 1 inimigo sem script fica exatamente numa vaga de um
    // gerador "radar" (GENE-01..05 nas vagas do GENE-00, GENE-11..13 nas do GENE-10…); ligar todos dava 40 Goburimons vivos de
    // uma vez. HIPÓTESE: nasce ligado o gerador sem script que é "radar" (grupo que aparece quando o jogador chega perto) ou que
    // gera objeto (barris e caixotes g_*); os "direct" de inimigo sem script ficam desligados (alternativas por vaga, uso não
    // achado). Onde o byte +0x14 começa ligado não foi achado no código nativo.
    void ArmUnreferencedGenerators()
    {
        var luaDir = Path.Combine(_dir, "lua");
        if (!GameFiles.DirExists(luaDir)) return;
        var cited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in GameFiles.EnumerateFiles(luaDir, "*.lua"))
            foreach (System.Text.RegularExpressions.Match s in System.Text.RegularExpressions.Regex.Matches(
                System.Text.Encoding.Latin1.GetString(GameFiles.ReadAllBytes(file)), @"[ -~]{3,}"))
                cited.Add(s.Value);
        foreach (var g in _generators.Values.Where(g => !cited.Contains(g.Name)))
        {
            string model = _db != null && _db.Contains("enemy") ? _db["enemy"].Text(g.EnemyRow, "MODEL") : "";
            if (g.StartMode.StartsWith('r') || model.StartsWith("g_", StringComparison.Ordinal)) g.Armed = true;
        }
    }

    // Modo de início de um gerador ligado. FATO (0x802FD1E0..0x802FD2C8): 'd' (direct) começa já; 'r' (radar) quando a distância²
    // do jogador mais perto ao _genePoint fica ≤ _startData (3025 = 55²); 't' (timer) pelo BOOT_TIMER, aqui começa já (HIPÓTESE).
    void TickGeneratorStart()
    {
        foreach (var g in _generators.Values.Where(g => g.Armed && !g.Active && !g.Finished))
        {
            if (g.StartMode.StartsWith('r') && NearestMember(g.Point).DistanceSquaredTo(g.Point) > g.StartData) continue;
            g.Armed = false;
            g.Active = true;
            ScriptLog($"{g.Name}: começou ({g.StartMode})");
        }
    }

    bool DeliverToGenerators(BeMessage m)
    {
        if (m.Class != "GENERATER" || m.Text(0) is not { } name || !_generators.TryGetValue(name, out var g)) return false;
        switch (m.Command)
        {
            case "START":
                // Depois de um STOP (pausa), volta a gerar; na primeira vez, espera o modo de início (direct/radar).
                if (!g.Finished) { if (g.Spawned > 0) g.Active = true; else g.Armed = true; }
                break;
            case "STOP":
                g.Active = false;
                g.Armed = false;
                break;
            // FATO (en2_f_luce.lua: GENERATER RESTART "GENE-01" gera a f_luce2, a peça que derruba o chefe 3). HIPÓTESE: recomeça a
            // contagem e liga de novo, mesmo depois de terminado.
            case "RESTART":
                g.Finished = false;
                g.Spawned = g.Alive.Count;
                g.Active = true;
                break;
            case "STOPREVERS":
                g.Active = false;
                foreach (var o in g.Alive.ToList()) Despawn(o);
                g.Alive.Clear();
                if (!g.Finished) g.Spawned = 0;
                break;
        }
        return true;
    }

    void Despawn(object o)
    {
        if (o is Enemy e) { CoopRemoved(e); RemoveEnemy(e); }
        else if (o is WorldObject w) { w.Dead = true; w.Body?.QueueFree(); w.Actor.Root.QueueFree(); _objects.Remove(w); }
    }

    // Morte de algo gerado: sai da lista do gerador; com o total gerado e ninguém vivo, roda o _callLua ("wipeout").
    void GeneratedDied(Generator? g, object o)
    {
        if (g == null || !g.Alive.Remove(o)) return;
        if (_coopGuest) return; // no grupo, quem vence a onda é o anfitrião (Wipeout)
        if (g.Spawned < g.Count || g.Alive.Count > 0 || g.Finished) return;
        g.Finished = true;
        g.Active = false;
        ScriptLog($"{g.Name}: onda vencida ({g.CallLua ?? "sem script"})");
        CoopWipeout(g);
        if (g.CallLua != null && _stage?.Has(g.CallLua) == true) _stage.Run(g.CallLua);
    }

    void TickGenerators()
    {
        TickGeneratorStart();
        foreach (var g in _generators.Values.Where(g => g.Active))
        {
            if (g.Spawned >= g.Count || g.Alive.Count >= g.MaxCount || g.Positions.Length == 0) continue;
            int slot = g.Spawned % g.Positions.Length;
            var pos = g.Positions[slot];
            float rot = g.RotYDeg.Length > slot ? g.RotYDeg[slot] : 0;
            string model = _db != null && _db.Contains("enemy") ? _db["enemy"].Text(g.EnemyRow, "MODEL") : "";
            // Combate em grupo: inimigo quem gera é o anfitrião; caixas e barris (g_*) continuam de cada um.
            if (!model.StartsWith("g_", StringComparison.Ordinal) && CoopWaitsForHost) continue;
            object? spawned = model.StartsWith("g_", StringComparison.Ordinal)
                ? SpawnBox(model, pos, rot, g)
                : SpawnEnemy(g.EnemyRow, pos, rot * MathF.PI / 180, g);
            if (spawned == null) { g.Active = false; ScriptLog($"{g.Name}: não sei gerar {model} (linha {g.EnemyRow})"); continue; }
            g.Spawned++;
            g.Alive.Add(spawned);
            if (DiagNpc) GD.Print($"[gerador] {g.Name}: {model} ({g.Spawned}/{g.Count}) em {pos}");
        }
    }

    // ---------- gatilhos de área ----------

    void TickVolumes()
    {
        if (_stage == null || _leaving) return;
        if (_snapToFloor || _volumeGrace > 0) { if (!_snapToFloor) _volumeGrace--; return; }
        var p = _player.GlobalPosition;
        foreach (var v in _volumes)
        {
            bool inside = p.X >= v.Min.X && p.X <= v.Max.X && p.Y >= v.Min.Y && p.Y <= v.Max.Y && p.Z >= v.Min.Z && p.Z <= v.Max.Z;
            if (inside == v.Inside) continue;
            v.Inside = inside;
            if (!v.Run) continue;
            var script = v.Name + ".lua";
            ScriptLog($"{(inside ? "entrou em" : "saiu de")} {v.Name}");
            if (_stage.Has(script)) _stage.RunMat(script, new BeMatEvent(inside, inside, 0, run => v.Run = run));
        }
    }

    // ---------- comportamento de cada inimigo ----------

    void UpdateEnemies(float dt)
    {
        foreach (var e in _enemies.ToList())
            if (!_frozenModels.Contains(e.Kind.Model)) UpdateEnemy(e, dt);
        TickBossBarModel();
    }

    void UpdateEnemy(Enemy e, float dt)
    {
        CheckEnemyFlash(e);
        // Relato do usuário (2026-11-05): parado embaixo de um inimigo que morria, o jogador ficava preso no corpo dele durante a
        // animação de queda. DECISÃO DO PORT: com o HP em 0 (ou já derrotado) o corpo sai da camada que o jogador enxerga; o
        // inimigo continua no chão (a máscara dele não muda). Volta ao renascer.
        bool down = e.Killed || e.Dead || (e.Kind.Stats?.MaxHp > 0 && e.Hp <= 0);
        e.Body.CollisionLayer = down ? 0u : 1u;
        if (e.Proxy) { UpdateProxy(e, dt); return; }
        if (e.Dead)
        {
            if (!e.Respawns) return;
            // DECISÃO DA FATIA (sem geradores): o Numemon volta 5 s depois, no ponto, para continuar testando.
            e.RespawnTimer -= dt;
            if (e.RespawnTimer > 0) return;
            e.Dead = false;
            e.Hp = e.Kind.Stats?.MaxHp ?? 0;
            e.Killed = false;
            e.Body.Position = e.Home;
            e.Body.Visible = true;
            e.Body.ProcessMode = ProcessModeEnum.Inherit;
            e.Brain?.Release();
        }
        var vel = Vector3.Zero;
        var forward = new Vector3(MathF.Sin(e.Yaw), 0, MathF.Cos(e.Yaw));
        float Speed(float move) => _move.MaxSpeed * move / GuilmonMove;
        if (e.Brain != null && e.Runner != null)
        {
            var p = e.Body.GlobalPosition;
            var t = NearestMember(p); // no grupo, o membro mais perto (sozinho, o jogador)
            e.Brain.Tick(dt, new NVector3(p.X, p.Y, p.Z), new NVector3(t.X, t.Y, t.Z), e.Yaw);
            e.Yaw = e.Brain.Yaw;
            forward = new Vector3(MathF.Sin(e.Yaw), 0, MathF.Cos(e.Yaw));
            if (e.Brain.External is { } ext)
            {
                // Sequência do jogo (SQ_ATCK1, SQ_FIRE, SQ_SDMG…); "common" é o bloco sem elemento das de dano.
                if (!e.Started)
                {
                    e.Started = StartEnemySequence(e, ext);
                    e.Serial = -1;
                    if (!e.Started) e.Brain.Release();
                }
                else e.Runner.Tick(dt, false);
                // FATO (e_mecha4 SQ_ATCK2/SQ_ATCK4): depois do recuo vem CTRL "toWait", AIMAP MAP01/MAP02 e uma corrida sem fim (ACTN02/ACTN06
                // sem WCHECK); o MAP01 escolhe SQ_ATCK4 de perto. HIPÓTESE: "toWait" deixa a IA do mapa atual cortar a sequência com um golpe
                // diferente dela (passeio e espera não cortam); sem isso o SQ_ATCK4 e o SQ_SWOF nunca saíam.
                if (e.Started && e.AiCanCut && e.Runner.Running && e.Hp > 0)
                {
                    var to = _player.GlobalPosition - e.Body.GlobalPosition;
                    if (e.Brain.Map.Pick(to.X * to.X + to.Z * to.Z, _rng.Next(16)) is { } cut && cut.Sequence != e.Runner.Sequence
                        && cut.Sequence is not ("SQ_WAIT" or "SQ_WALK" or "SQ_SILENT") && e.Kind.Sequences.ContainsKey(cut.Sequence))
                    {
                        e.Yaw = MathF.Atan2(to.X, to.Z) + cut.Angle;
                        if (StartEnemySequence(e, cut.Sequence)) { e.Brain.Force(cut.Sequence); e.Serial = -1; }
                    }
                }
                // FATO (f_luce SQ_BOMBn → JUMP "SQ_ATCKOF", que põe o MAP02 e leva ao voo/pouso): JUMP para outra sequência continua
                // nela; só SQ_WAIT (e SQ_ERAS) devolvem o inimigo à IA. Com HP 0, só segue para a queda (FATO, e_blosso: SQ_DDMG/common →
                // JUMP "SQ_DFORMC", rótulo BOSS do SQ_ENTR, LUACALL "ded" → ded_e_blosso.lua abre as portas; antes o chefe sumia sem isso).
                if (e.Started && !e.Runner.Running && e.Runner.Next is { } next && next is not ("SQ_WAIT" or "SQ_ERAS") && e.Kind.Sequences.ContainsKey(next)
                    && (e.Hp > 0 || next.StartsWith("SQ_DFORM", StringComparison.Ordinal)) && StartEnemySequence(e, next))
                {
                    e.Brain.Force(next);
                    e.Serial = -1;
                }
                else if (e.Started && !e.Runner.Running && e.Hp > 0 && e.Runner.Next == "SQ_WAIT" && LabelJump(e, "SQ_WAIT") is { } jump
                    && StartEnemySequence(e, jump))
                {
                    e.Brain.Force(jump);
                    e.Serial = -1;
                }
                if (e.Started && !e.Runner.Running)
                {
                    e.Started = false;
                    e.Motion.SequenceChanged();
                    // FATO: o SQ_WAIT do jogo começa por MOVE "Stop" 1 4 (e_nume, f_apocaly, f_luce); a IA do cliente não roda essa
                    // sequência, então o freio vem daqui (HIPÓTESE: a volta é sempre para o SQ_WAIT).
                    if (e.Kind.Sequences.TryGetValue("SQ_WAIT", out var wait) && wait.FirstOrDefault(c => c.Kind == "MOVE") is { } stop && stop.S(0) == "Stop") e.Motion.Apply(stop);
                    e.Brain.Release();
                    // SQ_DDMG termina em JUMP "SQ_ERAS" (depois do ERDE01 com FADESET "OUT"): o inimigo some.
                    if (e.Kind.Stats != null && e.Hp <= 0) { Kill(e); return; }
                }
            }
            if (e.Started && e.Runner.Running) TickEnemyAction(e);
            else
            {
                // Passeio e espera da IA no rótulo guardado do SQ_WALK/SQ_WAIT (FATO, f_apocaly SQ_PHASE2: rótulo FLOAT, WAIT02 e RUNN03 com
                // MOVE "Move" 0,6; antes ficava no wait01/run do 1º bloco e na velocidade da fase 1).
                var (idleAction, idleMove) = IdleBlock(e, e.Brain.Walking ? "SQ_WALK" : "SQ_WAIT");
                if (e.Brain.Walking) vel = forward * Speed(idleMove ?? e.Kind.Walk);
                var want = (idleAction != null ? EnemyAnimName(e, idleAction) : null) ?? (e.Brain.Walking ? e.Kind.Run : e.Kind.Wait);
                if (e.Anim != null && want != null && e.Anim.HasAnimation(want) && e.Anim.CurrentAnimation != want) e.Anim.Play(want, 8 / SequenceRunner.FramesPerSecond);
            }
        }
        else if (e.Anim != null && e.Kind.Wait != null && !e.Anim.IsPlaying()) e.Anim.Play(e.Kind.Wait);
        // MOVE/MOVEEX: deslocamento local por quadro (X direita, Y cima, Z frente) e giro Y em graus. HIPÓTESE: unidade do jogo =
        // unidade do mundo, quadros a 30 Hz (como o pulo); o andar da IA (SQ_WALK) segue na escala do PadMovement.
        bool flying = false;
        if (e.Motion.Attached is { } rl && AttachTarget(rl, e.Kind.Model) is { } at)
        {
            e.Body.GlobalPosition = at.Origin;
            e.Yaw = MathF.Atan2(at.Basis.Z.X, at.Basis.Z.Z);
            vel = Vector3.Zero;
            flying = true;
        }
        if (e.Motion.AnyActive && dt > 0)
        {
            var (local, yawDeg) = e.Motion.Step(dt * SequenceRunner.FramesPerSecond);
            e.Yaw += yawDeg * MathF.PI / 180;
            var fw = new Vector3(MathF.Sin(e.Yaw), 0, MathF.Cos(e.Yaw));
            var right = new Vector3(fw.Z, 0, -fw.X);
            vel += (right * local.X + fw * local.Z) / dt;
            if (e.Motion[1].Active) { vel.Y = local.Y / dt; flying = true; if (local.Y > 0) e.Airborne = true; }
        }
        if (!flying)
        {
            if (e.Airborne && e.Body.IsOnFloor()) e.Airborne = false;
            vel.Y = e.Airborne ? 0 : e.Body.IsOnFloor() ? -1f : e.Body.Velocity.Y - Gravity * dt;
        }
        e.Body.Velocity = vel;
        e.Body.MoveAndSlide();
        // DECISÃO DO PORT (rede contra queda, como a do jogador): inimigo que atravessou o chão e caiu 100 abaixo de onde nasceu volta
        // ao ponto de nascimento. Sem isso ele ficava vivo caindo para sempre e a onda dele (e o script dela) nunca terminava.
        if (!e.Proxy && e.Body.GlobalPosition.Y < e.Home.Y - 100)
        {
            ScriptLog($"{e.Kind.Model} ({e.Gen?.Name ?? "sem gerador"}) caiu do mapa: volta a {e.Home.Round()}");
            e.Body.GlobalPosition = e.Home + Vector3.Up;
            e.Body.Velocity = Vector3.Zero;
            e.Airborne = false;
        }
        // HIPÓTESE (f_devl3 SQ_WALK: LUACALL "wft" "WHit" e MOVEEX "TZPACC" para a frente; wft_f_devl3.lua manda SQ_WALKOFF): "WHit"
        // é o evento de bater na parede. No passeio da IA (SQ_WALK interno) o evento vem do SQ_WALK do jogo.
        if (e.HitLua == null && e.Brain?.Walking == true && !e.Started && e.Kind.Sequences.TryGetValue("SQ_WALK", out var walk)
            && walk.FirstOrDefault(c => c.Kind == "LUACALL" && c.S(1).EndsWith("Hit", StringComparison.Ordinal)) is { } wl)
            e.HitLua = (wl.S(0), wl.S(1));
        if (e.HitLua is { } hit && e.Body.IsOnWall())
        {
            e.HitLua = null;
            RunLuaCall(hit.Name, e.Kind.Model, e, hit.Call);
        }
        if (e.Model != null) e.Model.Rotation = new Vector3(0, e.Yaw, 0);
    }

    // Ação nova da sequência do inimigo (som, animação) e as caixas de golpe contra o jogador.
    void TickEnemyAction(Enemy e)
    {
        if (e.Runner == null) return;
        if (e.Runner.ActionSerial != e.Serial)
        {
            e.Serial = e.Runner.ActionSerial;
            // Caixa que já acertou e passou para a ação nova (CarryHitBoxes) não acerta de novo.
            e.HitThisAction.RemoveWhere(k => !e.Runner.CarriedHitBoxes.Any(h => HitKey(h) == k));
            if (e.Runner.Action != null) _sfx?.Action(e.Kind.Model, e.Runner.Action);
            if (e.Anim != null && e.Runner.Action != null && EnemyAnimName(e, e.Runner.Action) is { } an)
            {
                e.Anim.GetAnimation(an).LoopMode = ActionAnimations.Loops(e.Kind.Actions, e.Runner.Action) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                e.Anim.Play(an, ActionAnimations.Blend(e.Kind.Actions, e.Runner.Action) / SequenceRunner.FramesPerSecond);
                // As animações seguintes da ação (MOTIONSET depois de WAITCHECK "Motion") entram em fila.
                foreach (var next in ActionAnimations.Chain(e.Kind.Actions, e.Runner.Action).Skip(1))
                    if (e.Anim.HasAnimation(next)) e.Anim.Queue(next);
            }
        }
        CheckEnemyHits(e);
    }

    void Kill(Enemy e)
    {
        e.Dead = true;
        if (e.Respawns)
        {
            e.RespawnTimer = 5;
            e.Body.Visible = false;
            e.Body.ProcessMode = ProcessModeEnum.Disabled;
            return;
        }
        CoopRemoved(e);
        RemoveEnemy(e);
        GeneratedDied(e.Gen, e);
    }

    Vector3 EnemyBoxCenter(Enemy e, HitBox h) => e.Body.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, e.Yaw);

    // A mesma caixa antes e depois de passar para a ação seguinte (o quadro inicial muda).
    static HitBox HitKey(HitBox h) => h with { StartFrame = 0 };

    // Caixas do corpo do jogador (beModelCtrlInfoDataHit da forma atual; o Guilmon tem 5 × 7 × 5, FATO).
    List<(NVector3 Min, NVector3 Max)> _playerBody = [];

    static NVector3 N(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Caixa de golpe (centro no mundo, tamanho nos eixos de quem bate) contra o corpo do jogador.</summary>
    bool HitsPlayer(Vector3 c, NVector3 size, float attackerYaw)
    {
        var g = _player.GlobalPosition;
        return _playerBody.Count > 0
            ? HitBody.Hits(N(c), size, attackerYaw, N(g), _move.Facing, 1, _playerBody)
            : Math.Abs(c.X - g.X) <= size.X / 2 + 2.5f && Math.Abs(c.Z - g.Z) <= size.Z / 2 + 2.5f && c.Y + size.Y / 2 >= g.Y && c.Y - size.Y / 2 <= g.Y + 7;
    }

    // Caixas 7xxxx do inimigo (ataque; 70004 no Numemon, HIPÓTESE pelo SQ_ATCK1) contra as caixas do corpo do jogador; sem elas no
    // json, a caixa do Guilmon.
    void CheckEnemyHits(Enemy e)
    {
        if (e.Runner == null || _runner?.Invincible == true) return;
        var g = _player.GlobalPosition;
        foreach (var h in e.Runner.ActiveHitBoxes().Where(h => h.Type / 10000 == 7))
        {
            if (e.HitThisAction.Contains(HitKey(h))) continue;
            var c = EnemyBoxCenter(e, h);
            if (HitsPlayer(c, h.Size, e.Yaw))
            {
                e.HitThisAction.UnionWith(e.Runner.ActionHitBoxes.Select(HitKey));
                _hitsTaken++;
                // Com o hit_p_<forma>.lua da fase, o próprio script decide dano, reação e estados (Fatia.PlayerHit.cs).
                if (PlayerHitScript(h.Type, e.Kind.Stats, e.Kind.Name, e.Body.GlobalPosition)) break;
                string? reaction = EnemyStrikes(h.Type, e.Kind.Stats, e.Kind.Name);
                if (reaction == null) break; // errou ou bloqueou: sem reação
                // Reação do Guilmon pela faixa do dano (bloco do estilo da arma, ou "common"), virado para quem bateu (HIPÓTESE).
                if (_runner != null && (_runner.Start(reaction, StyleOf(reaction)) || _runner.Start(reaction, "common")))
                {
                    _wazaPending = null; // interrompida: o MP já foi gasto no painel
                    var to = e.Body.GlobalPosition - g;
                    _move.Stop();
                    _move.Face(MathF.Atan2(to.X, to.Z));
                }
                break;
            }
        }
    }

    // Golpe físico do jogador contra cada inimigo (um acerto por inimigo por ação).
    void CheckHits()
    {
        if (_runner == null) return;
        foreach (var e in _enemies.ToList())
        {
            if (e.Dead || e.Killed || _enemiesHitThisAction.Contains(e) || (e.Started && e.Runner?.Invincible == true)) continue;
            var p = e.Body.GlobalPosition;
            // FATO (igb-seq do p_vmon): o golpe carregado (SQ_ATCKTM, bloco "gun": giro com pulo e 6 caixas) usa HITBOX 20000; o
            // x_100gunk01 da KATCG3 é só o efeito (sem caixa). Antes só o 10000 valia e o golpe carregado não acertava nada.
            foreach (var h in _runner.ActiveHitBoxes().Where(h => h.Type is 10000 or 20000))
            {
                // Caixa de dano contra as caixas do corpo do inimigo (beModelCtrlInfoDataHit, nos eixos e na escala dele). Sem elas no
                // json, a caixa pela malha (HitHalf/HitHeight, mínimo a do Numemon 8 × 14 × 8) em eixos do mundo.
                var c = BoxCenter(h);
                bool touch = e.Kind.BodyBoxes.Count > 0
                    ? HitBody.Hits(N(c), h.Size, _move.Facing, N(p), e.Yaw, e.Model?.Scale.X ?? 1, e.Kind.BodyBoxes)
                    : Math.Abs(c.X - p.X) <= h.Size.X / 2 + e.HitHalf && Math.Abs(c.Z - p.Z) <= h.Size.Z / 2 + e.HitHalf && c.Y + h.Size.Y / 2 >= p.Y && c.Y - h.Size.Y / 2 <= p.Y + e.HitHeight;
                if (DiagNpc && c.DistanceTo(p) < 40) GD.Print($"[golpe] {e.Kind.Model}: caixa {c.Round()} {h.Size} contra {p.Round()} (caixas do corpo {e.Kind.BodyBoxes.Count}, escala {e.Model?.Scale.X ?? 1:0.##}) → {(touch ? "acerta" : "erra")}");
                if (!touch) continue;
                _enemiesHitThisAction.Add(e);
                _hits++;
                _lastEnemy = e;
                int hpBefore = e.Hp;
                if (UsesHitScript(e))
                {
                    StrikeWithScript(e, h.Type); // a reação vem do MDLsetSeq do próprio script
                    ProxyStruck(e, hpBefore, "");
                    break;
                }
                string reaction = PlayerStrikes(e, h.Type);
                if (ProxyStruck(e, hpBefore, reaction)) break;
                if (e.Brain != null && e.Runner != null)
                {
                    e.Brain.Force(reaction);
                    e.Started = false;
                    var to = _player.GlobalPosition - p;
                    e.Yaw = MathF.Atan2(to.X, to.Z); // HIPÓTESE: vira para quem bateu
                }
                else if (e.Anim != null && e.Kind.Hurt != null) e.Anim.Play(e.Kind.Hurt, 0.05f);
                break;
            }
        }
    }
}
