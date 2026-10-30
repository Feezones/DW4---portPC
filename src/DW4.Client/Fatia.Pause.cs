using Godot;

namespace DW4.Client;

/// <summary>
/// Menu de pausa do port (DECISÃO DO PORT; o jogo original não tem): Esc (ação "pausa") abre Continuar / Configurações / Gravar e
/// voltar ao menu. É um jogo em rede: a fase continua rodando atrás, só o controle do jogador fica travado. As Configurações são as
/// mesmas do menu inicial (SettingsPanel) e valem na hora: janela, som e controles; a câmera da dungeon troca de conjunto na hora;
/// a luz (LS/LH_pack, carregada no boot da fase) vale a partir da próxima troca de fase.
/// </summary>
public partial class Fatia
{
    void OpenPause() => SaveWindow("Pausa",
    [
        ("Continuar", CloseSaveWindow),
        ("Configurações", OpenSettings),
        ("Gravar e voltar ao menu", () => { CloseSaveWindow(); LeaveGame(); }),
    ]);

    void OpenSettings()
    {
        CloseSaveWindow();
        _padEnabled = false;
        _saveLayer = new CanvasLayer { Layer = 20 };
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        var panel = new PanelContainer();
        panel.AddChild(new SettingsPanel(OpenPause, ApplyLiveSettings));
        center.AddChild(panel);
        UseGameFont(center);
        _saveLayer.AddChild(dim);
        _saveLayer.AddChild(center);
        AddChild(_saveLayer);
    }

    // O que as Configurações mudam além de janela, som e controles (PortSettings.Apply já fez esses).
    void ApplyLiveSettings()
    {
        var c = PortSettings.Current;
        Ram.CameraMode = c.CameraMode;
        Ram.LightMode = c.LightMode; // vale no boot da próxima fase (pacote de luz do cenário)
        _cameraSet = c.CameraMode != 0 && _cameraSets.Length > 1 ? 1 : 0;
    }
}
