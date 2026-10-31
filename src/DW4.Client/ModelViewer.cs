using System.IO;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// DEPURAÇÃO (só no build Debug, não é do jogo): DW4_VER_MODELO="arquivo.gltf;animação;arquivo.gltf;…" mostra cada modelo de frente e
/// de lado, com a animação no meio, e grava user://prints/modelo_&lt;n&gt;_&lt;frente|lado&gt;.png; depois fecha. Serve para conferir peles e
/// poses sem jogar até o modelo.
/// </summary>
public partial class ModelViewer : Node3D
{
    string[] _items = [];
    int _index = -2;
    int _wait;
    Node3D? _model;
    Camera3D _camera = null!;

    public override void _Ready()
    {
        var spec = OS.GetEnvironment("DW4_VER_MODELO").Split(';');
        _items = spec;
        AddChild(new WorldEnvironment { Environment = new Environment { BackgroundMode = Environment.BGMode.Color, BackgroundColor = new Color(0.35f, 0.4f, 0.5f), AmbientLightSource = Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.8f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-40, 30, 0) });
        _camera = new Camera3D { Current = true, Fov = 40 };
        AddChild(_camera);
        DirAccess.MakeDirRecursiveAbsolute("user://prints");
    }

    // Um passo por quadro: carrega e põe a câmera de frente → grava frente e põe de lado → grava lado → próximo modelo.
    int _step;
    Vector3 _center;
    float _radius;

    public override void _Process(double delta)
    {
        if (_wait-- > 0) return;
        int n = _index / 2;
        switch (_step)
        {
            case 0:
                _index += 2;
                _model?.QueueFree();
                _model = null;
                if (_index >= _items.Length) { GetTree().Quit(); return; }
                if (!Load(_items[_index], _index + 1 < _items.Length ? _items[_index + 1] : "")) return;
                Aim(new Vector3(0, 0, _radius * 1.4f));
                _step = 1;
                _wait = 2;
                return;
            case 1:
                Save($"modelo_{n}_frente");
                Aim(new Vector3(_radius * 1.4f, 0, 0));
                _step = 2;
                _wait = 2;
                return;
            default:
                Save($"modelo_{n}_lado");
                _step = 0;
                return;
        }
    }

    bool Load(string file, string anim)
    {
        var doc = new GltfDocument();
        var state = new GltfState();
        if (!File.Exists(file) || doc.AppendFromFile(file, state) != Error.Ok || doc.GenerateScene(state) is not Node3D m) { GD.Print($"[modelo] {file}: não abriu"); return false; }
        _model = m;
        AddChild(m);
        var player = m.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        if (player != null && anim.Length > 0 && player.HasAnimation(anim)) { player.Play(anim); player.Seek(0.5, true); player.Pause(); }
        var box = m.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Select(mi => mi.GlobalTransform * mi.Mesh.GetAabb()).Aggregate((a, b) => a.Merge(b));
        _radius = Mathf.Max(box.Size.Length(), 1);
        _center = box.GetCenter();
        GD.Print($"[modelo] {_index / 2} {file} anim={anim} ({(player?.HasAnimation(anim) == true ? "ok" : "sem")}) caixa {box.Size}");
        return true;
    }

    void Aim(Vector3 offset)
    {
        _camera.Position = _center + offset;
        _camera.LookAt(_center, Vector3.Up);
    }

    void Save(string name) => GetViewport().GetTexture().GetImage().SavePng($"user://prints/{name}.png");
}
