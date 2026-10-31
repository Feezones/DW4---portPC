using System;
using System.IO;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Digimon da escolha girando na criação de personagem. FATO (stagIS13, ndmw #813): os modelos de escolha são h_agumon, h_girumon,
/// h_vmon e h_dorumon (KIND_OBJECT com esqueleto, exportados com dw4 igb-gltf/igb-seq --ator=h_… para &lt;fases&gt;\stagis13\escolha);
/// as sequências deles são SQ_ENTR (FADESET IN 8 + waitC em laço), SQ_WAIT (waitC), SQ_ENJOY (dcngC01 uma vez e dcngC02 em laço) e
/// SQ_EXIT (FADESET OUT 8). DECISÃO DO PORT: o modelo gira devagar numa câmera própria à esquerda da tela (a câmera de caixa da
/// stagIS13 não foi montada); ao confirmar, toca o dcngC01 → dcngC02 do SQ_ENJOY.
/// </summary>
public sealed class ModelTurntable
{
    const float TurnDegreesPerSecond = 30;
    readonly string _dir;
    readonly SubViewport _vp;
    readonly Camera3D _camera;
    readonly Node3D _pivot;
    readonly CanvasLayer _canvas;
    Node3D? _model;
    AnimationPlayer? _player;
    string _shown = "";

    public ModelTurntable(Node owner, string stagesDir, int layer)
    {
        _dir = Path.Combine(stagesDir, "stagis13", "escolha");
        _vp = new SubViewport { Size = new Vector2I(512, 640), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _camera = new Camera3D { Fov = 30, Current = true, Near = 0.1f, Far = 1000 };
        _vp.AddChild(_camera);
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 30, 0), LightEnergy = 1.1f });
        _vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.55f, 0.55f, 0.6f) } });
        _pivot = new Node3D();
        _vp.AddChild(_pivot);
        _canvas = new CanvasLayer { Layer = layer, Visible = false };
        var rect = new TextureRect { Texture = _vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 0.45f, AnchorBottom = 1 };
        _canvas.AddChild(_vp);
        _canvas.AddChild(rect);
        owner.AddChild(_canvas);
    }

    public static bool Available(string stagesDir) => Directory.Exists(Path.Combine(stagesDir, "stagis13", "escolha"));

    public bool Visible { get => _canvas.Visible; set => _canvas.Visible = value; }

    /// <summary>Mostra o modelo de escolha da forma (MODEL_NAME p_xxx → h_xxx).</summary>
    public void Show(string playerModel)
    {
        string name = "h_" + (playerModel.StartsWith("p_", StringComparison.Ordinal) ? playerModel[2..] : playerModel);
        if (name == _shown) return;
        _shown = name;
        _model?.QueueFree();
        _model = null;
        _player = null;
        var dir = Path.Combine(_dir, name);
        var file = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.gltf", SearchOption.AllDirectories).FirstOrDefault() : null;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (file == null || doc.AppendFromFile(file, state) != Error.Ok || doc.GenerateScene(state) is not Node3D m) { GD.Print($"[criar] {name}: modelo não exportado em {dir}"); return; }
        _model = m;
        _pivot.AddChild(m);
        _pivot.Rotation = Vector3.Zero;
        _player = m.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault();
        Loop("waitC");
        // Enquadra pela caixa do modelo (na pose do waitC).
        var boxes = m.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>().Select(mi => mi.GlobalTransform * mi.Mesh.GetAabb()).ToList();
        if (boxes.Count == 0) return;
        var box = boxes.Aggregate((a, b) => a.Merge(b));
        var center = box.GetCenter();
        float size = Math.Max(box.Size.Y, Math.Max(box.Size.X, box.Size.Z) * 0.8f);
        float dist = size / 2 / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2)) * 1.25f;
        _camera.Position = center + new Vector3(0, size * 0.08f, dist);
        _camera.LookAt(center, Vector3.Up);
    }

    void Loop(string anim)
    {
        if (_player == null || !_player.HasAnimation(anim)) return;
        _player.GetAnimation(anim).LoopMode = Animation.LoopModeEnum.Linear;
        _player.Play(anim);
    }

    /// <summary>Escolha confirmada: dcngC01 uma vez e dcngC02 em laço (SQ_ENJOY).</summary>
    public void Enjoy()
    {
        if (_player == null || !_player.HasAnimation("dcngC01")) return;
        _player.Play("dcngC01");
        if (_player.HasAnimation("dcngC02"))
        {
            _player.GetAnimation("dcngC02").LoopMode = Animation.LoopModeEnum.Linear;
            _player.Queue("dcngC02");
        }
    }

    public void Tick(float dt)
    {
        if (!Visible || _model == null) return;
        _pivot.RotateY(Mathf.DegToRad(TurnDegreesPerSecond) * dt);
    }
}
