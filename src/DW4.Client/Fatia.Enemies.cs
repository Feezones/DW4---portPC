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
    sealed class Enemy
    {
        public required CharacterBody3D Body { get; init; }
        public required EnemyKind Kind { get; init; }
        public Node3D? Model { get; init; }
        public AnimationPlayer? Anim { get; init; }
        public EnemyBrain? Brain { get; set; }
        public SequenceRunner? Runner { get; set; }
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
        public int Count { get; init; }
        public int MaxCount { get; init; }
        public int EnemyRow { get; init; }
        public string? CallLua { get; init; }
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
        public required GltfDocument Doc { get; init; }
        public required GltfState State { get; init; }
        public Fighter? Stats { get; init; }
        public string? Json { get; init; }
        public Dictionary<string, List<SeqCommand>> Sequences { get; init; } = [];
        public Dictionary<string, List<SeqCommand>> Actions { get; init; } = [];
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
        if (Directory.Exists(dir)) return dir;
        dir = Path.Combine(_dir, "numemon");
        return model == "e_nume" && Directory.Exists(dir) ? dir : null;
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
            Model = model, Row = row, Doc = same.Doc, State = same.State, Stats = _db?.Enemy(row), Json = same.Json,
            Sequences = same.Sequences, Actions = same.Actions, Wait = same.Wait, Run = same.Run, Hurt = same.Hurt, Walk = same.Walk,
        };
        else kind = LoadEnemyKind(model, row);
        _enemyKinds[row] = kind;
        return kind;
    }

    EnemyKind? LoadEnemyKind(string model, int row)
    {
        var root = EnemyDir(model);
        var file = root != null ? Directory.EnumerateFiles(root, "personagem.gltf", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null) { _problems.Add($"falta inimigos\\{model}\\personagem.gltf (dw4 fase-exportar)"); return null; }
        var doc = new GltfDocument();
        var state = new GltfState();
        if (doc.AppendFromFile(file, state) != Error.Ok) { _problems.Add($"{file}: erro ao ler"); return null; }
        var seqFile = Directory.EnumerateFiles(root!, "*_seq.json", SearchOption.AllDirectories).FirstOrDefault();
        string? json = seqFile != null ? File.ReadAllText(seqFile) : null;
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
            Wait = ActionAnimations.FirstAction(seqs, "SQ_WAIT", null) is { } wa ? ActionAnimations.Motion(acts, wa) : null,
            Run = ActionAnimations.FirstAction(seqs, "SQ_WALK", null) is { } ra ? ActionAnimations.Motion(acts, ra) : null,
        };
    }

    Enemy? SpawnEnemy(int row, Vector3 pos, float yaw, Generator? gen, bool respawns = false)
    {
        if (Kind(row) is not { } kind || kind.Doc.GenerateScene(kind.State) is not Node3D model) return null;
        // Caixa de corpo do Numemon (igAABox do HIT): 8 × 14 × 8, FATO; HIPÓTESE: a mesma para os outros até ler o HIT de cada um.
        var body = new CharacterBody3D { Position = pos, FloorSnapLength = 4f };
        body.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 4f, Height = 14f }, Position = new Vector3(0, 7, 0) });
        body.AddChild(model);
        model.Rotation = new Vector3(0, yaw, 0);
        AddChild(body);
        var e = new Enemy { Body = body, Model = model, Kind = kind, Anim = FindAll<AnimationPlayer>(model).FirstOrDefault(), Yaw = yaw, Hp = kind.Stats?.MaxHp ?? 0, Gen = gen, Home = pos, Respawns = respawns };
        if (kind.Json != null)
        {
            e.Runner = new SequenceRunner(kind.Sequences, a => EnemyAnimFrames(e, a));
            var maps = AiMap.LoadAll(kind.Json);
            if (maps.Count > 0) e.Brain = new EnemyBrain(maps, kind.Sequences, () => _rng.Next(16));
        }
        if (e.Anim != null)
        {
            var names = e.Anim.GetAnimationList();
            kind.Wait ??= Pick(names, "wait01", "wait");
            kind.Hurt ??= Pick(names, "Sdmg01", "dmg");
            foreach (var n in new[] { kind.Wait, kind.Run })
                if (n != null && e.Anim.HasAnimation(n)) e.Anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
            if (kind.Wait != null && e.Anim.HasAnimation(kind.Wait)) e.Anim.Play(kind.Wait);
        }
        _enemies.Add(e);
        return e;
    }

    string? EnemyAnimName(Enemy e, string action) =>
        ActionAnimations.Motion(e.Kind.Actions, action) is { } motion ? e.Anim?.GetAnimationList().FirstOrDefault(n => n == motion) : null;

    float? EnemyAnimFrames(Enemy e, string action) => ActionAnimations.Length(e.Kind.Actions, action, motion =>
        e.Anim != null && e.Anim.HasAnimation(motion) ? (float)e.Anim.GetAnimation(motion).Length * SequenceRunner.FramesPerSecond : null);

    bool Vulnerable(Enemy e) => !e.Dead && !e.Killed && !(e.Started && e.Runner?.Invincible == true);

    // ---------- geradores ----------

    void LoadStageData()
    {
        var genFile = Path.Combine(_dir, "geradores.json");
        if (File.Exists(genFile))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(genFile));
            foreach (var g in doc.RootElement.GetProperty("generators").EnumerateArray())
            {
                static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
                // Contagens por número de jogadores (1..4); a fatia tem 1 jogador.
                var gen = new Generator
                {
                    Name = g.GetProperty("name").GetString()!,
                    Positions = g.GetProperty("positions").EnumerateArray().Select(V).ToArray(),
                    RotYDeg = g.GetProperty("rotYDeg").EnumerateArray().Select(x => x.GetSingle()).ToArray(),
                    Count = g.GetProperty("count")[0].GetInt32(),
                    MaxCount = g.GetProperty("maxCount")[0].GetInt32(),
                    EnemyRow = g.GetProperty("enemyRow").GetInt32(),
                    CallLua = g.TryGetProperty("callLua", out var cl) && cl.ValueKind == JsonValueKind.String ? cl.GetString() : null,
                };
                _generators[gen.Name] = gen;
            }
        }
        else _problems.Add("falta geradores.json (dw4 igb-geradores ndmw.afs 184 <fatia>): sem ondas, Numemon fixo");
        var matFile = Path.Combine(_dir, "gatilhos.json");
        if (File.Exists(matFile))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(matFile));
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

    bool DeliverToGenerators(BeMessage m)
    {
        if (m.Class != "GENERATER" || m.Text(0) is not { } name || !_generators.TryGetValue(name, out var g)) return false;
        switch (m.Command)
        {
            case "START":
                if (!g.Finished) g.Active = true;
                break;
            case "STOP":
                g.Active = false;
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
        if (o is Enemy e) { e.Dead = true; e.Body.QueueFree(); _enemies.Remove(e); }
        else if (o is WorldObject w) { w.Dead = true; w.Body?.QueueFree(); w.Actor.Root.QueueFree(); _objects.Remove(w); }
    }

    // Morte de algo gerado: sai da lista do gerador; com o total gerado e ninguém vivo, roda o _callLua ("wipeout").
    void GeneratedDied(Generator? g, object o)
    {
        if (g == null || !g.Alive.Remove(o)) return;
        if (g.Spawned < g.Count || g.Alive.Count > 0 || g.Finished) return;
        g.Finished = true;
        g.Active = false;
        ScriptLog($"{g.Name}: onda vencida ({g.CallLua ?? "sem script"})");
        if (g.CallLua != null && _stage?.Has(g.CallLua) == true) _stage.Run(g.CallLua);
    }

    void TickGenerators()
    {
        foreach (var g in _generators.Values.Where(g => g.Active))
        {
            if (g.Spawned >= g.Count || g.Alive.Count >= g.MaxCount || g.Positions.Length == 0) continue;
            int slot = g.Spawned % g.Positions.Length;
            var pos = g.Positions[slot];
            float rot = g.RotYDeg.Length > slot ? g.RotYDeg[slot] : 0;
            string model = _db != null && _db.Contains("enemy") ? _db["enemy"].Text(g.EnemyRow, "MODEL") : "";
            object? spawned = model.StartsWith("g_", StringComparison.Ordinal)
                ? SpawnBox(model, pos, rot, g)
                : SpawnEnemy(g.EnemyRow, pos, rot * MathF.PI / 180, g);
            if (spawned == null) { g.Active = false; ScriptLog($"{g.Name}: não sei gerar {model} (linha {g.EnemyRow})"); continue; }
            g.Spawned++;
            g.Alive.Add(spawned);
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
        foreach (var e in _enemies.ToList()) UpdateEnemy(e, dt);
    }

    void UpdateEnemy(Enemy e, float dt)
    {
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
            var t = _player.GlobalPosition;
            e.Brain.Tick(dt, new NVector3(p.X, p.Y, p.Z), new NVector3(t.X, t.Y, t.Z), e.Yaw);
            e.Yaw = e.Brain.Yaw;
            forward = new Vector3(MathF.Sin(e.Yaw), 0, MathF.Cos(e.Yaw));
            if (e.Brain.External is { } ext)
            {
                // Sequência do jogo (SQ_ATCK1, SQ_FIRE, SQ_SDMG…); "common" é o bloco sem elemento das de dano.
                if (!e.Started)
                {
                    e.Started = e.Runner.Start(ext, "common");
                    e.Serial = -1;
                    if (!e.Started) e.Brain.Release();
                }
                else e.Runner.Tick(dt, false);
                if (e.Started && !e.Runner.Running)
                {
                    e.Started = false;
                    e.Brain.Release();
                    // SQ_DDMG termina em JUMP "SQ_ERAS" (depois do ERDE01 com FADESET "OUT"): o inimigo some.
                    if (e.Kind.Stats != null && e.Hp <= 0) { Kill(e); return; }
                }
            }
            if (e.Started && e.Runner.Running)
            {
                if (e.Runner.Move is { } m)
                    vel = m.S(0) switch { "Move" => forward * Speed(m.F(1)), "Back" => -forward * Speed(m.F(1)), _ => Vector3.Zero };
                if (e.Runner.ActionSerial != e.Serial)
                {
                    e.Serial = e.Runner.ActionSerial;
                    e.HitThisAction.Clear();
                    if (e.Runner.Action != null) _sfx?.Action(e.Kind.Model, e.Runner.Action);
                    if (e.Anim != null && e.Runner.Action != null && EnemyAnimName(e, e.Runner.Action) is { } an)
                    {
                        e.Anim.GetAnimation(an).LoopMode = ActionAnimations.Loops(e.Kind.Actions, e.Runner.Action) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                        e.Anim.Play(an, ActionAnimations.Blend(e.Kind.Actions, e.Runner.Action) / SequenceRunner.FramesPerSecond);
                    }
                }
                CheckEnemyHits(e);
            }
            else
            {
                if (e.Brain.Walking) vel = forward * Speed(e.Kind.Walk);
                var want = e.Brain.Walking ? e.Kind.Run : e.Kind.Wait;
                if (e.Anim != null && want != null && e.Anim.HasAnimation(want) && e.Anim.CurrentAnimation != want) e.Anim.Play(want, 8 / SequenceRunner.FramesPerSecond);
            }
        }
        else if (e.Anim != null && e.Kind.Wait != null && !e.Anim.IsPlaying()) e.Anim.Play(e.Kind.Wait);
        vel.Y = e.Body.IsOnFloor() ? -1f : e.Body.Velocity.Y - Gravity * dt;
        e.Body.Velocity = vel;
        e.Body.MoveAndSlide();
        if (e.Model != null) e.Model.Rotation = new Vector3(0, e.Yaw, 0);
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
        e.Body.QueueFree();
        _enemies.Remove(e);
        if (_lastEnemy == e) _lastEnemy = null;
        GeneratedDied(e.Gen, e);
    }

    Vector3 EnemyBoxCenter(Enemy e, HitBox h) => e.Body.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, e.Yaw);

    // Caixas 7xxxx do inimigo (ataque; 70004 no Numemon, HIPÓTESE pelo SQ_ATCK1) contra a caixa de corpo do Guilmon (5 × 7 × 5, FATO).
    void CheckEnemyHits(Enemy e)
    {
        if (e.Runner == null || _runner?.Invincible == true) return;
        var g = _player.GlobalPosition;
        foreach (var h in e.Runner.ActiveHitBoxes().Where(h => h.Type / 10000 == 7))
        {
            if (e.HitThisAction.Contains(h)) continue;
            var c = EnemyBoxCenter(e, h);
            if (Math.Abs(c.X - g.X) <= h.Size.X / 2 + 2.5f && Math.Abs(c.Z - g.Z) <= h.Size.Z / 2 + 2.5f && c.Y + h.Size.Y / 2 >= g.Y && c.Y - h.Size.Y / 2 <= g.Y + 7)
            {
                e.HitThisAction.UnionWith(e.Runner.ActionHitBoxes);
                _hitsTaken++;
                string? reaction = EnemyStrikes(h.Type, e.Kind.Stats, e.Kind.Name);
                if (reaction == null) break; // errou ou bloqueou: sem reação
                // Reação do Guilmon pela faixa do dano (bloco do estilo da arma, ou "common"), virado para quem bateu (HIPÓTESE).
                if (_runner != null && (_runner.Start(reaction, WeaponStyle) || _runner.Start(reaction, "common")))
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
            foreach (var h in _runner.ActiveHitBoxes().Where(h => h.Type == 10000))
            {
                // Caixa de dano contra a caixa de corpo do Numemon (8 × 14 × 8), em eixos do mundo: aproximação.
                var c = BoxCenter(h);
                if (!(Math.Abs(c.X - p.X) <= h.Size.X / 2 + 4 && Math.Abs(c.Z - p.Z) <= h.Size.Z / 2 + 4 && c.Y + h.Size.Y / 2 >= p.Y && c.Y - h.Size.Y / 2 <= p.Y + 14)) continue;
                _enemiesHitThisAction.Add(e);
                _hits++;
                _lastEnemy = e;
                string reaction = PlayerStrikes(e, h.Type);
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
