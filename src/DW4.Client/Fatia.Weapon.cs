using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Arma na mão e blocos de sequência pela arma equipada (<see cref="WeaponStyles"/>). Modelos e estilos lidos de
/// &lt;fatia&gt;\armas\&lt;MODEL&gt; (dw4 igb-cena e igb-dump beWeaponSeqData dos w_*.igb, ndmw #899..#972; fora do repositório).
/// Sem a pasta da arma, fica a pasta antiga &lt;fatia&gt;\arma (w_sword01) e o estilo "slash".
/// HIPÓTESE: o osso da mão direita (RArm04) segura todas as armas, inclusive as duplas (o beWeaponAttachData não foi lido).
/// </summary>
public partial class Fatia
{
    WeaponStyles _styles = WeaponStyles.Fallback;
    BoneAttachment3D? _weaponAttach;
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
        _styles = stylesFile != null && File.Exists(stylesFile) ? WeaponStyles.FromJson(File.ReadAllText(stylesFile)) : WeaponStyles.Fallback;
        if (_model == null || FindAll<Skeleton3D>(_model).FirstOrDefault() is not { } skeleton) return;
        _weaponAttach?.QueueFree();
        _weaponAttach = null;
        var mesh = dir != null && Directory.Exists(dir) ? Load(Path.Combine("armas", model!), "cena.gltf") : Load("arma", "cena.gltf");
        if (mesh == null) return;
        if (skeleton.FindBone(WeaponBone) < 0) _problems.Add($"osso {WeaponBone} não existe para a arma");
        _weaponAttach = new BoneAttachment3D { BoneName = WeaponBone };
        skeleton.AddChild(_weaponAttach);
        _weaponAttach.AddChild(mesh);
        SetupStyleAnims();
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
