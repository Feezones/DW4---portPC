using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Quadro do grupo (pedido do responsável, 2026-11-03): no lado esquerdo da tela, um cartão por membro da party que está no DW4
/// (você não entra), com nome, nível, HP e MP. Quem está em outra fase aparece apagado, com o nome da fase. Os dados vêm do
/// servidor (<see cref="PartyRoster"/>), que manda de novo sempre que algo muda.
/// DECISÃO DO PORT: no original os painéis dos outros jogadores ficam ao lado do seu, embaixo (tela dividida num só aparelho);
/// aqui é uma lista simples, sem a arte do jogo.
/// Líder (2026-11-07): o cartão do líder tem a etiqueta LÍDER; o líder abre o menu do membro com o botão direito no cartão (no
/// celular, tocando no cartão): passar líder e expulsar (Fatia.PartyManage.cs).
/// </summary>
public partial class Fatia
{
    sealed class PartyCard
    {
        public required PanelContainer Root;
        public required Label Name, Leader;
        public required ProgressBar Hp, Mp;
        public int PlayerId = -1;
        public required Label HpText, MpText;
    }

    VBoxContainer? _partyBox;
    Label? _partyHead;
    readonly List<PartyCard> _partyCards = [];
    PartyEntry[]? _partyShown;

    void TickPartyHud()
    {
        var roster = Online.Party;
        if (ReferenceEquals(roster, _partyShown)) return;
        _partyShown = roster;
        var others = roster.Where(m => m.PlayerId != Online.MyId).ToList();
        bool iLead = roster.Any(m => m.PlayerId == Online.MyId && m.Leader);
        if (_partyBox == null)
        {
            if (others.Count == 0) return;
            var layer = new CanvasLayer { Layer = 14 };
            _partyBox = new VBoxContainer { OffsetLeft = 16, OffsetTop = 140, MouseFilter = Control.MouseFilterEnum.Ignore };
            _partyBox.AddThemeConstantOverride("separation", 6);
            _partyHead = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            _partyHead.AddThemeFontSizeOverride("font_size", 13);
            _partyHead.AddThemeConstantOverride("outline_size", 5);
            _partyHead.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            _partyHead.AddThemeColorOverride("font_color", QuickGold);
            _partyBox.AddChild(_partyHead);
            layer.AddChild(_partyBox);
            AddChild(layer);
        }
        if (_partyHead != null)
        {
            _partyHead.Visible = others.Count > 0;
            _partyHead.Text = iLead ? "GRUPO · você é o líder" : "GRUPO";
        }
        while (_partyCards.Count < others.Count) _partyCards.Add(NewPartyCard());
        for (int i = 0; i < _partyCards.Count; i++)
        {
            var c = _partyCards[i];
            c.Root.Visible = i < others.Count;
            if (!c.Root.Visible) continue;
            var m = others[i];
            c.PlayerId = m.PlayerId;
            c.Leader.Visible = m.Leader;
            c.Root.TooltipText = !iLead ? "" : Touch ? "Toque para passar a liderança ou expulsar" : "Botão direito: passar líder ou expulsar";
            bool here = m.Stage.Equals(_stageName, StringComparison.OrdinalIgnoreCase);
            c.Name.Text = $"{m.Character}  Lv {m.Level}" + (here ? "" : $"  ({m.Stage})");
            SetBar(c.Hp, c.HpText, m.Hp, m.MaxHp);
            SetBar(c.Mp, c.MpText, m.Mp, m.MaxMp);
            c.Root.Modulate = here ? Colors.White : new Color(1, 1, 1, 0.55f);
        }
    }

    static void SetBar(ProgressBar bar, Label text, int value, int max)
    {
        bar.MaxValue = Math.Max(1, max);
        bar.Value = Math.Clamp(value, 0, Math.Max(1, max));
        text.Text = $"{value}/{max}";
    }

    PartyCard NewPartyCard()
    {
        // Stop: o clique (botão direito / toque) no cartão abre o menu do grupo e não vira golpe nem defesa.
        var root = new PanelContainer { CustomMinimumSize = new Vector2(220, 0), MouseFilter = Control.MouseFilterEnum.Stop };
        root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0.55f), CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 6,
        });
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        var top = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var name = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        name.AddThemeFontSizeOverride("font_size", 15);
        var leader = new Label { Text = "LÍDER", Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        leader.AddThemeFontSizeOverride("font_size", 12);
        leader.AddThemeColorOverride("font_color", QuickGold);
        top.AddChild(name);
        top.AddChild(leader);
        col.AddChild(top);
        var (hp, hpText) = Meter(col, "HP", new Color(0.30f, 0.85f, 0.35f));
        var (mp, mpText) = Meter(col, "MP", new Color(0.30f, 0.55f, 1f));
        root.AddChild(col);
        _partyBox!.AddChild(root);
        var card = new PartyCard { Root = root, Name = name, Leader = leader, Hp = hp, Mp = mp, HpText = hpText, MpText = mpText };
        root.GuiInput += e => PartyCardInput(card, e);
        return card;
    }

    static (ProgressBar, Label) Meter(Container parent, string caption, Color fill)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var cap = new Label { Text = caption, CustomMinimumSize = new Vector2(26, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeFontSizeOverride("font_size", 12);
        var bar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(110, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.15f, 0.15f, 0.15f, 0.9f) });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        var text = new Label { HorizontalAlignment = HorizontalAlignment.Right, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        text.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(cap);
        row.AddChild(bar);
        row.AddChild(text);
        parent.AddChild(row);
        return (bar, text);
    }
}
