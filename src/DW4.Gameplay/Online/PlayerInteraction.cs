using System.Numerics;

namespace DW4.Gameplay.Online;

/// <summary>Outro jogador visível na fase: nome e posição (unidades do jogo).</summary>
public sealed record NearbyPlayer(string Name, Vector3 Position);

public enum InteractAction { InviteToParty, AddFriend, Trade }

/// <summary>Uma linha do menu do F: ação, se dá para escolher e, se não, por quê.</summary>
public sealed record InteractOption(InteractAction Action, string Label, bool Enabled, string? Reason);

/// <summary>O que o jogador sabe de si e do alvo para montar o menu.</summary>
public sealed record InteractContext(PartyGroup? MyParty, string Me, bool TargetIsFriend, bool FriendRequestSent, bool TargetInOtherParty, bool Busy);

/// <summary>
/// Interagir com outro jogador (DECISÃO DO PORT, pedido do responsável em 2026-11-02; não existe no original): perto de alguém,
/// F abre o menu com convidar para o grupo, adicionar amigo e trocar itens. Convidar segue a party da DMO.API: só o líder, só
/// amigos mútuos (ver <see cref="PartyGroup"/>). Alcance em unidades do jogo; a coleta de itens do
/// original é a menos de 64 unidades (Drops), e o menu usa o dobro (HIPÓTESE de conforto, ajustar jogando).
/// </summary>
public static class PlayerInteraction
{
    public const float Range = 128f;

    /// <summary>O jogador mais perto dentro do alcance (no plano, sem altura), ou null.</summary>
    public static NearbyPlayer? Nearest(Vector3 me, IEnumerable<NearbyPlayer> others, float range = Range)
    {
        NearbyPlayer? best = null;
        float bestD = range * range;
        foreach (var p in others)
        {
            float dx = p.Position.X - me.X, dz = p.Position.Z - me.Z, d = dx * dx + dz * dz;
            if (d <= bestD) { bestD = d; best = p; }
        }
        return best;
    }

    public static IReadOnlyList<InteractOption> Options(string target, InteractContext c)
    {
        InteractOption invite;
        if (c.MyParty?.Contains(target) == true) invite = new(InteractAction.InviteToParty, "Convidar para o grupo", false, "Já está no seu grupo.");
        else if (c.TargetInOtherParty) invite = new(InteractAction.InviteToParty, "Convidar para o grupo", false, "Já está em outro grupo.");
        else if (!c.TargetIsFriend) invite = new(InteractAction.InviteToParty, "Convidar para o grupo", false, "Só amigos entram no grupo. Adicione como amigo primeiro.");
        else if (c.MyParty != null && !PartyGroup.Names.Equals(c.MyParty.Leader, c.Me))
            invite = new(InteractAction.InviteToParty, "Convidar para o grupo", false, "Só o líder convida.");
        else if (c.MyParty != null && c.MyParty.Count + c.MyParty.PendingInvites.Count >= Party.MaxPlayers)
            invite = new(InteractAction.InviteToParty, "Convidar para o grupo", false, $"Grupo cheio ({Party.MaxPlayers}).");
        else invite = new(InteractAction.InviteToParty, "Convidar para o grupo", true, null);

        var friend = c.TargetIsFriend ? new InteractOption(InteractAction.AddFriend, "Adicionar amigo", false, "Já é seu amigo.")
                   : c.FriendRequestSent ? new InteractOption(InteractAction.AddFriend, "Adicionar amigo", false, "Pedido já enviado.")
                   : new InteractOption(InteractAction.AddFriend, "Adicionar amigo", true, null);

        var trade = c.Busy ? new InteractOption(InteractAction.Trade, "Trocar itens", false, "Termine o que está fazendo.")
                           : new InteractOption(InteractAction.Trade, "Trocar itens", true, null);
        return [invite, friend, trade];
    }
}
