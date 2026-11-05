using System;
using System.IO;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Modelo 3D de uma forma numa caixa da interface (SubViewport próprio): o Digimon parado na animação de espera, enquadrado pela
/// caixa dele, e girando devagar quando <c>spin</c>. Usa o modelo de jogo da forma (&lt;fatia&gt;\formas\&lt;MODEL_NAME&gt;; o Guilmon também
/// aceita a pasta antiga &lt;fatia&gt;\guilmon). DECISÃO DO PORT: telas de vagas e criação com layout próprio (docs/decisoes-port-pc.md).
/// </summary>
public partial class ModelView : SubViewportContainer
{
    const float TurnDegreesPerSecond = 30;
    readonly string _common;
    readonly bool _spin;
    readonly SubViewport _vp;
    readonly Camera3D _camera;
    readonly Node3D _pivot;
    Node3D? _model;
    string _shown = "";

    public ModelView(string commonDir, Vector2 minSize, bool spin)
    {
        _common = commonDir;
        _spin = spin;
        Stretch = true;
        CustomMinimumSize = minSize;
        MouseFilter = MouseFilterEnum.Ignore;
        _vp = new SubViewport { TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Msaa3D = Viewport.Msaa.Msaa4X };
        _camera = new Camera3D { Fov = 30, Current = true, Near = 0.1f, Far = 1000 };
        _vp.AddChild(_camera);
        _vp.AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, 30, 0), LightEnergy = 1.1f });
        _vp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.ClearColor, AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color(0.6f, 0.6f, 0.65f) } });
        _pivot = new Node3D();
        _vp.AddChild(_pivot);
        AddChild(_vp);
    }

    public ModelView() : this("", new Vector2(64, 64), false) { }

    /// <summary>Mostra a forma (MODEL_NAME, ex. p_agumon); vazio limpa.</summary>
    public void Show(string playerModel)
    {
        if (playerModel == _shown) return;
        _shown = playerModel;
        _model?.QueueFree();
        _model = null;
        if (playerModel.Length == 0) return;
        var dir = Path.Combine(_common, "formas", playerModel);
        if (!GameFiles.DirExists(dir) && playerModel == "p_girumon") dir = Path.Combine(_common, "guilmon");
        var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "personagem.gltf", SearchOption.AllDirectories).FirstOrDefault() : null;
        var doc = new GltfDocument();
        var state = new GltfState();
        if (file == null || doc.AppendFromFile(GameFiles.GodotPath(file), state) != Error.Ok || doc.GenerateScene(state) is not Node3D m) return;
        _model = m;
        _pivot.AddChild(m);
        _pivot.Rotation = new Vector3(0, Mathf.DegToRad(20), 0);
        if (m.FindChildren("*", nameof(AnimationPlayer), true, false).OfType<AnimationPlayer>().FirstOrDefault() is { } player)
        {
            var wait = player.GetAnimationList().FirstOrDefault(a => a.StartsWith("wait", StringComparison.OrdinalIgnoreCase)) ?? player.GetAnimationList().FirstOrDefault();
            if (wait != null) { player.GetAnimation(wait).LoopMode = Animation.LoopModeEnum.Linear; player.Play(wait); }
        }
        // Enquadra pela caixa das malhas (no espaço do pivô; o modelo ainda não terminou de entrar na árvore).
        var boxes = m.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>()
            .Select(mi => Relative(mi, _pivot) * mi.Mesh.GetAabb()).ToList();
        if (boxes.Count == 0) return;
        var box = boxes.Aggregate((a, b) => a.Merge(b));
        var center = box.GetCenter();
        float size = Math.Max(box.Size.Y, Math.Max(box.Size.X, box.Size.Z) * 0.85f);
        float dist = size / 2 / MathF.Tan(Mathf.DegToRad(_camera.Fov / 2)) * 1.2f;
        var pos = center + new Vector3(0, size * 0.06f, dist);
        _camera.Transform = new Transform3D(Basis.LookingAt(center - pos, Vector3.Up), pos); // sem LookAt: pode estar fora da árvore
    }

    static Transform3D Relative(Node3D n, Node3D root)
    {
        var t = Transform3D.Identity;
        for (Node? x = n; x != null && x != root; x = x.GetParent()) if (x is Node3D x3) t = x3.Transform * t;
        return t;
    }

    public override void _Process(double delta)
    {
        if (_spin && _model != null) _pivot.RotateY(Mathf.DegToRad(TurnDegreesPerSecond) * (float)delta);
    }
}
