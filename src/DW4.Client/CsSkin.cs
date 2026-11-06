using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Troca só a APARÊNCIA de um Digimon pelo modelo do Cyber Sleuth (publicado a partir da 0.6.22, decisão do responsável em 2026-11-06).
/// O modelo do DW4 continua carregado e tocando as animações originais (hitboxes, quadros de ataque, arma, placa de nome
/// seguem os ossos dele); só as malhas dele ficam invisíveis. O modelo do CS (D:\dw4-work\cyber-sleuth\saida\*_dw4.glb,
/// fora do repositório) tem as mesmas animações do DW4 passadas para o esqueleto dele e acompanha quadro a quadro.
/// Pasta dos .glb: DW4_CS_MODELOS ou, sem ela, &lt;dados&gt;\cs_modelos (vai solta no pacote do CDN, nunca no git).
/// Sem a pasta, o jogo fica com os modelos do DW4.
/// </summary>
public partial class CsSkin : Node3D
{
    // forma/inimigo/NPC do DW4 → arquivo, escala e mãos, lidos de <pasta>/cs_modelos.json (gerado pelos scripts do teste,
    // fora do repositório): { "p_agumon": { "arquivo": "p_agumon.glb", "escala": 5.01, "desloc": 0.0, "maos": { "RArm04": "J_hand_r" } } }.
    // desloc: altura (unidades do modelo DW4) somada para os pés do CS ficarem onde estão os do DW4 na pose parada.
    // Mãos: ossos onde o jogo prende a arma (Fatia.Weapon: RArm04/LArm04) → osso equivalente do CS. A arma continua presa ao
    // osso do DW4 (mesma rotação), só é empurrada até a mão do modelo do CS, que tem outro tamanho de braço.
    sealed record Entry(string File, float Scale, float Lift, Dictionary<string, string> Hands);
    static Dictionary<string, Entry>? s_models;
    static Dictionary<string, Entry> Models => s_models ??= ReadManifest();

    static Dictionary<string, Entry> ReadManifest()
    {
        var map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (Folder is not { } folder) return map;
        var path = Path.Combine(folder, "cs_modelos.json");
        if (!File.Exists(path)) { GD.PrintErr($"[cs] falta {path}"); return map; }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var hands = new Dictionary<string, string>();
                if (p.Value.TryGetProperty("maos", out var h))
                    foreach (var x in h.EnumerateObject()) hands[x.Name] = x.Value.GetString() ?? "";
                map[p.Name] = new Entry(p.Value.GetProperty("arquivo").GetString() ?? "", p.Value.GetProperty("escala").GetSingle(),
                    p.Value.TryGetProperty("desloc", out var d) ? d.GetSingle() : 0f, hands);
            }
            GD.Print($"[cs] {map.Count} modelos do Cyber Sleuth em {path}");
        }
        catch (Exception e) { GD.PrintErr($"[cs] erro em {path}: {e.Message}"); }
        return map;
    }

    static string? s_folder;
    static bool s_folderInit;
    static string? Folder
    {
        get
        {
            if (s_folderInit) return s_folder;
            s_folderInit = true;
            var env = OS.GetEnvironment("DW4_CS_MODELOS");
            var dir = env is { Length: > 0 } ? env : Path.Combine(DataPaths.DataRoot, "cs_modelos");
            s_folder = File.Exists(Path.Combine(dir, "cs_modelos.json")) ? dir : null;
            return s_folder;
        }
    }
    static readonly Dictionary<string, (GltfDocument Doc, GltfState State)?> s_cache = new();

    AnimationPlayer? _source, _mine;
    Skeleton3D? _dwSkeleton, _csSkeleton;
    Dictionary<string, string> _hands = new();
    readonly Dictionary<Node3D, Vector3> _basePos = new();

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
        var skin = new CsSkin { Name = "CsSkin", _source = source, Scale = Vector3.One * m.Scale, Position = new Vector3(0, m.Lift, 0) };
        skin.AddChild(scene);
        skin._mine = Find<AnimationPlayer>(scene).FirstOrDefault();
        skin._dwSkeleton = Find<Skeleton3D>(dw4Model).FirstOrDefault();
        skin._csSkeleton = Find<Skeleton3D>(scene).FirstOrDefault();
        skin._hands = m.Hands;
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
