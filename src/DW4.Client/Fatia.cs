using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace DW4.Client;

/// <summary>
/// Fatia vertical da Fase 3: dng1 com colisão, Guilmon controlável e câmera por volumes do jogo.
/// Regras (câmera, movimento) ficam em DW4.Gameplay; aqui só entrada, física da Godot e apresentação.
/// Recursos lidos em tempo de execução da pasta DW4_FATIA (fora do repositório), gerada por:
///   dw4 igb-cena ndmw.afs 179 2877 &lt;fatia&gt;\cenario · dw4 igb-colisao ndmw.afs 179 1862 &lt;fatia&gt;\colisao
///   dw4 igb-gltf ndmw.afs 857 &lt;fatia&gt;\guilmon · dw4 igb-seq ndmw.afs 857 &lt;fatia&gt;\guilmon · dw4 igb-cena ndmw.afs 967 todas &lt;fatia&gt;\arma · dw4 igb-gltf ndmw.afs 179 &lt;fatia&gt;\numemon --ator=e_nume · dw4 igb-camera ndmw.afs 180 e 181 &lt;fatia&gt; · dw4 igb-hud ndmw.afs 179 area.afs 9 &lt;fatia&gt;\hud
/// </summary>
public partial class Fatia : Node3D
{
    // PROVISÓRIO até medir no jogo: ponto inicial (gerador GENE-03 da dng1; o PLAYERPOINT real ainda não foi achado)
    // e gravidade. Caixa de corpo do Guilmon (beModelCtrlInfoDataHit → igAABox): 5 × 7 × 5, FATO.
    static readonly Vector3 Start = new(-180, 45, 60);
    // Numemon no gerador GENE-05 da dng1 (FATO: posição e rotação −90° do beGeneraterData).
    static readonly Vector3 EnemyAt = new(40, 45, 120);
    // HIPÓTESE: o primeiro número do MOVE "Move" é proporcional à velocidade. O Guilmon anda com 0,7 e PadMovement.MaxSpeed
    // (provisório); o Numemon, com 0,24, anda a 0,24 / 0,7 disso.
    const float GuilmonMove = 0.7f;
    const float Gravity = 300f;

    string _dir = "";
    CameraZones[] _cameraSets = [];
    int _cameraSet, _zone = -1;
    readonly PadMovement _move = new();
    CharacterBody3D _player = null!;
    Node3D? _model;
    AnimationPlayer? _anim;
    string? _animWait, _animRun;
    // Ataque: sequência SQ_ATCK1 do Guilmon no bloco do estilo da arma (w_sword01 = "slash", FATO pelo beWeaponSeqData).
    const string WeaponStyle = "slash";
    // HIPÓTESE: osso da mão direita para prender a arma (beWeaponAttachData só existe no executável).
    const string WeaponBone = "RArm04";
    SequenceRunner? _runner;
    Dictionary<string, List<SeqCommand>> _actions = [];
    int _lastSerial;
    bool _attackHeld, _showBoxes, _techHeld;
    int _techKey = -1;
    CharacterBody3D? _enemy;
    AnimationPlayer? _enemyAnim;
    string? _enemyWait, _enemyHurt, _enemyRun;
    int _hits;
    // IA do Numemon: mapas beModelCtrlAIMap e sequências do e_nume (#179), ver EnemyBrain.
    EnemyBrain? _brain;
    SequenceRunner? _enemyRunner;
    Dictionary<string, List<SeqCommand>> _enemyActions = [];
    float _enemyYaw = -MathF.PI / 2, _enemyWalk = 0.24f;
    bool _enemyStarted;
    Node3D? _enemyModel;
    int _enemySerial, _hitsTaken;
    readonly HashSet<HitBox> _enemyHitThisAction = [];
    readonly Random _rng = new();
    // Combate com as tabelas e fórmulas do jogo (GameTables, DamageFormula, PlayerGrowth, SkillGrowth). Sorteio: o MT19937
    // do jogo com a semente do construtor (5150). A IA continua com o Random do .NET (o sorteio dela não foi lido).
    GameTables? _db;
    SkillGrowth? _skillGrowth;
    readonly MersenneTwister _mt = new();
    Fighter? _pf, _ef;
    int _shinkaRow = -1, _weaponRow = -1, _level = 1, _hp, _enemyHp;
    long _exp;
    readonly int[] _skill = new int[9];
    // Técnicas (Techniques, TechniquePanel). DECISÃO DA FATIA: sem o painel do jogo, as teclas 1..6 usam a técnica n da
    // WAZALIST direto (K, botão direito ou Y usam a primeira).
    int _mp;
    List<Technique> _techs = [];
    readonly TechniquePanel _panel = new();
    Technique? _wazaPending;
    // Projéteis das técnicas (KIND_PBULLET x_200fire01…: MOVE "Push" 1,5 e HITBOX 4 × 4 × 4 por 1000 quadros, FATO).
    // A fatia desenha uma esfera no lugar do modelo do projétil.
    readonly List<(MeshInstance3D Mesh, Vector3 Dir, float Life, Technique Tech)> _shots = [];
    // Drops (Drops, Inventory): lista do gerador do Numemon no modo normal (GENE-01..07 do #183 → nZ1nume1, FATO), um
    // sorteio por Numemon morto. A fatia desenha uma caixa com o nome no lugar do modelo do item.
    const string DropList = "nZ1nume1";
    List<DropEntry>? _drops;
    readonly Inventory _inventory = new();
    int _xdata;
    bool _killed;
    // Equipamento (RECALCPLAYER completo). DECISÃO DA FATIA: sem o menu de status, a tecla E equipa o último equipamento
    // pego, se a regra do jogo deixar; a arma muda só os números (o modelo na mão continua o da w_sword01).
    Equipment? _equip;
    SoundEffects? _sfx;
    readonly List<(Node3D Node, ItemRef Item)> _ground = [];
    // Linha do Numemon na tabela enemy: Int@7 dos geradores da dng1 no modo normal (generatorN #184 = 13; HIPÓTESE forte).
    const int EnemyRow = 13;
    bool _enemyDead;
    float _respawn;
    string _lastEvent = "";
    readonly List<(Label3D Label, float Life)> _floaters = [];
    static readonly string[] WeaponGrow = ["BLUNT_GROW", "SLASH_GROW", "STAB_GROW", "BASH_GROW", "SHOT_GROW", "CRUSH_GROW"];
    static readonly string[] DamageSequences = ["SQ_SDMG", "SQ_MDMG", "SQ_LDMG", "SQ_DDMG"];
    // DW4_DEMO=1: começa ao lado do Numemon, virado para ele, e aperta ataque sozinho (para capturas sem controle).
    bool _demo;
    int _tick;
    readonly HashSet<HitBox> _hitThisAction = [];
    readonly List<MeshInstance3D> _boxMeshes = [];
    Camera3D _camera = null!;
    Label _hud = null!;
    // HUD do jogo (Hud.cs): painel de status do jogador 1 e números de dano i_dmgnum/i_pcdmgnum, de <fatia>hud.
    HudOverlay? _overlay;
    DamageNumbers? _damageNumbers;
    readonly List<string> _problems = [];

    public override void _Ready()
    {
        _dir = OS.GetEnvironment("DW4_FATIA") is { Length: > 0 } d ? d : @"D:\dw4-work\fatia";
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.65f), BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, 30, 0) });

        if (Load("cenario", "cena.gltf") is { } scenery) AddChild(scenery);
        if (Load("colisao", "colisao.gltf") is { } collision) AddChild(MakeStaticBody(collision));

        _demo = OS.GetEnvironment("DW4_DEMO") == "1";
        _showBoxes = _demo;
        var start = _demo ? EnemyAt + new Vector3(-16, 0, 0) : Start;
        // DW4_INICIO="x,y,z": ponto inicial para capturas (ex.: conferir a câmera de um volume).
        if (OS.GetEnvironment("DW4_INICIO").Split(',') is { Length: 3 } xyz
            && xyz.Select(t => float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : float.NaN).ToArray() is var v3
            && v3.All(float.IsFinite))
            start = new Vector3(v3[0], v3[1], v3[2]);
        _player = new CharacterBody3D { Position = start, FloorSnapLength = 4f };
        _player.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 2.5f, Height = 7f }, Position = new Vector3(0, 3.5f, 0) });
        AddChild(_player);
        _model = Load("guilmon", "personagem.gltf");
        if (_model != null)
        {
            _player.AddChild(_model);
            _anim = FindAll<AnimationPlayer>(_model).FirstOrDefault();
            if (_anim != null)
            {
            }
            var seqFile = SeqFile("guilmon");
            if (seqFile == null) _problems.Add("falta o *_seq.json do Guilmon (dw4 igb-seq ndmw.afs 857 <fatia>\\guilmon)");
            else
            {
                var json = System.IO.File.ReadAllText(seqFile);
                _actions = SeqCommand.LoadActions(json);
                if (_actions.Count == 0) _problems.Add("o *_seq.json não tem ações: regere com o igb-seq atual");
                var sequences = SeqCommand.LoadAll(json);
                _runner = new SequenceRunner(sequences, AnimFrames);
                // Parado e andando conforme o estilo da arma: SQ_WAIT / SQ_WALK → primeira ACTION do bloco → MOTIONSET.
                // (Antes era "waitG"/"runG": o G é o estilo gun, por isso os braços erguidos.)
                if (ActionAnimations.FirstAction(sequences, "SQ_WAIT", WeaponStyle) is { } wa) _animWait = AnimName(wa);
                // SQ_WALK (bloco slash) → RUNNS1, cujos MOTIONSET são "waitS" e depois "runS": o andar é o último.
                // HIPÓTESE: o primeiro é a transição de parado para andando.
                _animRun = ActionAnimations.BlockActions(sequences, "SQ_WALK", WeaponStyle)
                    .SelectMany(a => ActionAnimations.Motions(_actions, a)).LastOrDefault() is { } rm && _anim != null && _anim.HasAnimation(rm) ? rm : null;
            }
            if (_anim != null)
            {
                var names = _anim.GetAnimationList();
                _animWait ??= Pick(names, "waitS", "wait");
                _animRun ??= Pick(names, "runS", "run");
                foreach (var n in new[] { _animWait, _animRun })
                    if (n != null) _anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
                if (_animWait != null) _anim.Play(_animWait);
            }
            var skeleton = FindAll<Skeleton3D>(_model).FirstOrDefault();
            if (Load("arma", "cena.gltf") is { } weapon && skeleton != null)
            {
                if (skeleton.FindBone(WeaponBone) < 0) _problems.Add($"osso {WeaponBone} não existe para a arma");
                var attach = new BoneAttachment3D { BoneName = WeaponBone };
                skeleton.AddChild(attach);
                attach.AddChild(weapon);
            }
        }

        if (Load("numemon", "personagem.gltf") is { } numemon)
        {
            // Caixa de corpo do Numemon (igAABox do HIT): 8 × 14 × 8, FATO; forma de cápsula é aproximação.
            var enemy = _enemy = new CharacterBody3D { Position = EnemyAt, FloorSnapLength = 4f };
            enemy.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 4f, Height = 14f }, Position = new Vector3(0, 7, 0) });
            enemy.AddChild(numemon);
            (_enemyModel = numemon).Rotation = new Vector3(0, _enemyYaw, 0);
            AddChild(enemy);
            _enemyAnim = FindAll<AnimationPlayer>(numemon).FirstOrDefault();
            if (SeqFile("numemon") is not { } enemySeq) _problems.Add("falta o *_seq.json do Numemon (dw4 igb-seq ndmw.afs 179 <fatia>\\numemon --ator=e_nume)");
            else
            {
                var json = System.IO.File.ReadAllText(enemySeq);
                _enemyActions = SeqCommand.LoadActions(json);
                var sequences = SeqCommand.LoadAll(json);
                _enemyRunner = new SequenceRunner(sequences, EnemyAnimFrames);
                var maps = AiMap.LoadAll(json);
                if (maps.Count == 0) _problems.Add("o *_seq.json do Numemon não tem mapas de IA: regere com o igb-seq atual");
                else _brain = new EnemyBrain(maps, sequences, () => _rng.Next(16));
                // Parado e andando: SQ_WAIT → WAIT01 → "wait01"; SQ_WALK → RUNN01 → "run01" e MOVE "Move" 0,24.
                if (ActionAnimations.FirstAction(sequences, "SQ_WAIT", null) is { } wa) _enemyWait = EnemyAnimName(wa);
                if (ActionAnimations.FirstAction(sequences, "SQ_WALK", null) is { } ra) _enemyRun = EnemyAnimName(ra);
                if (sequences.TryGetValue("SQ_WALK", out var walk) && walk.FirstOrDefault(c => c.Kind == "MOVE" && c.S(0) == "Move") is { } mv)
                    _enemyWalk = mv.F(1);
            }
            if (_enemyAnim != null)
            {
                var names = _enemyAnim.GetAnimationList();
                _enemyWait ??= Pick(names, "wait01", "wait");
                _enemyHurt = Pick(names, "Sdmg01", "dmg");
                foreach (var n in new[] { _enemyWait, _enemyRun })
                    if (n != null) _enemyAnim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
                if (_enemyWait != null) _enemyAnim.Play(_enemyWait);
            }
        }

        LoadCombat();
        if (HudData.Load(System.IO.Path.Combine(_dir, "hud"), _problems) is { } hud)
        {
            _overlay = new HudOverlay(hud, this);
            _damageNumbers = new DamageNumbers(hud, this);
        }
        // Som da dng1 (FATO: first.lua toca "AM_DUNGE" e "DG_CAVE1"; beSoundInfo da fase → ndmw #6 e #35), com o loop do
        // cabeçalho ADX. Gerado por: dw4 adx-wav ndmw.afs <fatia>\audio 6 35. Volume do beSoundData (S8): unidade
        // desconhecida, por isso os dois tocam a 0 dB.
        _sfx = new SoundEffects(System.IO.Path.Combine(_dir, "sons"), this, LoadWav, _problems);
        // Com os scripts (Fatia.Scripts.cs), as músicas vêm do first.lua; sem eles, tocam fixas.
        if (!StartScripts())
        {
            foreach (var idx in new[] { 6, 35 })
            {
                var wav = System.IO.Path.Combine(_dir, "audio", $"ndmw_{idx:D4}.wav");
                if (!System.IO.File.Exists(wav)) { _problems.Add($"falta {wav} (dw4 adx-wav ndmw.afs <fatia>\\audio 6 35)"); continue; }
                if (LoadWav(wav) is { } stream) { var p = new AudioStreamPlayer { Stream = stream }; AddChild(p); p.Play(); }
            }
        }

        _cameraSets = new[] { "ndmw_0180_camera.json", "ndmw_0181_camera.json" }
            .Select(f => System.IO.Path.Combine(_dir, f))
            .Where(p => { bool ok = System.IO.File.Exists(p); if (!ok) _problems.Add($"falta {p}"); return ok; })
            .Select(p => CameraZones.FromJson(System.IO.File.ReadAllText(p))).ToArray();
        _camera = new Camera3D { Fov = 45, Far = 4000, Current = true };
        AddChild(_camera);

        var layer = new CanvasLayer();
        _hud = new Label { Position = new Vector2(12, 12) };
        layer.AddChild(_hud);
        AddChild(layer);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.F1 && _cameraSets.Length > 1) _cameraSet = (_cameraSet + 1) % _cameraSets.Length;
            if (k.Keycode == Key.F2) _showBoxes = !_showBoxes;
            if (k.Keycode == Key.F5) { _player.Position = Start; _player.Velocity = Vector3.Zero; _move.Stop(); _hp = _pf?.MaxHp ?? 0; _mp = _pf?.MaxMp ?? 0; }
            if (k.Keycode is >= Key.Key1 and <= Key.Key6) _techKey = (int)(k.Keycode - Key.Key1);
            if (k.Keycode == Key.K) _techKey = 0;
            if (k.Keycode == Key.E) EquipLast();
            if (k.Keycode == Key.Escape) GetTree().Quit();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _stage?.Tick();
        var input = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        if (Input.IsKeyPressed(Key.A)) input.X -= 1;
        if (Input.IsKeyPressed(Key.D)) input.X += 1;
        if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) input.Y += 1;
        input = input.LimitLength(1);

        // Direção relativa à câmera, no plano XZ
        var forward = _player.GlobalPosition - _camera.GlobalPosition;
        forward.Y = 0;
        forward = forward.LengthSquared() > 1e-6f ? forward.Normalized() : Vector3.Forward;
        var right = forward.Cross(Vector3.Up);
        var dir = right * input.X - forward * input.Y;

        bool attackDown = Input.IsKeyPressed(Key.J) || Input.IsMouseButtonPressed(MouseButton.Left) || Input.IsJoyButtonPressed(0, JoyButton.X);
        _tick++;
        if (_demo)
        {
            attackDown = _tick % 15 < 2 && _tick > 30; // um toque a cada 0,25 s depois de 0,5 s
            if (_tick < 3) input = new Vector2(0, 0);
        }
        if (_pf != null && _hp <= 0) { input = Vector2.Zero; dir = Vector3.Zero; attackDown = false; } // caído: F5 reinicia
        if (!_padEnabled) { input = Vector2.Zero; dir = Vector3.Zero; attackDown = false; _techKey = -1; } // STAGECTL PADINPUT DISABLE do script
        bool attackPressed = attackDown && !_attackHeld;
        _attackHeld = attackDown;
        bool techDown = Input.IsMouseButtonPressed(MouseButton.Right) || Input.IsJoyButtonPressed(0, JoyButton.Y);
        if (techDown && !_techHeld) _techKey = 0;
        _techHeld = techDown;
        if (_pf != null && _hp <= 0) _techKey = -1;
        bool attacking = _runner?.Running == true;
        bool wasWaza = _wazaPending != null;
        if (_runner != null && !attacking && (attackPressed || _techKey >= 0))
        {
            attacking = _techKey >= 0 ? StartTechnique(_techKey) && _runner.Running : _runner.Start("SQ_ATCK1", WeaponStyle);
            _techKey = -1;
            if (attacking && !_demo && !Input.IsJoyButtonPressed(0, JoyButton.X) && MouseGround() is { } aim)
            {
                // DECISÃO DO PORT PARA PC (não é do original): o golpe sai na direção do cursor do mouse.
                var to = aim - _player.GlobalPosition;
                if (to.X * to.X + to.Z * to.Z > 0.01f) _move.Face(MathF.Atan2(to.X, to.Z));
            }
        }
        else if (attacking) _runner!.Tick(dt, attackPressed && !wasWaza);
        _techKey = -1;
        _panel.Tick(dt);
        if (_wazaPending is { } waza && _runner?.Running != true) { _wazaPending = null; ReleaseTechnique(waza); }
        if (attacking) { dir = Vector3.Zero; _move.Stop(); }
        if (_demo && _tick < 30 && _enemy != null)
        {
            var to = _enemy.GlobalPosition - _player.GlobalPosition;
            dir = new Vector3(to.X, 0, to.Z).Normalized() * 0.05f; // só vira para o Numemon
        }

        var v = _move.Step(new NVector2(dir.X, dir.Z), dt);
        // MOVE da sequência em andamento (recuo do dano: "Back"), na velocidade proporcional ao "Move" 0,7 (HIPÓTESE).
        if (attacking && _runner!.Move is { } mv && mv.S(0) is "Back" or "Move")
        {
            float sign = mv.S(0) == "Back" ? -1 : 1;
            v = new NVector2(MathF.Sin(_move.Facing), MathF.Cos(_move.Facing)) * (sign * _move.MaxSpeed * mv.F(1) / GuilmonMove);
        }
        var vel = _player.Velocity;
        vel.X = v.X;
        vel.Z = v.Y;
        vel.Y = _player.IsOnFloor() ? -1f : vel.Y - Gravity * dt;
        _player.Velocity = vel;
        _player.MoveAndSlide();
        if (_model != null) _model.Rotation = new Vector3(0, _move.Facing, 0);

        if (_runner?.Running == true)
        {
            if (_runner.ActionSerial != _lastSerial)
            {
                _lastSerial = _runner.ActionSerial;
                _hitThisAction.Clear();
                if (_runner.Action != null) _sfx?.Action("p_girumon", _runner.Action);
                if (_anim != null && _runner.Action != null && AnimName(_runner.Action) is { } an)
                {
                    _anim.GetAnimation(an).LoopMode = ActionAnimations.Loops(_actions, _runner.Action) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                    _anim.Play(an, ActionAnimations.Blend(_actions, _runner.Action) / SequenceRunner.FramesPerSecond);
                }
            }
            CheckHits();
        }
        else
        {
            bool moving = v.LengthSquared() > 1f;
            var want = moving ? _animRun : _animWait;
            if (_anim != null && want != null && _anim.CurrentAnimation != want) _anim.Play(want, 0.13f); // ~8 quadros a 60 Hz (MOTIONSET Float@4 = 8, hipótese)
        }
        UpdateEnemy(dt);
        UpdateFloaters(dt);
        _damageNumbers?.Update(dt);
        if (_pf != null) _overlay?.Update(dt, _hp, _pf.MaxHp, _mp, _pf.MaxMp, _level);
        UpdateShots(dt);
        UpdateGround();
        _sfx?.Tick(dt);
        DrawBoxes();

        UpdateCamera();
    }

    void UpdateCamera()
    {
        var p = _player.GlobalPosition;
        if (_cameraSets.Length == 0)
        {
            _camera.LookAtFromPosition(p + new Vector3(0, 80, 110), p, Vector3.Up);
        }
        else
        {
            var set = _cameraSets[_cameraSet];
            var target = new NVector3(p.X, p.Y, p.Z);
            _zone = set.Select(target, _zone);
            var (pos, look) = set.Pose(_zone, target);
            _camera.LookAtFromPosition(new Vector3(pos.X, pos.Y, pos.Z), new Vector3(look.X, look.Y, look.Z), Vector3.Up);
        }
        _hud.Text = $"pos ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})  vel {_move.Velocity.Length():0.0} u/s\n" +
            $"câmera {(_cameraSet == 0 ? "#180" : "#181 (afastada)")} volume {_zone}  [F1 troca, F5 reinicia, Esc sai]\n" +
            $"animações: parado = {_animWait ?? "-"}, correndo = {_animRun ?? "-"}\n" +
            $"ataque [J, clique ou X]: {(_runner?.Running == true ? $"{_runner.Action} → {(_runner.Action == null ? "-" : AnimName(_runner.Action) ?? "sem animação")} quadro {_runner.ActionFrame:0}/{_runner.ActionLength:0}" : "-")}  acertos no Numemon: {_hits}  [F2 caixas]\n" +
            $"Numemon: {(_brain == null ? "sem IA" : $"{_brain.Map.Name}, {(_brain.External ?? _brain.Current?.Sequence ?? "-")}")}{(_enemyRunner?.Running == true ? $" ({_enemyRunner.Action})" : "")}  golpes no Guilmon: {_hitsTaken}\n" +
            (_pf == null ? "combate: sem tabelas (dw4 igb-db area.afs 3 <fatia>\\db)\n" :
                $"Guilmon nv {_level}  HP {_hp}/{_pf.MaxHp}  MP {_mp}/{_pf.MaxMp}  EXP {_exp}/{PlayerGrowth.NextExp(_level)}  SLASH {_skill[1]}  |  Numemon HP {(_enemyDead ? "derrotado" : $"{_enemyHp}/{_ef!.MaxHp}")}  |  {_lastEvent}{(_hp <= 0 ? "  |  CAIU: F5 reinicia" : "")}\n" +
                $"técnicas [1..6; K, botão direito ou Y = 1]: {(_techs.Count == 0 ? "-" : string.Join("  ", _techs.Select((t, i) => $"{i + 1} {t.Name} {t.Mp} MP{(_panel.Cooldown(t) > 0 ? $" ({_panel.Cooldown(t):0.0} s)" : "")}")))}  skills ataque/cura/suporte {_skill[6]}/{_skill[7]}/{_skill[8]}\n" +
                $"arma {_db!["weapon"].Text(_equip!.WeaponRow, "NAME")}  armaduras {(_equip.Armor1Row >= 0 ? _db["armor1"].Text(_equip.Armor1Row, "NAME") : "-")} / {(_equip.Armor2Row >= 0 ? _db["armor2"].Text(_equip.Armor2Row, "NAME") : "-")}  [E equipa o último pego]  " +
                $"BIT {_inventory.Bit}  Xデータ {_xdata}  equipamentos {_inventory.Devices.Count}/{Inventory.DeviceMax}{(_inventory.Devices.Count > 0 ? ": " + string.Join(", ", _inventory.Devices.TakeLast(3).Select(d => _db![d.Table].Text(d.Row, "NAME"))) : "")}\n") +
            "movimento, gravidade, ponto inicial e IA PROVISÓRIOS (não medidos no jogo)" +
            (_stage != null ? $"\nscripts{(_padEnabled ? "" : " (controle travado pelo script)")}:\n  " + string.Join("\n  ", _scriptLog.TakeLast(6)) : "") +
            (_problems.Count > 0 ? "\n" + string.Join("\n", _problems) : "");
    }

    // Ponto do chão (na altura do personagem) sob o cursor do mouse.
    Vector3? MouseGround()
    {
        var mouse = GetViewport().GetMousePosition();
        var origin = _camera.ProjectRayOrigin(mouse);
        var normal = _camera.ProjectRayNormal(mouse);
        if (Math.Abs(normal.Y) < 1e-4f) return null;
        float t = (_player.GlobalPosition.Y - origin.Y) / normal.Y;
        return t > 0 ? origin + normal * t : null;
    }

    // Ação do jogo ("VATCS1") → MOTIONSET da beAction2Data de mesmo nome → animação do glTF.
    string? AnimName(string action) =>
        ActionAnimations.Motion(_actions, action) is { } motion ? _anim?.GetAnimationList().FirstOrDefault(n => n == motion) : null;

    string? EnemyAnimName(string action) =>
        ActionAnimations.Motion(_enemyActions, action) is { } motion ? _enemyAnim?.GetAnimationList().FirstOrDefault(n => n == motion) : null;

    float? EnemyAnimFrames(string action) => ActionAnimations.Length(_enemyActions, action, motion =>
        _enemyAnim != null && _enemyAnim.HasAnimation(motion) ? (float)_enemyAnim.GetAnimation(motion).Length * SequenceRunner.FramesPerSecond : null);

    string? SeqFile(string sub)
    {
        var root = System.IO.Path.Combine(_dir, sub);
        return System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, "*_seq.json", System.IO.SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    float? AnimFrames(string action) => ActionAnimations.Length(_actions, action, motion =>
        _anim != null && _anim.HasAnimation(motion) ? (float)_anim.GetAnimation(motion).Length * SequenceRunner.FramesPerSecond : null);

    // Caixa local (x lateral, y altura, z à frente) no mundo, girada pela direção do personagem.
    Vector3 BoxCenter(HitBox h) => _player.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, _move.Facing);

    void CheckHits()
    {
        if (_runner == null || _enemy == null || _enemyDead || (_enemyStarted && _enemyRunner?.Invincible == true)) return;
        var e = _enemy.GlobalPosition;
        foreach (var h in _runner.ActiveHitBoxes().Where(h => h.Type == 10000))
        {
            if (_hitThisAction.Contains(h)) continue;
            // Caixa de dano contra a caixa de corpo do Numemon (8 × 14 × 8), em eixos do mundo: aproximação.
            var c = BoxCenter(h);
            if (Math.Abs(c.X - e.X) <= h.Size.X / 2 + 4 && Math.Abs(c.Z - e.Z) <= h.Size.Z / 2 + 4 && c.Y + h.Size.Y / 2 >= e.Y && c.Y - h.Size.Y / 2 <= e.Y + 14)
            {
                _hitThisAction.UnionWith(_runner.ActionHitBoxes);
                _hits++;
                string reaction = PlayerStrikes(h.Type);
                if (_brain != null && _enemyRunner != null)
                {
                    _brain.Force(reaction);
                    _enemyStarted = false;
                    var to = _player.GlobalPosition - e;
                    _enemyYaw = MathF.Atan2(to.X, to.Z); // HIPÓTESE: vira para quem bateu
                }
                else if (_enemyAnim != null && _enemyHurt != null) _enemyAnim.Play(_enemyHurt, 0.05f);
                break;
            }
        }
    }

    void LoadCombat()
    {
        var dbDir = System.IO.Path.Combine(_dir, "db");
        var dbFile = System.IO.Directory.Exists(dbDir) ? System.IO.Directory.EnumerateFiles(dbDir, "*_db.json").FirstOrDefault() : null;
        if (dbFile == null) { _problems.Add("falta o *_db.json (dw4 igb-db area.afs 3 <fatia>\\db): combate desligado"); return; }
        _db = GameTables.FromJson(System.IO.File.ReadAllText(dbFile));
        var skillFile = System.IO.Path.Combine(_dir, "skillup.json");
        if (System.IO.File.Exists(skillFile)) _skillGrowth = SkillGrowth.FromJson(System.IO.File.ReadAllText(skillFile));
        else _problems.Add("falta skillup.json (dw4 dol-skilltab <main.dol> <fatia>): skill não sobe");
        _shinkaRow = _db["shinka"].RowsWhere("MODEL_NAME", "p_girumon").DefaultIfEmpty(-1).First();
        // DECISÃO DA FATIA: Guilmon com a w_sword01 α (linha 25, SKILLPOINT 1: equipável com as skills iniciais). No jogo
        // ele começa com INI_WEAPON1 = 268 (adaga STAB, TSUYOSA 22) ativa e 275/274 de reserva; a espada fica mais forte.
        _weaponRow = _db["weapon"].RowsWhere("MODEL", "w_sword01").DefaultIfEmpty(-1).First();
        if (_shinkaRow < 0 || _weaponRow < 0) { _problems.Add("p_girumon ou w_sword01 não estão nas tabelas"); _db = null; return; }
        // FATO (GAMEDATAINIT 0x8036CFEC..D0B8): jogador novo com nível 1, EXP 0, as 9 skills em 1 e sem armaduras.
        Array.Fill(_skill, 1);
        Recalc();
        _hp = _pf!.MaxHp;
        _mp = _pf.MaxMp;
        if (!_db.Contains("pcTech")) _problems.Add("a pcTech não está no *_db.json: técnicas desligadas");
        var dropsDir = System.IO.Path.Combine(_dir, "drops");
        var dropsFile = System.IO.Directory.Exists(dropsDir) ? System.IO.Directory.EnumerateFiles(dropsDir, "*_drops.json").FirstOrDefault() : null;
        if (dropsFile == null) _problems.Add("falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\\drops): sem drops");
        else if (!Drops.FromJson(System.IO.File.ReadAllText(dropsFile)).TryGetValue(DropList, out _drops)) _problems.Add($"{DropList} não está no *_drops.json");
        _ef = _db.Enemy(EnemyRow);
        _enemyHp = _ef.MaxHp;
    }

    void Recalc()
    {
        _equip ??= new Equipment(_weaponRow);
        _pf = _db!.Player(_shinkaRow, _level, _equip, _skill);
        if (_db.Contains("pcTech")) _techs = Techniques.List(_db, _shinkaRow, _level, _skill);
    }

    // Tecla da técnica → painel (MP e recarga) → SQ_WAZA com o LABEL; o projétil ou a cura sai quando a sequência acaba.
    bool StartTechnique(int n)
    {
        if (_runner == null || _db == null || n >= _techs.Count) return false;
        var t = _techs[n];
        if (_panel.TryUse(t, ref _mp) is not { } label) { _lastEvent = $"{t.Name}: {(_mp < t.Mp ? "MP insuficiente" : "recarregando")}"; return false; }
        if (!_runner.Start("SQ_WAZA", label)) { _lastEvent = $"SQ_WAZA sem o bloco {label}"; ReleaseTechnique(t); return true; }
        _wazaPending = t;
        return true;
    }

    // HIPÓTESE: o projétil nasce quando a ação da técnica termina (quem gera o x_200fire01 no GENERATEWAZA não foi achado).
    void ReleaseTechnique(Technique t)
    {
        _lastEvent = t.Name;
        if (t.Stats.Hp > 0)
        {
            // Cura (heal*): HP da técnica somado ao jogador. HIPÓTESE: sem fórmula extra (o caso de cura não foi lido).
            int before = _hp;
            _hp = Math.Min(_pf!.MaxHp, _hp + t.Stats.Hp);
            Float(_player, $"+{_hp - before}", Colors.LightGreen);
            return;
        }
        if (t.Stats.Hp == 0) { _lastEvent += " (efeito de suporte ainda não ligado na fatia)"; return; }
        var dir = new Vector3(MathF.Sin(_move.Facing), 0, MathF.Cos(_move.Facing));
        var color = t.Stats.Element switch { 0 => Colors.OrangeRed, 1 => Colors.LightBlue, 2 => Colors.Yellow, 3 => Colors.MediumPurple, _ => Colors.White };
        var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = 2, Height = 4 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
        AddChild(mesh);
        mesh.GlobalPosition = _player.GlobalPosition + new Vector3(0, 4, 0) + dir * 4;
        _shots.Add((mesh, dir, 1000 / SequenceRunner.FramesPerSecond, t));
    }

    void UpdateShots(float dt)
    {
        // Mesma escala do "Move" 0,7 do Guilmon (HIPÓTESE): "Push" 1,5.
        float speed = _move.MaxSpeed * 1.5f / GuilmonMove;
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var (mesh, dir, life, tech) = _shots[i];
            life -= dt;
            mesh.GlobalPosition += dir * speed * dt;
            var c = mesh.GlobalPosition;
            bool hit = false;
            if (_enemy != null && !_enemyDead && !(_enemyStarted && _enemyRunner?.Invincible == true))
            {
                var e = _enemy.GlobalPosition;
                hit = Math.Abs(c.X - e.X) <= 2 + 4 && Math.Abs(c.Z - e.Z) <= 2 + 4 && c.Y + 2 >= e.Y && c.Y - 2 <= e.Y + 14;
            }
            if (hit) TechniqueStrikes(tech);
            if (hit || life <= 0) { mesh.QueueFree(); _shots.RemoveAt(i); continue; }
            _shots[i] = (mesh, dir, life, tech);
        }
    }

    // P2EISHITCHECK (técnica acerta sempre), P2EISCRITICAL e P2EDAMAGE no ramo técnica; código = 40000 + linha (HIPÓTESE).
    void TechniqueStrikes(Technique t)
    {
        if (_pf == null || _ef == null) return;
        int code = 40000 + t.Row;
        bool crit = DamageFormula.TechniqueCritical(t.Stats, _ef, _mt);
        var r = DamageFormula.Technique(_pf, _ef, t.Stats, crit, _mt);
        RaiseSkill(t.Stats.SkillIndex, technique: true);
        _enemyHp = Math.Max(0, _enemyHp - r.Damage);
        _hits++;
        Damage(_enemy!, r.Damage, onPlayer: false, crit ? $"{r.Damage}!" : r.Damage.ToString(), crit ? Colors.Orange : Colors.Gold);
        _lastEvent = $"{t.Name}: {r.Damage}{(crit ? " crítico" : "")} (código {code})";
        if (_enemyHp <= 0 && !_killed) { _killed = true; GainExp(); DropItem(); }
        if (_brain != null && _enemyRunner != null)
        {
            _brain.Force(DamageSequences[DamageFormula.Rank(r.Damage, _enemyHp, _ef.MaxHp)]);
            _enemyStarted = false;
        }
    }

    // hit_e_nume.lua com o FIGHTCALC: acerto, crítico, dano, skill, HP, faixa → sequência de reação do Numemon.
    string PlayerStrikes(int code)
    {
        if (_db == null || _pf == null || _ef == null) return "SQ_SDMG";
        if (!DamageFormula.PlayerHits(_pf, _ef, technique: false, new StatusMods(), _mt))
        {
            SkillUp();
            Float(_enemy!, "MISS", Colors.LightGray);
            _lastEvent = "Guilmon errou (Numemon defendeu)";
            return "SQ_GUARD";
        }
        bool crit = DamageFormula.PlayerCritical(_pf, _mt);
        var r = DamageFormula.Physical(_pf, _ef, crit, new StatusMods(), _mt);
        SkillUp();
        _enemyHp = Math.Max(0, _enemyHp - r.Damage);
        int rank = DamageFormula.Rank(r.Damage, _enemyHp, _ef.MaxHp);
        Damage(_enemy!, r.Damage, onPlayer: false, crit ? $"{r.Damage}!" : r.Damage.ToString(), crit ? Colors.Orange : Colors.White);
        _lastEvent = $"Guilmon: {r.Damage}{(crit ? " crítico" : "")}{(r.Strong ? " forte" : "")} (código {code})";
        if (_enemyHp <= 0 && !_killed) { _killed = true; GainExp(); DropItem(); }
        return DamageSequences[rank];
    }

    void SkillUp()
    {
        if (_pf == null) return;
        int idx = _pf.WeaponSkillNo;
        if (idx >= 0 && idx < WeaponGrow.Length) RaiseSkill(idx, technique: false);
    }

    // P2ESKILLUP: arma pelo <X>_GROW da shinka; técnica pelo índice SKILL da pcTech (6 ataque, 7 cura, 8 suporte).
    void RaiseSkill(int idx, bool technique)
    {
        if (_skillGrowth == null || _db == null || idx < 0 || idx >= _skill.Length) return;
        int grow = technique ? 0 : _db["shinka"].Int(_shinkaRow, WeaponGrow[idx]);
        int before = _skill[idx];
        _skill[idx] = _skillGrowth.Raise(before, _level, technique, grow, special: false, _mt);
        if (_skill[idx] != before) { Recalc(); Float(_player, $"SKILL {_skill[idx]}", Colors.Cyan); }
    }

    // Sorteio do drop (0x802FCC48) na posição do inimigo morto.
    void DropItem()
    {
        if (_enemy == null) return;
        // DW4_DROP="tabela:linha" (ex.: insItem:0): força o item, para conferir a coleta sem depender do sorteio.
        DropEntry? d = OS.GetEnvironment("DW4_DROP").Split(':') is [var tb, var rw] && int.TryParse(rw, out int forced)
            ? new DropEntry(new ItemRef(tb, forced), 0, 0) : _drops != null ? Drops.Roll(_drops, _mt) : null;
        if (d == null) return;
        string name = _db!.Contains(d.Item.Table) ? _db[d.Item.Table].Text(d.Item.Row, "NAME") : "";
        if (name.Length == 0) name = $"{d.Item.Table}:{d.Item.Row}";
        var node = new Node3D();
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3, 3, 3) }, Position = new Vector3(0, 1.5f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Gold, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        node.AddChild(new Label3D { Text = name, FontSize = 48, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, PixelSize = 0.05f, Position = new Vector3(0, 6, 0) });
        AddChild(node);
        node.GlobalPosition = _enemy.GlobalPosition;
        _ground.Add((node, d.Item));
        _lastEvent += $"  caiu {name}";
    }

    // Coleta a menos de 64 unidades (FATO). insItem tem efeito na hora; o resto vai para o inventário. HIPÓTESE da fatia:
    // tudo é pego por proximidade (no jogo, armas e armaduras só ganham dono; a confirmação não foi lida).
    void UpdateGround()
    {
        for (int i = _ground.Count - 1; i >= 0; i--)
        {
            var (node, item) = _ground[i];
            node.RotateY(0.03f);
            if (node.GlobalPosition.DistanceTo(_player.GlobalPosition) >= Drops.PickupRadius || _hp <= 0) continue;
            string text;
            if (item.Table == "insItem")
            {
                if (item.Row == 0)
                {
                    int bit = Drops.Bit(_level, _pf!.Bitritsu, _mt);
                    _inventory.Bit += bit;
                    text = $"+{bit} BIT";
                }
                else
                {
                    var (nouryoku, up) = Inventory.Instant(_db!, item.Row);
                    if (nouryoku is "" or "xdata") { _xdata += up; text = $"+{up} Xデータ"; }
                    else text = $"{nouryoku} +{up} (ainda não ligado)";
                }
            }
            else if (_inventory.Add(item)) text = _db![item.Table].Text(item.Row, "NAME");
            else { _lastEvent = "inventário cheio"; continue; }
            Float(_player, text, Colors.Gold);
            _lastEvent = $"pegou {text}";
            node.QueueFree();
            _ground.RemoveAt(i);
        }
    }

    void EquipLast()
    {
        if (_db == null || _equip == null || _inventory.Devices.Count == 0) return;
        var item = _inventory.Devices[^1];
        string name = _db[item.Table].Text(item.Row, "NAME");
        bool ok = item.Table switch
        {
            // FATO: DIGIMONNO = linha da shinka (SOUBI das armas exclusivas e INI_WEAPON1 das formas evoluídas).
            "weapon" => _db.CanEquipWeapon(item.Row, _shinkaRow, _skill),
            "armor1" or "armor2" => _db.CanEquipArmor(item.Table, item.Row, _level),
            _ => false,
        };
        if (!ok) { _lastEvent = $"não dá para equipar {name}"; return; }
        _equip = item.Table switch
        {
            "weapon" => _equip with { WeaponRow = item.Row },
            "armor1" => _equip with { Armor1Row = item.Row },
            _ => _equip with { Armor2Row = item.Row },
        };
        Recalc();
        _hp = Math.Min(_hp, _pf!.MaxHp);
        _mp = Math.Min(_mp, _pf.MaxMp);
        _lastEvent = $"equipou {name}";
    }

    void GainExp()
    {
        int gain = DamageFormula.ExpShare(_db!["enemy"].Int(EnemyRow, "EXP"), 0, players: 1, killer: true);
        _exp += gain;
        _lastEvent += $"  +{gain} EXP";
        bool up = false;
        while (_level < PlayerGrowth.MaxLevel && PlayerGrowth.NextExp(_level) <= _exp) { _level++; up = true; }
        if (!up) return;
        Recalc();
        _hp = _pf!.MaxHp; // P2EEXPUP: sobe de nível com HP e MP cheios
        _mp = _pf.MaxMp;
        Float(_player, $"NÍVEL {_level}", Colors.Yellow);
    }

    // Golpe do Numemon: código do HITBOX (70004) → linha da eneatk (HIPÓTESE forte), E2PISHITCHECK e E2PDAMAGE.
    string? EnemyStrikes(int code)
    {
        if (_db == null || _pf == null || _ef == null) return "SQ_SDMG";
        var atk = _db.EnemyAttackRow(code);
        if (!DamageFormula.EnemyHits(_ef, atk, _pf, _mt)) { Float(_player, "MISS", Colors.LightGray); return null; }
        var r = DamageFormula.EnemyDamage(_ef, atk, _pf, new StatusMods(), _mt);
        _hp = Math.Max(0, _hp - r.Damage);
        Damage(_player, r.Damage, onPlayer: true, r.Damage.ToString(), Colors.Red);
        _lastEvent = $"Numemon: {r.Damage} (eneatk {code % 10000})";
        return DamageSequences[DamageFormula.Rank(r.Damage, _hp, _pf.MaxHp)];
    }

    // Dano com o número do jogo (i_dmgnum no inimigo, i_pcdmgnum no jogador) na posição do modelo (HIPÓTESE: o jogo gera
    // o número ali; o dmgnmtrans já sobe 10). Sem o HUD exportado, cai no texto.
    void Damage(Node3D over, int value, bool onPlayer, string text, Color color)
    {
        if (_damageNumbers?.Show(onPlayer, value, over.GlobalPosition) == true) return;
        Float(over, text, color);
    }

    void Float(Node3D over, string text, Color color)
    {
        var label = new Label3D { Text = text, Modulate = color, FontSize = 64, OutlineSize = 12, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, PixelSize = 0.05f };
        AddChild(label);
        label.GlobalPosition = over.GlobalPosition + new Vector3(0, 18, 0);
        _floaters.Add((label, 1f));
    }

    void UpdateFloaters(float dt)
    {
        for (int i = _floaters.Count - 1; i >= 0; i--)
        {
            var (label, life) = _floaters[i];
            life -= dt;
            if (life <= 0) { label.QueueFree(); _floaters.RemoveAt(i); continue; }
            label.Position += new Vector3(0, 8 * dt, 0);
            _floaters[i] = (label, life);
        }
    }

    void UpdateEnemy(float dt)
    {
        if (_enemy == null) return;
        if (_enemyDead)
        {
            // DECISÃO DA FATIA (não é do original): o Numemon volta 5 s depois, no gerador, para continuar testando.
            _respawn -= dt;
            if (_respawn > 0) return;
            _enemyDead = false;
            _enemyHp = _ef?.MaxHp ?? 0;
            _killed = false;
            _enemy.Position = EnemyAt;
            _enemy.Visible = true;
            _enemy.ProcessMode = ProcessModeEnum.Inherit;
            _brain?.Release();
        }
        var vel = Vector3.Zero;
        var forward = new Vector3(MathF.Sin(_enemyYaw), 0, MathF.Cos(_enemyYaw));
        float Speed(float move) => _move.MaxSpeed * move / GuilmonMove;
        if (_brain != null && _enemyRunner != null)
        {
            var p = _enemy.GlobalPosition;
            var t = _player.GlobalPosition;
            _brain.Tick(dt, new NVector3(p.X, p.Y, p.Z), new NVector3(t.X, t.Y, t.Z), _enemyYaw);
            _enemyYaw = _brain.Yaw;
            forward = new Vector3(MathF.Sin(_enemyYaw), 0, MathF.Cos(_enemyYaw));
            if (_brain.External is { } ext)
            {
                // Sequência do jogo (SQ_ATCK1, SQ_FIRE, SQ_SDMG…); "common" é o bloco sem elemento das de dano.
                if (!_enemyStarted)
                {
                    _enemyStarted = _enemyRunner.Start(ext, "common");
                    _enemySerial = -1;
                    if (!_enemyStarted) _brain.Release();
                }
                else _enemyRunner.Tick(dt, false);
                if (_enemyStarted && !_enemyRunner.Running)
                {
                    _enemyStarted = false;
                    _brain.Release();
                    // SQ_DDMG termina em JUMP "SQ_ERAS" (depois do ERDE01 com FADESET "OUT"): o inimigo some.
                    if (_ef != null && _enemyHp <= 0) { _enemyDead = true; _respawn = 5; _enemy.Visible = false; _enemy.ProcessMode = ProcessModeEnum.Disabled; return; }
                }
            }
            if (_enemyStarted && _enemyRunner.Running)
            {
                if (_enemyRunner.Move is { } m)
                    vel = m.S(0) switch { "Move" => forward * Speed(m.F(1)), "Back" => -forward * Speed(m.F(1)), _ => Vector3.Zero };
                if (_enemyRunner.ActionSerial != _enemySerial)
                {
                    _enemySerial = _enemyRunner.ActionSerial;
                    _enemyHitThisAction.Clear();
                    if (_enemyRunner.Action != null) _sfx?.Action("e_nume", _enemyRunner.Action);
                    if (_enemyAnim != null && _enemyRunner.Action != null && EnemyAnimName(_enemyRunner.Action) is { } an)
                    {
                        _enemyAnim.GetAnimation(an).LoopMode = ActionAnimations.Loops(_enemyActions, _enemyRunner.Action) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                        _enemyAnim.Play(an, ActionAnimations.Blend(_enemyActions, _enemyRunner.Action) / SequenceRunner.FramesPerSecond);
                    }
                }
                CheckEnemyHits();
            }
            else
            {
                if (_brain.Walking) vel = forward * Speed(_enemyWalk);
                var want = _brain.Walking ? _enemyRun : _enemyWait;
                if (_enemyAnim != null && want != null && _enemyAnim.CurrentAnimation != want) _enemyAnim.Play(want, 8 / SequenceRunner.FramesPerSecond);
            }
        }
        else if (_enemyAnim != null && _enemyWait != null && !_enemyAnim.IsPlaying()) _enemyAnim.Play(_enemyWait);
        vel.Y = _enemy.IsOnFloor() ? -1f : _enemy.Velocity.Y - Gravity * dt;
        _enemy.Velocity = vel;
        _enemy.MoveAndSlide();
        if (_enemyModel != null) _enemyModel.Rotation = new Vector3(0, _enemyYaw, 0);
    }

    Vector3 EnemyBoxCenter(HitBox h) => _enemy!.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, _enemyYaw);

    // Caixas 70004 do Numemon (ataque, HIPÓTESE pelo SQ_ATCK1) contra a caixa de corpo do Guilmon (5 × 7 × 5, FATO).
    void CheckEnemyHits()
    {
        if (_enemyRunner == null || _runner?.Invincible == true) return;
        var g = _player.GlobalPosition;
        foreach (var h in _enemyRunner.ActiveHitBoxes().Where(h => h.Type == 70004))
        {
            if (_enemyHitThisAction.Contains(h)) continue;
            var c = EnemyBoxCenter(h);
            if (Math.Abs(c.X - g.X) <= h.Size.X / 2 + 2.5f && Math.Abs(c.Z - g.Z) <= h.Size.Z / 2 + 2.5f && c.Y + h.Size.Y / 2 >= g.Y && c.Y - h.Size.Y / 2 <= g.Y + 7)
            {
                _enemyHitThisAction.UnionWith(_enemyRunner.ActionHitBoxes);
                _hitsTaken++;
                string? reaction = EnemyStrikes(h.Type);
                if (reaction == null) break; // errou ou bloqueou: sem reação
                // Reação do Guilmon pela faixa do dano (bloco do estilo da arma, ou "common"), virado para quem bateu (HIPÓTESE).
                if (_runner != null && (_runner.Start(reaction, WeaponStyle) || _runner.Start(reaction, "common")))
                {
                    _wazaPending = null; // interrompida: o MP já foi gasto no painel
                    var to = _enemy!.GlobalPosition - g;
                    _move.Stop();
                    _move.Face(MathF.Atan2(to.X, to.Z));
                }
                break;
            }
        }
    }

    void DrawBoxes()
    {
        var active = new List<(Vector3 Center, float Yaw, HitBox Box)>();
        if (_showBoxes && _runner?.Running == true)
            active.AddRange(_runner.ActiveHitBoxes().Select(h => (BoxCenter(h), _move.Facing, h)));
        if (_showBoxes && _enemyStarted && _enemyRunner?.Running == true)
            active.AddRange(_enemyRunner.ActiveHitBoxes().Where(h => h.Type == 70004).Select(h => (EnemyBoxCenter(h), _enemyYaw, h)));
        while (_boxMeshes.Count < active.Count)
        {
            var m = new MeshInstance3D { Mesh = new BoxMesh(), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 0, 0, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } };
            AddChild(m);
            _boxMeshes.Add(m);
        }
        for (int i = 0; i < _boxMeshes.Count; i++)
        {
            _boxMeshes[i].Visible = i < active.Count;
            if (i >= active.Count) continue;
            var (center, yaw, h) = active[i];
            _boxMeshes[i].GlobalPosition = center;
            _boxMeshes[i].Rotation = new Vector3(0, yaw, 0);
            _boxMeshes[i].Scale = new Vector3(h.Size.X, h.Size.Y, h.Size.Z);
        }
    }

    // WAV PCM 16 bits com o chunk "smpl" do adx-wav (início e fim inclusivo do loop).
    AudioStreamWav? LoadWav(string file)
    {
        var b = System.IO.File.ReadAllBytes(file);
        if (b.Length < 12 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF") { _problems.Add($"{file}: não é WAV"); return null; }
        int channels = 0, rate = 0, loopStart = -1, loopEnd = -1;
        byte[]? data = null;
        for (int at = 12; at + 8 <= b.Length;)
        {
            string id = System.Text.Encoding.ASCII.GetString(b, at, 4);
            int size = BitConverter.ToInt32(b, at + 4);
            int body = at + 8;
            if (id == "fmt ") { channels = BitConverter.ToInt16(b, body + 2); rate = BitConverter.ToInt32(b, body + 4); }
            else if (id == "smpl" && BitConverter.ToInt32(b, body + 28) > 0) { loopStart = BitConverter.ToInt32(b, body + 44); loopEnd = BitConverter.ToInt32(b, body + 48) + 1; }
            else if (id == "data") data = b.AsSpan(body, Math.Min(size, b.Length - body)).ToArray();
            at = body + size + (size & 1);
        }
        if (data == null || channels == 0) { _problems.Add($"{file}: WAV sem fmt ou data"); return null; }
        var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = channels == 2, Data = data };
        if (loopStart >= 0) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = loopStart; w.LoopEnd = loopEnd; }
        return w;
    }

    Node3D? Load(string sub, string fileName)
    {
        var root = System.IO.Path.Combine(_dir, sub);
        var file = System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, fileName, System.IO.SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null) { _problems.Add($"falta {fileName} em {root}"); return null; }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(file, state);
        if (err != Error.Ok) { _problems.Add($"{file}: {err}"); return null; }
        return doc.GenerateScene(state) as Node3D;
    }

    static StaticBody3D MakeStaticBody(Node3D collision)
    {
        var body = new StaticBody3D();
        foreach (var mi in FindAll<MeshInstance3D>(collision))
            body.AddChild(new CollisionShape3D { Shape = mi.Mesh.CreateTrimeshShape(), Transform = mi.GlobalTransformIn(collision) });
        collision.QueueFree();
        return body;
    }

    static string? Pick(string[] names, string exact, string contains) =>
        names.FirstOrDefault(n => n == exact) ?? names.FirstOrDefault(n => n.Contains(contains, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        if (n is T t) yield return t;
        foreach (var c in n.GetChildren())
            foreach (var x in FindAll<T>(c)) yield return x;
    }
}

static class NodeExtensions
{
    /// <summary>Transformação de um nó relativa a um ancestral, sem depender de estar na árvore.</summary>
    public static Transform3D GlobalTransformIn(this Node3D node, Node3D ancestor)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != ancestor; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }
}
