using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Menu de interagir (DECISÃO DO PORT, pedido do responsável em 2026-11-02; ADR 0003; era o F até a 0.6.22, G desde a 0.6.23,
/// Y no controle e botão JOGADOR no celular desde 2026-11-07): perto de outro jogador, a tecla pergunta ao servidor a relação (amigo, pedido, party, pela DMO.API) e abre o menu de <see cref="PlayerInteraction.Options(InteractView)"/>:
/// convidar para o grupo, adicionar amigo e trocar itens. Quem recebe um pedido de amizade ou um convite de grupo vê uma
/// pergunta com aceitar e recusar. Mouse/toque ou cima/baixo e Enter; Esc ou a mesma tecla fecha.
/// Perto de alguém aparece a dica "[G] Interagir com Fulano" (com a tecla que estiver no mapa de controles).
/// "Trocar itens" abre a janela de troca (Fatia.Trade.cs), feita pelo servidor nos saves da conta.
/// </summary>
public partial class Fatia
{
    bool _interactOpen;
    CanvasLayer? _interactLayer;
    int _interactTarget = -1;
    string _interactName = "";
    double _interactAskedAt = -10;
    readonly Queue<SocialPrompt> _prompts = new();

    bool InteractInput(InputEvent e)
    {
        if (_interactOpen)
        {
            if (e.IsActionPressed("ui_cancel") || PortInput.Pressed(e, "interagir")) { CloseInteract(); return true; }
            return false; // os botões recebem o resto (setas, Enter, mouse)
        }
        if (!PortInput.Pressed(e, "interagir")) return false;
        if (_menuOpen || ShopOpen || SubWindowOpen || _choice >= 0 || InCutscene || !_playerCtrl) return false;
        if (Online.State != Online.Status.Online) { Toast("Você não está online."); return true; }
        int best = NearestPlayer();
        if (best < 0 || !Online.Remotes.TryGetValue(best, out var r)) { Toast("Chegue mais perto de outro jogador."); return true; }
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _interactAskedAt < 0.5) return true;
        _interactAskedAt = now;
        _interactTarget = best;
        _interactName = r.Character;
        Online.Interact(best, "relation");
        return true;
    }

    // Jogador online mais perto dentro do alcance do menu (PlayerInteraction.Range), ou -1.
    int NearestPlayer()
    {
        if (_avatars.Count == 0) return -1;
        var me = _player.GlobalPosition;
        int best = -1;
        float bestD = PlayerInteraction.Range * PlayerInteraction.Range;
        foreach (var (id, a) in _avatars)
        {
            var p = a.Root.GlobalPosition;
            float dx = p.X - me.X, dz = p.Z - me.Z, d = dx * dx + dz * dz;
            if (d <= bestD) (bestD, best) = (d, id);
        }
        return best;
    }

    Label? _interactHint;
    int _interactHintTarget = -1;

    /// <summary>Tem alguém perto para o botão JOGADOR do celular (TouchControls).</summary>
    bool InteractAvailable => _interactHintTarget >= 0;

    // Por quadro: dica de interagir quando há outro jogador perto e nada aberto (no alto, logo abaixo dos avisos do Toast, longe das
    // barras do pé e dos botões do celular).
    void TickInteractHint()
    {
        bool can = Online.State == Online.Status.Online && _playerCtrl && !_interactOpen && !_menuOpen && !ShopOpen && !SubWindowOpen
            && _choice < 0 && !InCutscene && !ChatOpen && !TalkWindowOpen && !_spectating && (_pf == null || _hp > 0);
        int target = can ? NearestPlayer() : -1;
        if (target >= 0 && !Online.Remotes.ContainsKey(target)) target = -1;
        _interactHintTarget = target;
        if (target < 0) { if (_interactHint != null) _interactHint.Visible = false; return; }
        if (_interactHint == null)
        {
            var layer = new CanvasLayer { Layer = 15 };
            _interactHint = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -360, OffsetRight = 360,
                OffsetTop = 108, OffsetBottom = 140, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            _interactHint.AddThemeFontSizeOverride("font_size", 22);
            _interactHint.AddThemeConstantOverride("outline_size", 7);
            _interactHint.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            _interactHint.AddThemeColorOverride("font_color", QuickGold);
            layer.AddChild(_interactHint);
            AddChild(layer);
            UseGameFont(_interactHint);
        }
        string name = Online.Remotes[target].Character;
        _interactHint.Text = Touch && !_usingPad ? $"Toque em JOGADOR para interagir com {name}" : $"[{InteractKeyName()}] Interagir com {name}";
        _interactHint.Visible = true;
    }

    // Tecla de "interagir" do aparelho em uso (padrão: G no teclado, Y no controle).
    string InteractKeyName()
    {
        var device = _usingPad ? InputBindings.Pad : InputBindings.Keyboard;
        return PortSettings.Current.Bindings.Get(device, "interagir").FirstOrDefault() is { } b ? InputBindings.Describe(b) : "sem tecla";
    }

    void OnInteractResult(InteractResult r)
    {
        if (r.Action == "relation")
        {
            if (r.TargetId != _interactTarget) return;
            if (!r.Ok) { Toast(r.Message.Length > 0 ? r.Message : "Não deu para ver esse jogador agora."); return; }
            var view = new InteractView(r.HasParty, r.TargetInMyParty, r.AmLeader, r.PartyOccupied, r.Relation == "friend",
                r.Relation == "requestSent", r.TargetInOtherParty, _quickOpen || ShopOpen);
            ShowInteractMenu(r.TargetId, view, r.Relation);
            return;
        }
        string done = r.Action switch
        {
            "friend-request" => "Pedido de amizade enviado.",
            "party-invite" => "Convite para o grupo enviado.",
            "friend-accept" => "Agora vocês são amigos.",
            "party-accept" => "Você entrou no grupo.",
            _ => PartyManageDone(r),
        };
        if (!r.Ok) Toast(r.Message.Length > 0 ? r.Message : "Não deu certo.");
        else if (done.Length > 0) Toast(done);
    }

    void OnSocialPrompt(SocialPrompt p)
    {
        _prompts.Enqueue(p);
        if (!_interactOpen && !_guildOpen) ShowNextPrompt();
    }

    void ShowInteractMenu(int target, InteractView view, string relation)
    {
        var rows = new List<(string Label, bool Enabled, string? Reason, Action? Do)>();
        foreach (var o in PlayerInteraction.Options(view))
        {
            Action? act = o.Action switch
            {
                InteractAction.InviteToParty => () => Online.Interact(target, "party-invite"),
                InteractAction.AddFriend when relation == "requestReceived" => () => Online.Interact(target, "friend-accept"),
                InteractAction.AddFriend => () => Online.Interact(target, "friend-request"),
                InteractAction.Trade => () => AskTrade(target),
                _ => null,
            };
            string label = o.Action == InteractAction.AddFriend && relation == "requestReceived" && o.Enabled ? "Aceitar pedido de amizade" : o.Label;
            bool enabled = o.Enabled;
            string? reason = o.Reason;
            if (o.Action == InteractAction.Trade && !Session.Active) (enabled, reason) = (false, "Entre com um personagem para trocar itens.");
            rows.Add((label, enabled, reason, act));
        }
        // Guilda do DW4: Mestre e Sub-mestre convidam quem não tem guilda (o servidor confere de novo).
        if (Online.Guild is { Id: > 0, MyRank: <= 2 } g)
        {
            string theirs = Online.Remotes.TryGetValue(target, out var rp) ? rp.Guild : "";
            string? why = theirs.Length > 0 ? (theirs == g.Tag ? "Já está na sua guilda." : "Já está em outra guilda.")
                : g.Members.Length >= g.MaxMembers ? "A guilda está cheia." : null;
            rows.Add(("Convidar para a guilda", why == null, why, () => Online.SendGuild(new GuildCmd("invite", target))));
        }
        OpenInteractWindow(_interactName, rows);
    }

    void ShowNextPrompt()
    {
        if (!_prompts.TryDequeue(out var p)) return;
        if (p.Kind == "trade") { ShowTradePrompt(p); return; }
        if (p.Kind == "guild") { ShowGuildPrompt(p); return; }
        bool party = p.Kind == "party";
        string question = party ? $"{p.FromTamer} convidou você para o grupo." : $"{p.FromTamer} quer ser seu amigo.";
        OpenInteractWindow(question,
        [
            ("Aceitar", true, null, () => Online.Interact(p.FromId, party ? "party-accept" : "friend-accept", p.InviteId)),
            ("Recusar", true, null, () => Online.Interact(p.FromId, party ? "party-decline" : "friend-decline", p.InviteId)),
        ]);
    }

    void OpenInteractWindow(string title, List<(string Label, bool Enabled, string? Reason, Action? Do)> rows)
    {
        CloseInteract(showNext: false);
        _interactLayer = new CanvasLayer { Layer = 16 };
        AddChild(_interactLayer);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _interactLayer.AddChild(center);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", QuickBox(QuickBorder));
        center.AddChild(panel);
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        var head = new Label { Text = title, HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        head.AddThemeColorOverride("font_color", QuickGold);
        box.AddChild(head);
        Button? first = null;
        foreach (var (label, enabled, reason, act) in rows)
        {
            var b = new Button { Text = label, Disabled = !enabled, TooltipText = reason ?? "" };
            b.Pressed += () => { CloseInteract(); act?.Invoke(); };
            box.AddChild(b);
            if (!enabled && reason != null)
            {
                var why = new Label { Text = reason, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
                why.AddThemeFontSizeOverride("font_size", 13);
                why.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.85f));
                box.AddChild(why);
            }
            if (enabled) first ??= b;
        }
        var close = new Button { Text = "Fechar (Esc)" };
        close.Pressed += () => CloseInteract();
        box.AddChild(close);
        _interactOpen = true;
        _move.Stop();
        (first ?? close).CallDeferred(Control.MethodName.GrabFocus);
    }

    void CloseInteract(bool showNext = true)
    {
        _interactLayer?.QueueFree();
        _interactLayer = null;
        _interactOpen = false;
        if (showNext && _prompts.Count > 0) CallDeferred(MethodName.ShowNextPrompt);
    }
}
