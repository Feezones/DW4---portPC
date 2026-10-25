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
    // Onde a fase começou (ponto de entrada ou DW4_INICIO): o F5 volta para cá. Último ponto no chão: rede contra queda.
    Vector3 _spawn, _lastFloor;
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
    int _hits;
    int _hitsTaken;
    readonly Random _rng = new();
    // Combate com as tabelas e fórmulas do jogo (GameTables, DamageFormula, PlayerGrowth, SkillGrowth). Sorteio: o MT19937
    // do jogo com a semente do construtor (5150). A IA continua com o Random do .NET (o sorteio dela não foi lido).
    GameTables? _db;
    SkillGrowth? _skillGrowth;
    readonly MersenneTwister _mt = new();
    Fighter? _pf;
    int _shinkaRow = -1, _weaponRow = -1, _level = 1, _hp;
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
    // Drops (Drops, Inventory): FATO (beGeneraterPlayerData): cada gerador aponta, por número de jogadores, para 3 listas (uma por
    // dificuldade, nZ1/hZ1/vZ1) de drop por inimigo morto. A fatia desenha uma caixa com o nome no lugar do modelo do item.
    // Sem gerador (o Numemon fixo da fatia), vale a lista do Numemon normal.
    const string DropList = "nZ1nume1";
    Dictionary<string, List<DropEntry>> _dropLists = [];
    readonly Dictionary<string, string?[][]> _dropOfGenerator = [];
    // O inventário é da GAMERAM (registro do jogador 0): sobrevive à troca de fase e os SYSctl*Folder dos scripts o usam.
    static Inventory _inventory => Ram.Players[0].Items;
    int _xdata;
    // Equipamento (RECALCPLAYER completo). DECISÃO DA FATIA: sem o menu de status, a tecla E equipa o último equipamento
    // pego, se a regra do jogo deixar; a arma muda só os números (o modelo na mão continua o da w_sword01).
    Equipment? _equip;
    SoundEffects? _sfx;
    readonly List<(Node3D Node, ItemRef Item)> _ground = [];
    // Linha do Numemon na tabela enemy: Int@7 dos geradores da dng1 no modo normal (generatorN #184 = 13; HIPÓTESE forte).
    const int EnemyRow = 13;
    string _lastEvent = "";
    readonly List<(Label3D Label, float Life)> _floaters = [];
    static readonly string[] WeaponGrow = ["BLUNT_GROW", "SLASH_GROW", "STAB_GROW", "BASH_GROW", "SHOT_GROW", "CRUSH_GROW"];
    static readonly string[] DamageSequences = ["SQ_SDMG", "SQ_MDMG", "SQ_LDMG", "SQ_DDMG"];
    // DW4_DEMO=1: começa ao lado do Numemon, virado para ele, e aperta ataque sozinho (para capturas sem controle).
    bool _demo;
    int _tick;
    readonly List<MeshInstance3D> _boxMeshes = [];
    Camera3D _camera = null!;
    Label _hud = null!;
    // HUD do jogo (Hud.cs): painel de status do jogador 1 e números de dano i_dmgnum/i_pcdmgnum, de <fatia>hud.
    HudOverlay? _overlay;
    DamageNumbers? _damageNumbers;
    readonly List<string> _problems = [];
    // Neblina da fase (cor, início, fim), do fase.json; null sem neblina.
    (Color Color, float Begin, float End)? _fog;
    int _problemsLogged;

    // FATO (colisão #1862): sobre as vagas do grupo 0 (y 40) há uma peça de colisão de 10 de altura (topo em y 50); nascer
    // dentro dela derruba o Guilmon pelo chão. HIPÓTESE: o jogo assenta o jogador no chão acima do ponto. No primeiro quadro
    // de física, um raio de y + 60 para baixo põe o jogador no primeiro chão.
    bool _snapToFloor;
    // Foco da janela pelas notificações do SO (começa com foco; só perde quando o SO avisa que saiu).
    bool _hasFocus = true;

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest && !_leaving) SaveProgress();
        if (what == NotificationApplicationFocusOut) _hasFocus = false;
        else if (what == NotificationApplicationFocusIn) _hasFocus = true;
    }
    // Troca de fase pedida: a cena vai ser recarregada, a física para de rodar (evita !is_inside_tree).
    bool _leaving;
    // Gatilhos de área só depois do assentamento no chão e de um quadro inteiro na fase (posição velha disparava matt01).
    int _volumeGrace = 2;

    void SnapToFloor()
    {
        _snapToFloor = false;
        var p = _player.GlobalPosition;
        var query = PhysicsRayQueryParameters3D.Create(p + new Vector3(0, 60, 0), p - new Vector3(0, 200, 0));
        query.Exclude = [_player.GetRid()];
        query.HitBackFaces = false; // só chão (face para cima), não o lado de cima de um teto
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) _player.GlobalPosition = (Vector3)hit["position"];
        _player.Velocity = Vector3.Zero;
    }

    (Vector3 Position, float RotYDeg)? EntryPoint(int group)
    {
        var file = System.IO.Path.Combine(_dir, "pontos_entrada.json");
        if (!System.IO.File.Exists(file)) { _problems.Add($"falta {file} (dw4 igb-entradas ndmw.afs 179 <fatia>): início no ponto fixo"); return null; }
        using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
        foreach (var pt in doc.RootElement.GetProperty("points").EnumerateArray())
        {
            if (pt.GetProperty("index").GetInt32() != group) continue;
            var slot = pt.GetProperty("slots").EnumerateArray().First();
            var p = slot.GetProperty("position");
            return (new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle()), slot.GetProperty("rotYDeg").GetSingle());
        }
        _problems.Add($"pontos_entrada.json sem o grupo {group}");
        return null;
    }

    public override void _Ready()
    {
        ChooseStage();
        var env = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.65f), BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black };
        // FATO (stag0102 #621): igFogAttr 60..300 com cor (0,8 0,7 0,8). Fundo na cor da neblina e neblina por profundidade.
        if (_fog is { } fg)
        {
            env.BackgroundColor = fg.Color;
            env.FogEnabled = true;
            env.FogMode = Godot.Environment.FogModeEnum.Depth;
            env.FogLightColor = fg.Color;
            env.FogDepthBegin = fg.Begin;
            env.FogDepthEnd = fg.End;
            env.FogDepthCurve = 1;
        }
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, 30, 0) });

        if (Load("cenario", "cena.gltf") is { } scenery) { ApplyAdditive(scenery); AddChild(scenery); }
        if (Load("colisao", "colisao.gltf") is { } collision) AddChild(MakeStaticBody(collision));

        _demo = OS.GetEnvironment("DW4_DEMO") == "1";
        _showBoxes = _demo;
        var start = _demo ? EnemyAt + new Vector3(-16, 0, 0) : Start;
        // Ponto de entrada do jogo (bePoint01Info do #179, pontos_entrada.json de dw4 igb-entradas): grupo DW4_ENTRADA (0 = do
        // campo/lobby), vaga do jogador 0, virado como a matriz do ponto. Sem o arquivo, o ponto fixo antigo.
        if (!_demo && EntryPoint(int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var entry) ? entry : 0) is { } ep)
        {
            start = ep.Position;
            _move.Face(Mathf.DegToRad(ep.RotYDeg));
            _snapToFloor = true;
        }
        // DW4_INICIO="x,y,z": ponto inicial para capturas (ex.: conferir a câmera de um volume).
        if (OS.GetEnvironment("DW4_INICIO").Split(',') is { Length: 3 } xyz
            && xyz.Select(t => float.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : float.NaN).ToArray() is var v3
            && v3.All(float.IsFinite))
            start = new Vector3(v3[0], v3[1], v3[2]);
        _spawn = _lastFloor = start;
        _player = new CharacterBody3D { Position = start, FloorSnapLength = 4f };
        _player.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 2.5f, Height = 7f }, Position = new Vector3(0, 3.5f, 0) });
        AddChild(_player);
        _model = Load(PlayerFolder, "personagem.gltf");
        if (_model != null)
        {
            _player.AddChild(_model);
            _anim = FindAll<AnimationPlayer>(_model).FirstOrDefault();
            if (_anim != null)
            {
            }
            var seqFile = SeqFile(PlayerFolder);
            if (seqFile == null) _problems.Add($"falta o *_seq.json de {PlayerModel} em {PlayerFolder} (dw4 igb-seq)");
            else
            {
                var json = System.IO.File.ReadAllText(seqFile);
                _actions = SeqCommand.LoadActions(json);
                if (_actions.Count == 0) _problems.Add("o *_seq.json não tem ações: regere com o igb-seq atual");
                var sequences = SeqCommand.LoadAll(json);
                _runner = new SequenceRunner(sequences, AnimFrames);
                SetupJump(_runner);
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

        LoadCombat();
        LoadStageData();
        // Sem geradores: o Numemon fixo do GENE-05 que renasce (fatia anterior).
        // Só na fatia antiga (sem fase exportada): uma fase exportada sem geradores (dngq5b1*, stag0000) simplesmente não tem.
        if (_generators.Count == 0 && _dir == _common) SpawnEnemy(EnemyRow, EnemyAt, -MathF.PI / 2, null, respawns: true);
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
                if (LoadWav(wav) is { } stream) { var p = new AudioStreamPlayer { Stream = stream, Bus = PortSettings.EffectsBus }; AddChild(p); p.Play(); }
            }
        }

        // Câmeras por volume da fase (fase.json); campos sem aaacamera ficam com a câmera de seguir (HIPÓTESE da fatia).
        _cameraSets = _cameraFiles
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
        if (MenuInput(e)) { GetViewport().SetInputAsHandled(); return; }
        if (e is InputEventKey { Pressed: true, Echo: false } k)
        {
            if (k.Keycode == Key.F1 && _cameraSets.Length > 1) _cameraSet = (_cameraSet + 1) % _cameraSets.Length;
            if (k.Keycode == Key.F2) _showBoxes = !_showBoxes;
            if (k.Keycode == Key.F5) { _player.Position = _spawn; _snapToFloor = !_demo; _player.Velocity = Vector3.Zero; _move.Stop(); _hp = _pf?.MaxHp ?? 0; _mp = _pf?.MaxMp ?? 0; }
            if (k.Keycode is >= Key.Key1 and <= Key.Key6) _techKey = (int)(k.Keycode - Key.Key1);
            if (k.Keycode == Key.K) _techKey = 0;
            if (k.Keycode == Key.E) EquipLast();
            if (k.Keycode == Key.F9 && OS.IsDebugBuild()) DebugWeakenBoss();
            if (k.Keycode == Key.F8 && OS.IsDebugBuild()) DebugNextTarget();
            if (k.Keycode == Key.Escape) LeaveGame();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (_leaving || !IsInsideTree()) return;
        if (_snapToFloor) SnapToFloor();
        if (TickMenu(dt)) { _jumpHeld = true; return; } // o botão que fechou o menu não vira pulo
        if (TickShop()) { _jumpHeld = true; return; } // loja aberta: o jogador fica parado
        _stage?.Tick();
        WatchDemo();
        if (_leaving) return;
        TickStageChange(dt);
        if (_leaving) return;
        TickObjects(dt);
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
        bool jumpDown = JumpDown();
        _tick++;
        if (_demo)
        {
            attackDown = _tick % 15 < 2 && _tick > 30; // um toque a cada 0,25 s depois de 0,5 s
            if (_tick < 3) input = new Vector2(0, 0);
        }
        // Decisão do port PC: sem foco na janela, nada do controle/teclado move o jogador (controle pareado dava entrada fantasma).
        if (!_demo && !_hasFocus) { input = Vector2.Zero; dir = Vector3.Zero; attackDown = jumpDown = false; }
        if (_pf != null && _hp <= 0) { input = Vector2.Zero; dir = Vector3.Zero; attackDown = jumpDown = false; } // caído: F5 reinicia
        // Só vale um toque que começou depois de abrir a mensagem (o ataque que abriu a conversa ainda pode estar apertado).
        if (!attackDown) _confirmArmed = true;
        _confirmDown = attackDown && _confirmArmed;
        if (_confirmDown) _selectEnded = true;
        _menuInput = input;
        bool attackRaw = attackDown;
        bool jumpPressed = jumpDown && !_jumpHeld;
        _jumpHeld = jumpDown; // segurado com o controle travado não vira pulo quando ele volta
        if (!_padEnabled || !_playerCtrl) { input = Vector2.Zero; dir = Vector3.Zero; attackDown = jumpPressed = false; _techKey = -1; } // STAGECTL PADINPUT DISABLE do script
        bool attackPressed = attackDown && !_attackHeld;
        // Com o controle bloqueado o botão continua "segurado": o toque que fechou a mensagem não vira ataque quando o controle volta.
        _attackHeld = attackDown || attackRaw;
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
        else if (attacking) _runner!.Tick(dt, attackPressed && !wasWaza, attackDown && !wasWaza);
        // O golpe carregado: o SQ_ATCK1 termina em JUMP "SQ_ATCKTM" quando o ataque é solto depois de carregar.
        if (attacking && _runner is { Running: false, Next: SequenceRunner.ChargedSequence }) _runner.Start(SequenceRunner.ChargedSequence, WeaponStyle);
        if (!attacking && TryStartAir(jumpPressed)) attacking = true;
        _techKey = -1;
        _panel.Tick(dt);
        if (_wazaPending is { } waza && _runner?.Running != true) { _wazaPending = null; ReleaseTechnique(waza); }
        if (InAirSequence) dir *= _airControl;
        else if (attacking) { dir = Vector3.Zero; _move.Stop(); }
        if (_demo && _tick < 30 && _enemies.FirstOrDefault() is { } first)
        {
            var to = first.Body.GlobalPosition - _player.GlobalPosition;
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
        vel.Y = VerticalVelocity(vel.Y, dt);
        _player.Velocity = vel;
        _player.MoveAndSlide();
        // DECISÃO DO PORT PC: rede de segurança. Se atravessar o chão (empurrão forte numa borda, ponto inicial dentro de
        // uma parede), volta ao último ponto em que estava no chão em vez de cair para sempre.
        _airTime = _player.IsOnFloor() ? 0 : _airTime + dt;
        if (_player.IsOnFloor()) _lastFloor = _player.GlobalPosition;
        else if (_player.GlobalPosition.Y < _lastFloor.Y - 400) { _player.GlobalPosition = _lastFloor; _player.Velocity = Vector3.Zero; }
        if (_model != null) _model.Rotation = new Vector3(0, _move.Facing, 0);

        if (_runner?.Running == true)
        {
            if (_runner.ActionSerial != _lastSerial)
            {
                _lastSerial = _runner.ActionSerial;
                _enemiesHitThisAction.Clear();
                _objectsHitThisAction.Clear();
                CheckTalkProbe();
                if (_runner.Action != null) _sfx?.Action(PlayerModel, _runner.Action);
                if (_anim != null && _runner.Action != null && AnimName(_runner.Action) is { } an)
                {
                    _anim.GetAnimation(an).LoopMode = ActionAnimations.Loops(_actions, _runner.Action) ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                    _anim.Play(an, ActionAnimations.Blend(_actions, _runner.Action) / SequenceRunner.FramesPerSecond);
                }
            }
            CheckHits();
            CheckObjectHits();
        }
        else
        {
            bool moving = v.LengthSquared() > 1f;
            var want = moving ? _animRun : _animWait;
            if (_anim != null && want != null && _anim.CurrentAnimation != want) _anim.Play(want, 0.13f); // ~8 quadros a 60 Hz (MOTIONSET Float@4 = 8, hipótese)
        }
        TickVolumes();
        if (!_generatorsFrozen) TickGenerators();
        if (!_enemiesFrozen) UpdateEnemies(dt);
        _minimap?.Update(dt, _player.GlobalPosition, GetViewport().GetVisibleRect().Size);
        UpdateFloaters(dt);
        _damageNumbers?.Update(dt);
        if (_pf != null) _overlay?.Update(dt, _hp, _pf.MaxHp, _mp, _pf.MaxMp, _level);
        UpdateShots(dt);
        UpdateGround();
        _sfx?.Tick(dt);
        DrawBoxes();

        UpdateCamera(dt);
    }

    void UpdateCamera(float dt)
    {
        while (_problemsLogged < _problems.Count) GD.Print($"[problema] {_problems[_problemsLogged++]}"); // também no log (passadas)
        DebugKeepAim();
        var p = _player.GlobalPosition;
        if (_cameraDemo != null) UpdateCameraDemo(dt);
        else if (_cameraSets.Length == 0)
        {
            _camera.LookAtFromPosition(p + new Vector3(0, 80, 110), p, Vector3.Up);
        }
        else
        {
            _camera.KeepAspect = Camera3D.KeepAspectEnum.Height;
            _camera.Fov = 45;
            var set = _cameraSets[_cameraSet];
            var target = new NVector3(p.X, p.Y, p.Z);
            _zone = set.Select(target, _zone);
            var (pos, look) = set.Pose(_zone, target);
            _camera.LookAtFromPosition(new Vector3(pos.X, pos.Y, pos.Z), new Vector3(look.X, look.Y, look.Z), Vector3.Up);
        }
        _hud.Text = $"pos ({p.X:0.0}, {p.Y:0.0}, {p.Z:0.0})  vel {_move.Velocity.Length():0.0} u/s\n" +
            $"câmera {(_cameraSet < _cameraFiles.Length ? _cameraFiles[_cameraSet].Replace("_camera.json", "") : "-")}{(_cameraSet == 1 ? " (afastada)" : "")} volume {_zone}  [F1 troca, F5 reinicia, Esc sai]\n" +
            $"animações: parado = {_animWait ?? "-"}, correndo = {_animRun ?? "-"}\n" +
            $"ataque [J, clique ou X]: {(_runner?.Running == true ? $"{_runner.Action} → {(_runner.Action == null ? "-" : AnimName(_runner.Action) ?? "sem animação")} quadro {_runner.ActionFrame:0}/{_runner.ActionLength:0}" : "-")}  acertos no Numemon: {_hits}  [F2 caixas]\n" +
            $"Numemon: {(_lastEnemy?.Brain is not { } br ? "-" : $"{br.Map.Name}, {(br.External ?? br.Current?.Sequence ?? "-")}")}{(_lastEnemy?.Runner?.Running == true ? $" ({_lastEnemy.Runner.Action})" : "")}  golpes no Guilmon: {_hitsTaken}\n" +
            (_pf == null ? "combate: sem tabelas (dw4 igb-db area.afs 3 <fatia>\\db)\n" :
                $"{FormName} nv {_level}  HP {_hp}/{_pf.MaxHp}  MP {_mp}/{_pf.MaxMp}  EXP {_exp}/{PlayerGrowth.NextExp(_level)}  SLASH {_skill[1]}  |  {_lastEnemy?.Kind.Name ?? "inimigo"} HP {(_lastEnemy is { } le ? (le.Dead || le.Killed ? "derrotado" : $"{le.Hp}/{le.Kind.Stats?.MaxHp}") : "-")} (vivos {_enemies.Count(x => !x.Dead)})  |  {_lastEvent}{(_hp <= 0 ? "  |  CAIU: F5 reinicia" : "")}\n" +
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

    string? SeqFile(string sub)
    {
        var root = StagePath(sub);
        return System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, "*_seq.json", System.IO.SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    float? AnimFrames(string action) => ActionAnimations.Length(_actions, action, motion =>
        _anim != null && _anim.HasAnimation(motion) ? (float)_anim.GetAnimation(motion).Length * SequenceRunner.FramesPerSecond : null);

    // Caixa local (x lateral, y altura, z à frente) no mundo, girada pela direção do personagem.
    Vector3 BoxCenter(HitBox h) => _player.GlobalPosition + new Vector3(h.Position.X, h.Position.Y, h.Position.Z).Rotated(Vector3.Up, _move.Facing);

    void LoadCombat()
    {
        var dbDir = System.IO.Path.Combine(_dir, "db");
        var dbFile = System.IO.Directory.Exists(dbDir) ? System.IO.Directory.EnumerateFiles(dbDir, "*_db.json").FirstOrDefault() : null;
        if (dbFile == null) { _problems.Add("falta o *_db.json (dw4 igb-db area.afs 3 <fatia>\\db): combate desligado"); return; }
        _db = GameTables.FromJson(System.IO.File.ReadAllText(dbFile));
        var skillFile = StagePath("skillup.json");
        if (System.IO.File.Exists(skillFile)) _skillGrowth = SkillGrowth.FromJson(System.IO.File.ReadAllText(skillFile));
        else _problems.Add("falta skillup.json (dw4 dol-skilltab <main.dol> <fatia>): skill não sobe");
        _shinkaRow = _db["shinka"].RowsWhere("MODEL_NAME", PlayerModel).DefaultIfEmpty(-1).First();
        // DECISÃO DA FATIA: Guilmon com a w_sword01 α (linha 25, SKILLPOINT 1: equipável com as skills iniciais). No jogo
        // ele começa com INI_WEAPON1 = 268 (adaga STAB, TSUYOSA 22) ativa e 275/274 de reserva; a espada fica mais forte.
        _weaponRow = _db["weapon"].RowsWhere("MODEL", "w_sword01").DefaultIfEmpty(-1).First();
        if (_shinkaRow < 0 || _weaponRow < 0) { _problems.Add($"{PlayerModel} ou w_sword01 não estão nas tabelas"); _db = null; return; }
        // FATO (GAMEDATAINIT 0x8036CFEC..D0B8): jogador novo com nível 1, EXP 0, as 9 skills em 1 e sem armaduras.
        Array.Fill(_skill, 1);
        LoadProgress();
        Recalc();
        _hp = _pf!.MaxHp;
        _mp = _pf.MaxMp;
        if (!_db.Contains("pcTech")) _problems.Add("a pcTech não está no *_db.json: técnicas desligadas");
        var dropsDir = System.IO.Path.Combine(_dir, "drops");
        var dropsFile = System.IO.Directory.Exists(dropsDir) ? System.IO.Directory.EnumerateFiles(dropsDir, "*_drops.json").FirstOrDefault() : null;
        if (dropsFile == null) { if (_dir == _common) _problems.Add("falta o *_drops.json (dw4 igb-drops ndmw.afs 183 <fatia>\\drops): sem drops"); }
        else
        {
            var dropsJson = System.IO.File.ReadAllText(dropsFile);
            _dropLists = Drops.FromJson(dropsJson);
            using var dd = System.Text.Json.JsonDocument.Parse(dropsJson);
            if (dd.RootElement.TryGetProperty("generators", out var gens))
                foreach (var g in gens.EnumerateObject())
                    _dropOfGenerator[g.Name] = g.Value.EnumerateArray().Select(p => p.GetProperty("drop").EnumerateArray().Select(n => n.GetString()).ToArray()).ToArray();
            else _problems.Add("o *_drops.json não liga gerador a lista (regere com o fase-exportar atual): só a lista do Numemon");
        }
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
            var target = _enemies.FirstOrDefault(en =>
            {
                if (!Vulnerable(en)) return false;
                var e = en.Body.GlobalPosition;
                return Math.Abs(c.X - e.X) <= 2 + 4 && Math.Abs(c.Z - e.Z) <= 2 + 4 && c.Y + 2 >= e.Y && c.Y - 2 <= e.Y + 14;
            });
            bool hit = target != null;
            if (target != null) TechniqueStrikes(target, tech);
            if (hit || life <= 0) { mesh.QueueFree(); _shots.RemoveAt(i); continue; }
            _shots[i] = (mesh, dir, life, tech);
        }
    }

    // P2EISHITCHECK (técnica acerta sempre), P2EISCRITICAL e P2EDAMAGE no ramo técnica; código = 40000 + linha (HIPÓTESE).
    void TechniqueStrikes(Enemy e, Technique t)
    {
        if (_pf == null || e.Kind.Stats is not { } ef) return;
        int code = 40000 + t.Row;
        bool crit = DamageFormula.TechniqueCritical(t.Stats, ef, _mt);
        var r = DamageFormula.Technique(_pf, ef, t.Stats, crit, _mt);
        RaiseSkill(t.Stats.SkillIndex, technique: true);
        e.Hp = Math.Max(0, e.Hp - r.Damage);
        _hits++;
        _lastEnemy = e;
        Damage(e.Body, r.Damage, onPlayer: false, crit ? $"{r.Damage}!" : r.Damage.ToString(), crit ? Colors.Orange : Colors.Gold);
        _lastEvent = $"{t.Name}: {r.Damage}{(crit ? " crítico" : "")} (código {code})";
        if (e.Hp <= 0 && !e.Killed) { e.Killed = true; GainExp(e); DropItem(e); }
        if (e.Brain != null && e.Runner != null)
        {
            e.Brain.Force(DamageSequences[DamageFormula.Rank(r.Damage, e.Hp, ef.MaxHp)]);
            e.Started = false;
        }
    }

    // hit_e_nume.lua com o FIGHTCALC: acerto, crítico, dano, skill, HP, faixa → sequência de reação do Numemon.
    string PlayerStrikes(Enemy e, int code)
    {
        if (_db == null || _pf == null || e.Kind.Stats is not { } ef) return "SQ_SDMG";
        if (!DamageFormula.PlayerHits(_pf, ef, technique: false, new StatusMods(), _mt))
        {
            SkillUp();
            Float(e.Body, "MISS", Colors.LightGray);
            _lastEvent = $"Guilmon errou ({e.Kind.Name} defendeu)";
            return "SQ_GUARD";
        }
        bool crit = DamageFormula.PlayerCritical(_pf, _mt);
        var r = DamageFormula.Physical(_pf, ef, crit, new StatusMods(), _mt);
        SkillUp();
        e.Hp = Math.Max(0, e.Hp - r.Damage);
        int rank = DamageFormula.Rank(r.Damage, e.Hp, ef.MaxHp);
        Damage(e.Body, r.Damage, onPlayer: false, crit ? $"{r.Damage}!" : r.Damage.ToString(), crit ? Colors.Orange : Colors.White);
        _lastEvent = $"Guilmon: {r.Damage}{(crit ? " crítico" : "")}{(r.Strong ? " forte" : "")} (código {code})";
        if (e.Hp <= 0 && !e.Killed) { e.Killed = true; GainExp(e); DropItem(e); }
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

    // Sorteio do drop (0x802FCC48) na posição do inimigo morto. HIPÓTESE da fatia: só a lista do Numemon (nZ1nume1) é
    // conhecida; a ligação inimigo → lista de drop dos outros ainda não foi lida.
    void DropItem(Enemy e)
    {
        // Lista do gerador do inimigo: [jogadores − 1][dificuldade].
        string? listName = e.Gen != null && _dropOfGenerator.TryGetValue(e.Gen.Name, out var perPlayers) && perPlayers.Length > 0
            ? perPlayers[Math.Clamp(Ram.Ninzu, 1, perPlayers.Length) - 1].ElementAtOrDefault(Math.Clamp(Ram.Difficulty, 0, 2)) : e.Gen == null ? DropList : null;
        var drops = listName != null ? _dropLists.GetValueOrDefault(listName) : null;
        if (drops == null && OS.GetEnvironment("DW4_DROP").Length == 0) return;
        var at = e.Body.GlobalPosition;
        // DW4_DROP="tabela:linha" (ex.: insItem:0): força o item, para conferir a coleta sem depender do sorteio.
        DropEntry? d = OS.GetEnvironment("DW4_DROP").Split(':') is [var tb, var rw] && int.TryParse(rw, out int forced)
            ? new DropEntry(new ItemRef(tb, forced), 0, 0) : drops != null ? Drops.Roll(drops, _mt) : null;
        if (d == null) return;
        string name = _db!.Contains(d.Item.Table) ? _db[d.Item.Table].Text(d.Item.Row, "NAME") : "";
        if (name.Length == 0) name = $"{d.Item.Table}:{d.Item.Row}";
        var node = new Node3D();
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3, 3, 3) }, Position = new Vector3(0, 1.5f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Colors.Gold, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded } });
        node.AddChild(new Label3D { Text = name, FontSize = 48, OutlineSize = 10, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, PixelSize = 0.05f, Position = new Vector3(0, 6, 0) });
        AddChild(node);
        node.GlobalPosition = at;
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

    void GainExp(Enemy e)
    {
        int gain = DamageFormula.ExpShare(_db!["enemy"].Int(e.Kind.Row, "EXP"), 0, players: 1, killer: true);
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

    // Golpe de inimigo ou objeto: código do HITBOX (70004 no Numemon) → linha da eneatk (HIPÓTESE forte), E2PISHITCHECK e
    // E2PDAMAGE. Sem atacante (piso de dano, TYPE 2): o golpe não depende dos números dele, usa o próprio jogador no lugar.
    string? EnemyStrikes(int code, Fighter? attacker, string who)
    {
        if (_db == null || _pf == null) return "SQ_SDMG";
        var ef = attacker ?? _pf;
        var atk = _db.EnemyAttackRow(code);
        if (!DamageFormula.EnemyHits(ef, atk, _pf, _mt)) { Float(_player, "MISS", Colors.LightGray); return null; }
        var r = DamageFormula.EnemyDamage(ef, atk, _pf, new StatusMods(), _mt);
        _hp = Math.Max(0, _hp - r.Damage);
        Damage(_player, r.Damage, onPlayer: true, r.Damage.ToString(), Colors.Red);
        _lastEvent = $"{who}: {r.Damage} (eneatk {code % 10000})";
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

    void DrawBoxes()
    {
        var active = new List<(Vector3 Center, float Yaw, HitBox Box)>();
        if (_showBoxes && _runner?.Running == true)
            active.AddRange(_runner.ActiveHitBoxes().Select(h => (BoxCenter(h), _move.Facing, h)));
        if (_showBoxes)
            foreach (var e in _enemies.Where(e => e.Started && e.Runner?.Running == true))
                active.AddRange(e.Runner!.ActiveHitBoxes().Where(h => h.Type / 10000 == 7).Select(h => (EnemyBoxCenter(e, h), e.Yaw, h)));
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
        var root = StagePath(sub);
        var file = System.IO.Directory.Exists(root)
            ? System.IO.Directory.EnumerateFiles(root, fileName, System.IO.SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null) { _problems.Add($"falta {fileName} em {root}"); return null; }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(file, state);
        if (err != Error.Ok) { _problems.Add($"{file}: {err}"); return null; }
        return doc.GenerateScene(state) as Node3D;
    }

    // Materiais "_add" do exportador = igBlendFunctionAttr SRC_ALPHA/ONE do jogo (luzes, brilhos): mistura aditiva, sem luz e
    // sem gravar profundidade (os atores com essa mistura também têm igDepthWriteStateAttr).
    static void ApplyAdditive(Node root)
    {
        foreach (var mi in FindAll<MeshInstance3D>(root))
            for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D m && m.ResourceName.EndsWith("_add", StringComparison.Ordinal))
                {
                    m.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
                    m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                    m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
                    m.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled;
                }
    }

    // Cada malha vira uma forma; o caminho de nomes fica guardado para o HITLANDMODEL dos scripts ligar e desligar partes
    // (doorwall01..03 da dng1, Fatia.Scripts.cs).
    readonly List<(string Path, CollisionShape3D Shape)> _collisionShapes = [];

    StaticBody3D MakeStaticBody(Node3D collision)
    {
        var body = new StaticBody3D();
        foreach (var mi in FindAll<MeshInstance3D>(collision))
        {
            var shape = new CollisionShape3D { Shape = mi.Mesh.CreateTrimeshShape(), Transform = mi.GlobalTransformIn(collision) };
            body.AddChild(shape);
            var path = new List<string>();
            for (Node? n = mi; n != null && n != collision; n = n.GetParent()) path.Add(n.Name);
            _collisionShapes.Add((string.Join("/", path), shape));
        }
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
