using System.Collections.Generic;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Pisca vermelho de dano. FATO (igb-seq das fases): as ações de dano e de morte dos objetos (SDMG01/DEAD01 do g_cannon01, das
/// pedras, arbustos, barris, caixotes, tochas) e dos inimigos (LDMG01, LDMGD1… do e_goburi, e_nume, e_ogre) começam com
/// FLASHSET "Damage". HIPÓTESE (o efeito nativo do FLASHSET não foi lido): o modelo pisca em vermelho por meio segundo a cada
/// FLASHSET; como cada golpe recebido toca uma ação de dano, o objeto pisca a cada acerto até ser destruído.
/// </summary>
public partial class Fatia
{
    const float FlashSeconds = 0.5f, FlashPeriod = 0.1f;
    readonly Dictionary<Node3D, (float Left, StandardMaterial3D Mat)> _flashes = [];

    static bool ActionFlashes(IReadOnlyDictionary<string, List<SeqCommand>> actions, string? action) =>
        action != null && actions.TryGetValue(action, out var cmds) && cmds.Any(c => c.Kind == "FLASHSET" && c.S(0) == "Damage");

    void StartFlash(Node3D root)
    {
        if (DiagNpc) GD.Print($"[pisca] {root.Name}");
        if (_flashes.TryGetValue(root, out var f)) { _flashes[root] = (FlashSeconds, f.Mat); return; }
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1, 0.1f, 0.1f, 0.8f),
        };
        foreach (var g in FindAll<GeometryInstance3D>(root)) g.MaterialOverlay = mat;
        _flashes[root] = (FlashSeconds, mat);
    }

    void TickFlashes(float dt)
    {
        foreach (var (root, f) in _flashes.ToList())
        {
            float left = f.Left - dt;
            if (left <= 0 || !IsInstanceValid(root))
            {
                if (IsInstanceValid(root)) foreach (var g in FindAll<GeometryInstance3D>(root)) if (g.MaterialOverlay == f.Mat) g.MaterialOverlay = null;
                _flashes.Remove(root);
                continue;
            }
            bool on = (int)((FlashSeconds - left) / FlashPeriod) % 2 == 0;
            f.Mat.AlbedoColor = new Color(1, 0.1f, 0.1f, on ? 0.8f : 0f);
            _flashes[root] = (left, f.Mat);
        }
    }

    // Uma vez por ação nova: objetos pelo ObjectSequencer, inimigos pelo SequenceRunner.
    void CheckObjectFlash(WorldObject o)
    {
        if (o.Seq == null || o.Seq.FlashSerial == o.FlashSerial) return;
        o.FlashSerial = o.Seq.FlashSerial;
        StartFlash(o.Actor.Root);
    }

    // A ação de dano pode se repetir (dois golpes seguidos): conta também quando o quadro da mesma ação volta ao começo.
    void CheckEnemyFlash(Enemy e)
    {
        var action = e.Runner?.Action;
        float frame = e.Runner?.ActionFrame ?? 0;
        bool restarted = action == e.FlashAction && frame < e.FlashFrame;
        e.FlashFrame = frame;
        if (action == e.FlashAction && !restarted) return;
        e.FlashAction = action;
        if (ActionFlashes(e.Kind.Actions, action)) StartFlash(e.Model ?? (Node3D)e.Body);
    }
}
