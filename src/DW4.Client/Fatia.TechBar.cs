using System;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Barra de técnicas do port. FATO (fase4-levantamento-combate.md): o jogador manda GENERATEWAZA ao painel, que só aceita sem recarga e
/// com MP ≥ custo, desconta o MP, põe a recarga e devolve o LABEL da SQ_WAZA (<see cref="TechniquePanel"/>). FATO (db shinka): toda
/// Digimon tem 6 famílias de técnica (TECH1..6), então a WAZALIST chega a 6. DESCONHECIDO: como o painel do jogo escolhe a técnica
/// (a tela dele não foi lida).
/// DECISÃO DO PORT (pedido do usuário, 2026-11-06): a barra das técnicas fica separada da hotbar de itens (1–5), logo abaixo dela,
/// com a tecla de cada uma (padrão Q, E, R, F, C e V para a 6ª), custo e recarga. Tecla ou clique/toque na caixa usam direto; roda do
/// mouse ou LB/RB trocam a selecionada, e K ou o X do controle usam a selecionada.
/// </summary>
public partial class Fatia
{
    int _techSelected;
    HBoxContainer? _techBar;
    readonly Label[] _techLabels = new Label[6];
    readonly Label[] _techKeys = new Label[6];
    readonly ColorRect[] _techCool = new ColorRect[6];
    readonly PanelContainer[] _techBoxes = new PanelContainer[6];
    StyleBoxFlat? _techStyle, _techStyleSelected;

    void SetupTechBar()
    {
        var layer = new CanvasLayer { Layer = 6 };
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(holder);
        _techBar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _techBar.AddThemeConstantOverride("separation", 6);
        _techStyle = QuickBox(new Color(0.35f, 0.75f, 0.95f));
        _techStyleSelected = QuickBox(QuickGold, 3);
        for (int i = 0; i < 6; i++)
        {
            var box = new PanelContainer { CustomMinimumSize = new Vector2(104, 48), MouseFilter = Control.MouseFilterEnum.Stop };
            box.AddThemeStyleboxOverride("panel", _techStyle);
            int slot = i;
            box.GuiInput += ev =>
            {
                if (ev is not InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left }) return;
                box.AcceptEvent();
                if (slot < _techs.Count && !_menuOpen && !ShopOpen && !SubWindowOpen && !_quickOpen) _techKey = slot;
            };
            var cool = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
            var label = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 13);
            // Tecla numa plaquinha sobre o canto de cima à esquerda (fora do texto), como nos atalhos de MMO.
            var badgeStyle = new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.12f, 0.95f), BorderColor = QuickGold, CornerDetail = 4 };
            badgeStyle.SetBorderWidthAll(1);
            badgeStyle.SetCornerRadiusAll(4);
            badgeStyle.ContentMarginLeft = badgeStyle.ContentMarginRight = 4;
            var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(-14, -18) };
            badge.AddThemeStyleboxOverride("panel", badgeStyle);
            var key = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            key.AddThemeFontSizeOverride("font_size", 13);
            key.AddThemeColorOverride("font_color", QuickGold);
            badge.AddChild(key);
            var keyHolder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            keyHolder.AddChild(badge);
            box.AddChild(label);
            box.AddChild(cool);
            box.AddChild(keyHolder);
            _techBar.AddChild(box);
            _techLabels[i] = label;
            _techKeys[i] = key;
            _techCool[i] = cool;
            _techBoxes[i] = box;
        }
        holder.AddChild(_techBar);
        UseGameFont(_techBar);
        AddChild(layer);
    }

    // Roda do mouse e LB/RB trocam a técnica selecionada.
    bool TechBarInput(InputEvent e)
    {
        int step = PortInput.Pressed(e, "tecnica_proxima") ? 1 : PortInput.Pressed(e, "tecnica_anterior") ? -1 : 0;
        if (step == 0 || _techs.Count == 0 || _menuOpen || ShopOpen || SubWindowOpen) return false;
        _techSelected = ((_techSelected + step) % _techs.Count + _techs.Count) % _techs.Count;
        return true;
    }

    // Botões de técnica do celular: o mesmo que o clique na caixa da barra.
    TouchControls.TechInfo? TouchTech(int i)
    {
        if (i >= _techs.Count || InCutscene || TalkWindowOpen) return null;
        var t = _techs[i];
        float cd = _panel.Cooldown(t);
        return new(t.Name, t.Mp, t.Delay > 0 ? Math.Clamp(cd / (t.Delay / 60f), 0, 1) : 0, _mp < t.Mp);
    }

    void TouchUseTech(int i)
    {
        if (i < _techs.Count && !_menuOpen && !ShopOpen && !SubWindowOpen && !_quickOpen) { _techKey = i; _techSelected = i; }
    }

    // Tecla do aparelho em uso para a técnica direta n (1..6); sem ligação, nada.
    string TechKeyName(int n)
    {
        var device = _usingPad ? InputBindings.Pad : InputBindings.Keyboard;
        return PortSettings.Current.Bindings.Get(device, $"tecnica{n}").FirstOrDefault() is { } b ? InputBindings.Describe(b) : "";
    }

    void UpdateTechBar()
    {
        if (_techBar == null) return;
        // Some com a janela de fala aberta (a barra cobria a moldura de baixo).
        // Celular com os botões da tela à vista: as técnicas são botões em volta do ATQ (TouchControls); a barra volta com o controle.
        _techBar.Visible = _techs.Count > 0 && !InCutscene && !_menuOpen && !TalkWindowOpen && !ShopOpen && !SubWindowOpen && !TouchControls.Showing;
        if (!_techBar.Visible) return;
        if (_techSelected >= _techs.Count) _techSelected = 0;
        for (int i = 0; i < 6; i++)
        {
            var slot = _techBoxes[i];
            if (i >= _techs.Count) { slot.Visible = false; continue; }
            slot.Visible = true;
            var t = _techs[i];
            float cd = _panel.Cooldown(t);
            bool noMp = _mp < t.Mp;
            _techKeys[i].Text = TechKeyName(i + 1);
            ((Control)_techKeys[i].GetParent()).Visible = _techKeys[i].Text.Length > 0;
            _techLabels[i].Text = $"{t.Name}\n{t.Mp} MP";
            _techLabels[i].Modulate = noMp ? new Color(1, 0.5f, 0.5f) : i == _techSelected ? new Color(1, 1, 0.6f) : Colors.White;
            float frac = t.Delay > 0 ? Math.Clamp(cd / (t.Delay / 60f), 0, 1) : 0;
            // A sombra da recarga desce de cima para baixo (cobre a caixa toda e vai encolhendo).
            _techCool[i].Visible = frac > 0;
            _techCool[i].Size = new Vector2(slot.Size.X, slot.Size.Y * frac);
            _techCool[i].Position = new Vector2(0, slot.Size.Y * (1 - frac));
            slot.AddThemeStyleboxOverride("panel", i == _techSelected ? _techStyleSelected : _techStyle);
        }
        // No pé da tela, com a hotbar de itens logo acima (pedido do usuário: números em cima, técnicas embaixo), na mesma escala
        // (altura da janela, base 720); em 4:3 começa depois do painel de status do canto de baixo à esquerda, como a hotbar.
        var size = GetViewport().GetVisibleRect().Size;
        float s = size.Y / 720f;
        _techBar.Scale = new Vector2(s, s);
        _techBar.ResetSize();
        var barSize = _techBar.GetCombinedMinimumSize() * s;
        float x = Math.Max((size.X - barSize.X) / 2, 290 * s);
        _techBar.Position = new Vector2(x, size.Y - 10 * s - barSize.Y);
    }
}
