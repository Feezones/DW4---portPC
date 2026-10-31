using System;
using System.Linq;
using Godot;

namespace DW4.Client;

/// <summary>
/// Placa com o nome e o nível ("Nome  Lv N") em cima de um jogador. DECISÃO DO PORT (pedido do responsável, pensando no
/// multiplayer; docs/decisoes-port-pc.md): um componente por jogador, sempre virado para a câmera e preso ao osso da cabeça
/// ("Head"/"Head01" nas 16 formas). Sem esqueleto, fica sobre o modelo.
/// </summary>
public partial class Nameplate : Node3D
{
    const float AboveHead = 6f;
    Label3D _label = null!;
    string _name = "";
    int _level = -1;

    public override void _Ready()
    {
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FixedSize = true,
            PixelSize = 0.0012f,
            FontSize = 28,
            OutlineSize = 8,
            Modulate = new Color(1, 1, 1),
            OutlineModulate = new Color(0, 0, 0, 0.85f),
            RenderPriority = 10,
            Position = new Vector3(0, AboveHead, 0),
        };
        AddChild(_label);
    }

    public void Set(string name, int level)
    {
        if (name == _name && level == _level) return;
        (_name, _level) = (name, level);
        if (_label != null) _label.Text = $"{name}  Lv {level}";
    }

    /// <summary>Cria a placa presa à cabeça do modelo (osso "Head*"), ou no alto do modelo se não houver.</summary>
    public static Nameplate Attach(Node3D model, float fallbackHeight = 8f)
    {
        var plate = new Nameplate();
        var skeleton = model.FindChildren("*", nameof(Skeleton3D), true, false).OfType<Skeleton3D>().FirstOrDefault();
        int bone = -1;
        if (skeleton != null)
            for (int i = 0; i < skeleton.GetBoneCount() && bone < 0; i++)
                if (skeleton.GetBoneName(i).StartsWith("Head", StringComparison.OrdinalIgnoreCase)) bone = i;
        if (skeleton != null && bone >= 0)
        {
            var attach = new BoneAttachment3D { BoneName = skeleton.GetBoneName(bone) };
            skeleton.AddChild(attach);
            attach.AddChild(plate);
        }
        else
        {
            plate.Position = new Vector3(0, fallbackHeight, 0);
            model.AddChild(plate);
        }
        return plate;
    }
}
