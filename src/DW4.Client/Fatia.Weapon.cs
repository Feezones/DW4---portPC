using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Arma na mão e blocos de sequência pela arma equipada (<see cref="WeaponStyles"/>). Modelos e estilos lidos de
/// &lt;fatia&gt;\armas\&lt;MODEL&gt; (dw4 igb-cena e igb-dump beWeaponSeqData dos w_*.igb, ndmw #899..#972; fora do repositório).
/// Sem a pasta da arma, fica a pasta antiga &lt;fatia&gt;\arma (w_sword01) e o estilo "slash".
/// HIPÓTESE: o osso da mão direita (RArm04) segura a arma (o beWeaponAttachData não foi lido). Armas duplas (Crush Arm e as
/// outras 32 com nó "left" no IGB, maos.json do igb-cena): as superfícies "left" vão para a mão esquerda (LArm04), já com o
/// igTransform "Rot180" do jogo aplicado pelo exportador.
/// </summary>
public partial class Fatia
{
    WeaponStyles _styles = WeaponStyles.Fallback;
    BoneAttachment3D? _weaponAttach, _weaponAttachLeft;
    const string WeaponBoneLeft = "LArm04";
    int _weaponShown = -1;

    string StyleOf(string sequence) => _styles.Of(sequence);

    string? WeaponModel => _db != null && _equip != null && _equip.WeaponRow >= 0 ? _db["weapon"].Text(_equip.WeaponRow, "MODEL") : null;

    // Troca o modelo na mão e os blocos quando a arma equipada muda (início da fase e equipar).
    void RefreshWeapon()
    {
        int row = _equip?.WeaponRow ?? -1;
        if (row == _weaponShown && _weaponAttach != null) return;
        _weaponShown = row;
        var model = WeaponModel;
        var dir = model != null ? Path.Combine(_common, "armas", model) : null;
        var stylesFile = dir != null ? Path.Combine(dir, "estilos.json") : null;
        _styles = stylesFile != null && GameFiles.Exists(stylesFile) ? WeaponStyles.FromJson(GameFiles.ReadAllText(stylesFile)) : WeaponStyles.Fallback;
        if (_model == null) return;
        _weaponAttach?.QueueFree();
        _weaponAttach = null;
        _weaponAttachLeft?.QueueFree();
        _weaponAttachLeft = null;
        (_weaponAttach, _weaponAttachLeft) = AttachWeapon(_model, model);
        SetupStyleAnims();
    }

    /// <summary>
    /// Prende a arma <paramref name="model"/> (pasta &lt;fatia&gt;\armas\&lt;MODEL&gt;; null = a pasta antiga "arma") no esqueleto de
    /// <paramref name="character"/>: mão direita e, nas armas duplas, mão esquerda. Serve ao jogador e aos outros jogadores.
    /// </summary>
    (BoneAttachment3D? Right, BoneAttachment3D? Left) AttachWeapon(Node3D character, string? model)
    {
        if (FindAll<Skeleton3D>(character).FirstOrDefault() is not { } skeleton) return (null, null);
        var dir = model != null ? Path.Combine(_common, "armas", model) : null;
        var mesh = dir != null && GameFiles.DirExists(dir) ? Load(Path.Combine("armas", model!), "cena.gltf") : Load("arma", "cena.gltf");
        if (mesh == null) return (null, null);
        if (skeleton.FindBone(WeaponBone) < 0) _problems.Add($"osso {WeaponBone} não existe para a arma");
        var right = new BoneAttachment3D { BoneName = WeaponBone };
        skeleton.AddChild(right);
        right.AddChild(mesh);
        BoneAttachment3D? leftAttach = null;
        if (dir != null && SplitLeftHand(dir, mesh) is { } left && skeleton.FindBone(WeaponBoneLeft) >= 0)
        {
            leftAttach = new BoneAttachment3D { BoneName = WeaponBoneLeft };
            skeleton.AddChild(leftAttach);
            leftAttach.AddChild(left);
        }
        return (right, leftAttach);
    }

    // Tira da malha da arma as superfícies do lado esquerdo (maos.json: um lado por superfície da malha "cena", na ordem) e
    // devolve-as numa malha própria; sem o arquivo (arma de uma mão), null.
    static Node3D? SplitLeftHand(string dir, Node3D scene)
    {
        var file = GameFiles.DirExists(dir) ? GameFiles.EnumerateFiles(dir, "maos.json", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (file == null || FindAll<MeshInstance3D>(scene).FirstOrDefault(m => m.Mesh is ArrayMesh) is not { } mi) return null;
        var sides = System.Text.Json.JsonSerializer.Deserialize<string[]>(GameFiles.ReadAllText(file)) ?? [];
        var src = (ArrayMesh)mi.Mesh;
        if (sides.Length != src.GetSurfaceCount()) return null;
        var right = new ArrayMesh();
        var left = new ArrayMesh();
        for (int s = 0; s < sides.Length; s++)
        {
            var target = sides[s] == "left" ? left : right;
            target.AddSurfaceFromArrays(src.SurfaceGetPrimitiveType(s), src.SurfaceGetArrays(s));
            target.SurfaceSetMaterial(target.GetSurfaceCount() - 1, src.SurfaceGetMaterial(s));
        }
        mi.Mesh = right;
        var leftNode = new MeshInstance3D { Mesh = left, Transform = mi.Transform, Name = "arma_esquerda" };
        return leftNode;
    }

    // Parado e andando pelo bloco da arma: SQ_WAIT / SQ_WALK → ações do bloco → MOTIONSET (o andar é o último movimento).
    void SetupStyleAnims()
    {
        if (_runner == null || _anim == null) return;
        var seqs = _runner.Sequences;
        _animWait = ActionAnimations.FirstAction(seqs, "SQ_WAIT", StyleOf("SQ_WAIT")) is { } wa ? AnimName(wa) : null;
        _animRun = ActionAnimations.BlockActions(seqs, "SQ_WALK", StyleOf("SQ_WALK"))
            .SelectMany(a => ActionAnimations.Motions(_actions, a)).LastOrDefault() is { } rm && _anim.HasAnimation(rm) ? rm : null;
        var names = _anim.GetAnimationList();
        _animWait ??= Pick(names, "waitS", "wait");
        _animRun ??= Pick(names, "runS", "run");
        foreach (var n in new[] { _animWait, _animRun })
            if (n != null) _anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;
        if (_animWait != null && _runner?.Running != true) _anim.Play(_animWait);
    }
}
