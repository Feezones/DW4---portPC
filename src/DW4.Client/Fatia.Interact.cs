using System;
using System.Collections.Generic;
using DW4.Gameplay;
using DW4.Gameplay.Online;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Menu do F (DECISÃO DO PORT, pedido do responsável em 2026-11-02; ADR 0003): perto de outro jogador, F pergunta ao servidor a
/// relação (amigo, pedido, party, pela DMO.API) e abre o menu de <see cref="PlayerInteraction.Options(InteractView)"/>:
/// convidar para o grupo, adicionar amigo e trocar itens. Quem recebe um pedido de amizade ou um convite de grupo vê uma
/// pergunta com aceitar e recusar. Mouse ou cima/baixo e Enter; Esc ou F fecha.
/// A troca fica desligada até os saves irem para o servidor (sem isso, item duplicado vira trapaça).
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
        var me = _player.GlobalPosition;
        int best = -1;
        float bestD = PlayerInteraction.Range * PlayerInteraction.Range;
        foreach (var (id, a) in _avatars)
        {
            var p = a.Root.GlobalPosition;
            float dx = p.X - me.X, dz = p.Z - me.Z, d = dx * dx + dz * dz;
            if (d <= bestD) (bestD, best) = (d, id);
        }
        if (best < 0 || !Online.Remotes.TryGetValue(best, out var r)) { Toast("Chegue mais perto de outro jogador."); return true; }
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _interactAskedAt < 0.5) return true;
        _interactAskedAt = now;
        _interactTarget = best;
        _interactName = r.Character;
        Online.Interact(best, "relation");
        return true;
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
            _ => "",
        };
        if (!r.Ok) Toast(r.Message.Length > 0 ? r.Message : "Não deu certo.");
        else if (done.Length > 0) Toast(done);
    }

    void OnSocialPrompt(SocialPrompt p)
    {
        _prompts.Enqueue(p);
        if (!_interactOpen) ShowNextPrompt();
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
                _ => null,
            };
            string label = o.Action == InteractAction.AddFriend && relation == "requestReceived" && o.Enabled ? "Aceitar pedido de amizade" : o.Label;
            bool enabled = o.Enabled;
            string? reason = o.Reason;
            if (o.Action == InteractAction.Trade) (enabled, reason) = (false, "Em breve: a troca chega quando os saves forem para o servidor.");
            rows.Add((label, enabled, reason, act));
        }
        OpenInteractWindow(_interactName, rows);
    }

    void ShowNextPrompt()
    {
        if (!_prompts.TryDequeue(out var p)) return;
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
