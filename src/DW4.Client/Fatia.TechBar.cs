using System;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Barra de técnicas do port. FATO (fase4-levantamento-combate.md): o jogador manda GENERATEWAZA ao painel, que só aceita sem recarga e
/// com MP ≥ custo, desconta o MP, põe a recarga e devolve o LABEL da SQ_WAZA (<see cref="TechniquePanel"/>). DESCONHECIDO: como o
/// painel do jogo escolhe a técnica (a tela dele não foi lida). DECISÃO DO PORT: as 6 técnicas da WAZALIST numa barra embaixo, com
/// custo e recarga; 1..6 usam direto; roda do mouse ou LB/RB trocam a selecionada, e K, botão direito ou Y usam a selecionada.
/// </summary>
public partial class Fatia
{
    int _techSelected;
    HBoxContainer? _techBar;
    readonly Label[] _techLabels = new Label[6];
    readonly ColorRect[] _techCool = new ColorRect[6];

    void SetupTechBar()
    {
        var layer = new CanvasLayer { Layer = 6 };
        _techBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End, AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -680, OffsetRight = -20, OffsetTop = -74, OffsetBottom = -14, MouseFilter = Control.MouseFilterEnum.Ignore };
        _techBar.AddThemeConstantOverride("separation", 6);
        for (int i = 0; i < 6; i++)
        {
            var slot = new PanelContainer { CustomMinimumSize = new Vector2(104, 56), MouseFilter = Control.MouseFilterEnum.Ignore };
            var cool = new ColorRect { Color = new Color(0, 0, 0, 0.55f), AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
            var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", 13);
            slot.AddChild(label);
            slot.AddChild(cool);
            _techBar.AddChild(slot);
            _techLabels[i] = label;
            _techCool[i] = cool;
        }
        layer.AddChild(_techBar);
        AddChild(layer);
    }

    // Roda do mouse e LB/RB trocam a técnica selecionada.
    bool TechBarInput(InputEvent e)
    {
        int step = e switch
        {
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } => 1,
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } => -1,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.RightShoulder } => 1,
            InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.LeftShoulder } => -1,
            _ => 0,
        };
        if (step == 0 || _techs.Count == 0 || _menuOpen || ShopOpen || _saveLayer != null) return false;
        _techSelected = ((_techSelected + step) % _techs.Count + _techs.Count) % _techs.Count;
        return true;
    }

    void UpdateTechBar()
    {
        if (_techBar == null) return;
        // Some com a janela de fala aberta (a barra cobria a moldura de baixo).
        _techBar.Visible = _techs.Count > 0 && !_menuOpen && !TalkWindowOpen;
        if (_techSelected >= _techs.Count) _techSelected = 0;
        for (int i = 0; i < 6; i++)
        {
            var slot = (Control)_techLabels[i].GetParent();
            if (i >= _techs.Count) { slot.Visible = false; continue; }
            slot.Visible = true;
            var t = _techs[i];
            float cd = _panel.Cooldown(t);
            bool noMp = _mp < t.Mp;
            _techLabels[i].Text = $"{i + 1}  {t.Name}\n{t.Mp} MP";
            _techLabels[i].Modulate = noMp ? new Color(1, 0.5f, 0.5f) : i == _techSelected ? new Color(1, 1, 0.5f) : new Color(1, 1, 1);
            float frac = t.Delay > 0 ? Math.Clamp(cd / (t.Delay / 60f), 0, 1) : 0;
            _techCool[i].AnchorTop = 1 - frac;
            slot.SelfModulate = i == _techSelected ? new Color(1.4f, 1.4f, 0.8f) : new Color(1, 1, 1);
        }
    }
}
