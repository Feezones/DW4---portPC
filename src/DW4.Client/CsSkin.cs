using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// TESTE LOCAL (não é do jogo, desligado por padrão): troca só a APARÊNCIA de um Digimon pelo modelo do Cyber Sleuth.
/// O modelo do DW4 continua carregado e tocando as animações originais (hitboxes, quadros de ataque, arma, placa de nome
/// seguem os ossos dele); só as malhas dele ficam invisíveis. O modelo do CS (D:\dw4-work\cyber-sleuth\saida\*_dw4.glb,
/// fora do repositório) tem as mesmas animações do DW4 passadas para o esqueleto dele e acompanha quadro a quadro.
/// Liga com DW4_CS_MODELOS=&lt;pasta dos .glb&gt;.
/// </summary>
public partial class CsSkin : Node3D
{
    // forma/inimigo do DW4 → arquivo e escala (altura do DW4 na pose parada ÷ altura do CS com a mesma animação).
    static readonly Dictionary<string, (string File, float Scale)> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p_dorumon"] = ("dorumon_dw4.glb", 4.13f),
        ["e_goburi"] = ("goblimon_dw4.glb", 4.45f),
        ["e_ogre"] = ("ogremon_dw4.glb", 74f),
    };

    static string? Folder => OS.GetEnvironment("DW4_CS_MODELOS") is { Length: > 0 } d ? d : null;
    static readonly Dictionary<string, (GltfDocument Doc, GltfState State)?> s_cache = new();

    AnimationPlayer? _source, _mine;
    Skeleton3D? _dwSkeleton, _csSkeleton;
    Dictionary<string, string> _hands = new();
    readonly Dictionary<Node3D, Vector3> _basePos = new();

    // Mãos onde o jogo prende a arma (Fatia.Weapon: RArm04/LArm04) → osso equivalente do CS. A arma continua presa ao
    // osso do DW4 (mesma rotação), só é empurrada até a mão do modelo do CS, que tem outro tamanho de braço.
    static readonly Dictionary<string, Dictionary<string, string>> Hands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["p_dorumon"] = new() { ["RArm04"] = "J_te_R", ["LArm04"] = "J_te_L" },
    };

    /// <summary>Se houver modelo do CS para <paramref name="key"/>, esconde as malhas do DW4 e põe o do CS por cima.</summary>
    public static bool TryAttach(Node3D dw4Model, string key, AnimationPlayer? source)
    {
        if (Folder is not { } folder || !Models.TryGetValue(key, out var m)) return false;
        if (!s_cache.TryGetValue(key, out var loaded))
        {
            var path = Path.Combine(folder, m.File);
            loaded = null;
            if (File.Exists(path))
            {
                var doc = new GltfDocument(); var state = new GltfState();
                if (doc.AppendFromFile(path, state) == Error.Ok) loaded = (doc, state);
                else GD.PrintErr($"[cs] erro ao ler {path}");
            }
            else GD.PrintErr($"[cs] falta {path}");
            s_cache[key] = loaded;
        }
        if (loaded is not { } l || l.Doc.GenerateScene(l.State) is not Node3D scene) return false;
        foreach (var mi in Find<MeshInstance3D>(dw4Model)) mi.Visible = false; // a arma entra depois e continua visível
        var skin = new CsSkin { Name = "CsSkin", _source = source, Scale = Vector3.One * m.Scale };
        skin.AddChild(scene);
        skin._mine = Find<AnimationPlayer>(scene).FirstOrDefault();
        skin._dwSkeleton = Find<Skeleton3D>(dw4Model).FirstOrDefault();
        skin._csSkeleton = Find<Skeleton3D>(scene).FirstOrDefault();
        if (Hands.TryGetValue(key, out var hands)) skin._hands = hands;
        dw4Model.AddChild(skin);
        GD.Print($"[cs] {key}: modelo do Cyber Sleuth ligado ({m.File}, escala {m.Scale})");
        return true;
    }

    public override void _Process(double delta)
    {
        if (_source == null || _mine == null) return;
        string name = _source.CurrentAnimation.ToString(); // StringName na Godot 4.7
        if (name.Length == 0) name = _source.AssignedAnimation.ToString();
        if (name.Length == 0 || !_mine.HasAnimation(name)) return;
        if (_mine.AssignedAnimation.ToString() != name) { _mine.Play(name); _mine.Pause(); }
        _mine.Seek(_source.CurrentAnimationPosition, true);
        FollowHands();
    }

    void FollowHands()
    {
        if (_dwSkeleton == null || _csSkeleton == null || _hands.Count == 0) return;
        foreach (var att in _dwSkeleton.GetChildren().OfType<BoneAttachment3D>())
        {
            if (!_hands.TryGetValue(att.BoneName, out var csBone)) continue;
            int dwi = _dwSkeleton.FindBone(att.BoneName), csi = _csSkeleton.FindBone(csBone);
            if (dwi < 0 || csi < 0) continue;
            var dwPos = (_dwSkeleton.GlobalTransform * _dwSkeleton.GetBoneGlobalPose(dwi)).Origin;
            var csPos = (_csSkeleton.GlobalTransform * _csSkeleton.GetBoneGlobalPose(csi)).Origin;
            var basis = (_dwSkeleton.GlobalTransform * _dwSkeleton.GetBoneGlobalPose(dwi)).Basis;
            var local = basis.Inverse() * (csPos - dwPos);
            foreach (var child in att.GetChildren().OfType<Node3D>())
            {
                if (!_basePos.TryGetValue(child, out var p0)) _basePos[child] = p0 = child.Position;
                child.Position = p0 + local;
            }
        }
    }

    static IEnumerable<T> Find<T>(Node n) where T : Node
    {
        foreach (var c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var x in Find<T>(c)) yield return x;
        }
    }
}
