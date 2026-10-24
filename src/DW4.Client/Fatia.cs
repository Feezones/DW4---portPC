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
///   dw4 igb-gltf ndmw.afs 857 &lt;fatia&gt;\guilmon · dw4 igb-gltf ndmw.afs 179 &lt;fatia&gt;\numemon --ator=e_nume · dw4 igb-camera ndmw.afs 180 e 181 &lt;fatia&gt;
/// </summary>
public partial class Fatia : Node3D
{
    // PROVISÓRIO até medir no jogo: ponto inicial (gerador GENE-03 da dng1; o PLAYERPOINT real ainda não foi achado)
    // e gravidade. Caixa de corpo do Guilmon (beModelCtrlInfoDataHit → igAABox): 5 × 7 × 5, FATO.
    static readonly Vector3 Start = new(-180, 45, 60);
    // Numemon parado no gerador GENE-05 da dng1 (FATO: posição e rotação −90° do beGeneraterData); IA ainda não implementada.
    static readonly Vector3 EnemyAt = new(40, 45, 120);
    const float Gravity = 300f;

    string _dir = "";
    CameraZones[] _cameraSets = [];
    int _cameraSet, _zone = -1;
    readonly PadMovement _move = new();
    CharacterBody3D _player = null!;
    Node3D? _model;
    AnimationPlayer? _anim;
    string? _animWait, _animRun;
    Camera3D _camera = null!;
    Label _hud = null!;
    readonly List<string> _problems = [];

    public override void _Ready()
    {
        _dir = OS.GetEnvironment("DW4_FATIA") is { Length: > 0 } d ? d : @"D:\dw4-work\fatia";
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.65f), BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-60, 30, 0) });

        if (Load("cenario", "cena.gltf") is { } scenery) AddChild(scenery);
        if (Load("colisao", "colisao.gltf") is { } collision) AddChild(MakeStaticBody(collision));

        _player = new CharacterBody3D { Position = Start, FloorSnapLength = 4f };
        _player.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 2.5f, Height = 7f }, Position = new Vector3(0, 3.5f, 0) });
        AddChild(_player);
        _model = Load("guilmon", "personagem.gltf");
        if (_model != null)
        {
            _player.AddChild(_model);
            _anim = FindAll<AnimationPlayer>(_model).FirstOrDefault();
            if (_anim != null)
            {
                var names = _anim.GetAnimationList();
                _animWait = Pick(names, "waitG", "wait");
                _animRun = Pick(names, "runG", "run");
                foreach (var n in new[] { _animWait, _animRun })
                    if (n != null) _anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
                if (_animWait != null) _anim.Play(_animWait);
            }
        }

        if (Load("numemon", "personagem.gltf") is { } numemon)
        {
            // Caixa de corpo do Numemon (igAABox do HIT): 8 × 14 × 8, FATO; forma de cápsula é aproximação.
            var enemy = new CharacterBody3D { Position = EnemyAt, RotationDegrees = new Vector3(0, -90, 0) };
            enemy.AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 4f, Height = 14f }, Position = new Vector3(0, 7, 0) });
            enemy.AddChild(numemon);
            AddChild(enemy);
            if (FindAll<AnimationPlayer>(numemon).FirstOrDefault() is { } ea && Pick(ea.GetAnimationList(), "waitG", "wait") is { } w)
            {
                ea.GetAnimation(w).LoopMode = Animation.LoopModeEnum.Linear;
                ea.Play(w);
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
            if (k.Keycode == Key.F5) { _player.Position = Start; _player.Velocity = Vector3.Zero; _move.Stop(); }
            if (k.Keycode == Key.Escape) GetTree().Quit();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
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

        var v = _move.Step(new NVector2(dir.X, dir.Z), dt);
        var vel = _player.Velocity;
        vel.X = v.X;
        vel.Z = v.Y;
        vel.Y = _player.IsOnFloor() ? -1f : vel.Y - Gravity * dt;
        _player.Velocity = vel;
        _player.MoveAndSlide();
        if (_model != null) _model.Rotation = new Vector3(0, _move.Facing, 0);

        bool moving = v.LengthSquared() > 1f;
        var want = moving ? _animRun : _animWait;
        if (_anim != null && want != null && _anim.CurrentAnimation != want) _anim.Play(want, 0.13f); // ~8 quadros a 60 Hz (MOTIONSET Float@4 = 8, hipótese)

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
            "movimento, gravidade e ponto inicial PROVISÓRIOS (não medidos no jogo)" +
            (_problems.Count > 0 ? "\n" + string.Join("\n", _problems) : "");
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
