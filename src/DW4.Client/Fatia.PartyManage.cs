using System;
using System.Collections.Generic;
using System.Linq;
using DW4.Net;
using Godot;

namespace DW4.Client;

/// <summary>
/// Gestão do grupo (pedido do responsável, 2026-11-07). Quem cria o grupo (manda o primeiro convite aceito) é o líder; a regra e a
/// conferência são da DMO.API (PartyService) e o servidor do DW4 confere antes (GameServer.PartyManageCheck).
///  * Líder: botão direito no cartão de um membro no quadro da esquerda → "Passar líder" e "Expulsar do grupo".
///  * Qualquer membro: botão direito (ou clique) no ícone do próprio Digimon, no painel de status → "Sair do grupo".
///  * Celular: tocar no cartão ou no ícone faz o mesmo (o toque não tem botão direito). O toque nessas áreas não mexe o joystick.
/// Expulsar e sair pedem confirmação.
/// </summary>
public partial class Fatia
{
    Control? _selfIcon;

    bool PartyMenuBlocked => _menuOpen || ShopOpen || SubWindowOpen || _quickOpen || ChatOpen || TalkWindowOpen || InCutscene
        || _interactOpen || Online.State != Online.Status.Online;

    // PC: botão direito abre; celular: o toque (que chega como botão esquerdo) abre ao soltar.
    static bool OpensPartyMenu(InputEvent e) =>
        e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && !Touch
        || e is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } && Touch;

    void PartyCardInput(PartyCard card, InputEvent e)
    {
        if (!OpensPartyMenu(e)) return;
        GetViewport().SetInputAsHandled();
        if (PartyMenuBlocked || card.PlayerId < 0) return;
        var roster = Online.Party;
        var member = roster.FirstOrDefault(m => m.PlayerId == card.PlayerId);
        if (member == null) return;
        if (!roster.Any(m => m.PlayerId == Online.MyId && m.Leader))
        {
            var leader = roster.FirstOrDefault(m => m.Leader);
            Toast(leader == null ? "Só o líder do grupo pode passar a liderança ou expulsar."
                : $"Só o líder do grupo ({(leader.PlayerId == Online.MyId ? "você" : leader.Character)}) pode passar a liderança ou expulsar.");
            return;
        }
        int id = member.PlayerId;
        string name = member.Character;
        OpenInteractWindow(name,
        [
            ("Passar líder", true, null, () => Online.Interact(id, "party-promote")),
            ("Expulsar do grupo", true, null, () => ConfirmParty($"Expulsar {name} do grupo?", "Expulsar", () => Online.Interact(id, "party-kick"))),
        ]);
    }

    void SelfIconInput(InputEvent e)
    {
        bool open = OpensPartyMenu(e) || e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !Touch;
        if (!open) return;
        GetViewport().SetInputAsHandled();
        if (PartyMenuBlocked || !InParty) return;
        bool lead = Online.Party.Any(m => m.PlayerId == Online.MyId && m.Leader);
        OpenInteractWindow(lead ? "Grupo (você é o líder)" : "Grupo",
        [
            ("Sair do grupo", true, null, () => ConfirmParty(
                lead ? "Sair do grupo? A liderança passa para quem entrou primeiro." : "Sair do grupo?", "Sair",
                () => Online.Interact(Online.MyId, "party-leave"))),
        ]);
    }

    // A janela anterior já fechou (o botão fecha antes de agir): abre a pergunta no lugar.
    void ConfirmParty(string question, string yes, Action act) =>
        OpenInteractWindow(question, [(yes, true, null, act), ("Cancelar", true, null, null)]);

    // O quadro só vem para quem está numa party (vazio = sem party).
    bool InParty => Online.Party.Any(m => m.PlayerId == Online.MyId);

    /// <summary>Cartão do quadro ou ícone próprio no ponto (o toque ali vai para a interface, não para o joystick).</summary>
    bool PartyHudAt(Vector2 p) =>
        _partyCards.Any(c => c.Root.IsVisibleInTree() && c.Root.GetGlobalRect().HasPoint(p))
        || _selfIcon is { Visible: true } s && s.GetGlobalRect().HasPoint(p);

    // Por quadro: põe a área de clique em cima do rosto do painel de status (só em grupo; a HUD em texto não tem rosto).
    void TickSelfIcon()
    {
        var rect = InParty ? _overlay?.IconScreenRect() : null;
        if (rect == null) { if (_selfIcon != null) _selfIcon.Visible = false; return; }
        if (_selfIcon == null)
        {
            var layer = new CanvasLayer { Layer = 14 };
            _selfIcon = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Visible = false };
            _selfIcon.GuiInput += SelfIconInput;
            layer.AddChild(_selfIcon);
            AddChild(layer);
        }
        _selfIcon.TooltipText = Touch ? "Toque para sair do grupo" : "Clique: sair do grupo";
        if (OS.IsDebugBuild() && !_selfIcon.Visible) GD.Print($"[grupo] ícone próprio em {rect.Value}");
        _selfIcon.Position = rect.Value.Position;
        _selfIcon.Size = rect.Value.Size;
        _selfIcon.Visible = true;
    }

    // Resposta do servidor às ações do grupo (o quadro novo chega em seguida pelo PartyRoster).
    string PartyManageDone(InteractResult r)
    {
        string name = Online.Party.FirstOrDefault(m => m.PlayerId == r.TargetId)?.Character ?? "O jogador";
        return r.Action switch
        {
            "party-leave" => "Você saiu do grupo.",
            "party-kick" => $"{name} foi tirado do grupo.",
            "party-promote" => $"{name} agora é o líder do grupo.",
            _ => "",
        };
    }
}
