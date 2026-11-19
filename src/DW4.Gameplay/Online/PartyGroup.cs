namespace DW4.Gameplay.Online;

/// <summary>Resultado de uma ação social: ok ou um código com a mensagem para o jogador (os mesmos códigos vão pela rede).</summary>
public readonly record struct SocialResult(bool Ok, string Code, string Message)
{
    public static readonly SocialResult Success = new(true, "Ok", "");
    public static SocialResult Fail(string code, string message) => new(false, code, message);
}

/// <summary>Convite pendente para o grupo; vence em <see cref="PartyGroup.InviteLifetime"/>.</summary>
public sealed record PartyInvite(string Id, string From, string To, DateTime ExpiresUtc);

/// <summary>
/// Grupo (party) de até 4 jogadores. FATO do original: até 4 jogadores por partida (Party.MaxPlayers, lobby callshop.lua);
/// progresso da missão é do grupo, nível/EXP/BIT/inventário de cada um (docs/gameplay/fase4-levantamento-grupo.md).
/// DECISÃO DO PORT: líder, convite com validade, expulsar e passar a liderança são do online (o GameCube não tinha convite).
/// A fonte da verdade é a party da DMO.API (PartyService, decisões do responsável de 2026-10-18), que vale no launcher e no
/// jogo: até 4; convite só do líder e só entre amigos mútuos sem bloqueio; convite vale 60 s; se o líder sai, o membro mais
/// antigo vira líder; a party acaba quando sobra 1. Esta classe repete essas regras para o cliente e o servidor do DW4
/// avisarem antes de chamar a API e para os testes. Nomes comparados sem diferenciar maiúsculas, como no hub.
/// </summary>
public sealed class PartyGroup
{
    public static readonly TimeSpan InviteLifetime = TimeSpan.FromSeconds(60);
    public static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    readonly List<string> _members = [];
    readonly List<PartyInvite> _invites = [];
    int _nextInvite;

    public string Id { get; }
    public string Leader { get; private set; }
    public IReadOnlyList<string> Members => _members;
    public IReadOnlyList<PartyInvite> PendingInvites => _invites;
    public int Count => _members.Count;
    public bool IsFull => _members.Count >= Party.MaxPlayers;
    /// <summary>Acabou: sobrou um só depois de alguém sair (o líder sozinho com convites pendentes ainda não acabou).</summary>
    public bool IsDisbanded { get; private set; }

    public PartyGroup(string id, string leader)
    {
        Id = id;
        Leader = leader;
        _members.Add(leader);
    }

    public bool Contains(string name) => _members.Contains(name, Names);

    /// <summary>
    /// Convite do líder <paramref name="from"/> para <paramref name="to"/>, que precisa ser amigo mútuo sem bloqueio
    /// (<paramref name="mutualFriends"/>, a API confere). Vagas = 4 − membros − convites ainda válidos, para quatro aceites
    /// seguidos nunca passarem de 4.
    /// </summary>
    public SocialResult Invite(string from, string to, DateTime nowUtc, out PartyInvite? invite, bool mutualFriends = true)
    {
        invite = null;
        Prune(nowUtc);
        if (IsDisbanded) return SocialResult.Fail("SemParty", "O grupo acabou.");
        if (!Contains(from)) return SocialResult.Fail("NaoMembro", "Você não está neste grupo.");
        if (!Names.Equals(from, Leader)) return SocialResult.Fail("NaoLider", "Só o líder convida.");
        if (!mutualFriends) return SocialResult.Fail("NaoAmigo", "Só amigos entram no grupo. Adicione como amigo primeiro.");
        if (Names.Equals(from, to)) return SocialResult.Fail("Proprio", "Você já está no grupo.");
        if (Contains(to)) return SocialResult.Fail("JaMembro", $"{to} já está no grupo.");
        if (_invites.Any(i => Names.Equals(i.To, to))) return SocialResult.Fail("JaConvidado", $"{to} já foi convidado.");
        if (_members.Count + _invites.Count >= Party.MaxPlayers) return SocialResult.Fail("GrupoCheio", $"O grupo já tem {Party.MaxPlayers} jogadores.");
        invite = new PartyInvite($"{Id}-{++_nextInvite}", from, to, nowUtc + InviteLifetime);
        _invites.Add(invite);
        return SocialResult.Success;
    }

    /// <summary>Aceite do convite: o convidado entra (se ainda couber e o convite não venceu).</summary>
    public SocialResult Accept(string inviteId, string who, DateTime nowUtc)
    {
        Prune(nowUtc);
        var inv = _invites.FirstOrDefault(i => i.Id == inviteId && Names.Equals(i.To, who));
        if (inv == null || IsDisbanded) return SocialResult.Fail("ConviteVencido", "O convite não vale mais.");
        _invites.Remove(inv);
        if (IsFull) return SocialResult.Fail("GrupoCheio", $"O grupo já tem {Party.MaxPlayers} jogadores.");
        _members.Add(who);
        return SocialResult.Success;
    }

    public SocialResult Decline(string inviteId, string who)
    {
        int n = _invites.RemoveAll(i => i.Id == inviteId && Names.Equals(i.To, who));
        return n > 0 ? SocialResult.Success : SocialResult.Fail("ConviteVencido", "O convite não vale mais.");
    }

    /// <summary>
    /// Saída (ou 30 s offline, na API). Se o líder sai, o membro mais antigo vira líder e os convites dele caem; se sobra um
    /// só, o grupo acaba.
    /// </summary>
    public SocialResult Leave(string who)
    {
        int i = _members.FindIndex(m => Names.Equals(m, who));
        if (i < 0) return SocialResult.Fail("NaoMembro", "Você não está neste grupo.");
        _members.RemoveAt(i);
        if (_members.Count <= 1)
        {
            _members.Clear();
            _invites.Clear();
            IsDisbanded = true;
            return SocialResult.Success;
        }
        if (Names.Equals(Leader, who)) { Leader = _members[0]; _invites.Clear(); }
        return SocialResult.Success;
    }

    public SocialResult Kick(string by, string who)
    {
        if (!Names.Equals(by, Leader)) return SocialResult.Fail("NaoLider", "Só o líder pode tirar alguém do grupo.");
        if (Names.Equals(by, who)) return SocialResult.Fail("Proprio", "Use sair do grupo.");
        return Leave(who);
    }

    public SocialResult PassLeader(string by, string to)
    {
        if (!Names.Equals(by, Leader)) return SocialResult.Fail("NaoLider", "Só o líder pode passar a liderança.");
        if (!Contains(to)) return SocialResult.Fail("NaoMembro", $"{to} não está no grupo.");
        Leader = _members.First(m => Names.Equals(m, to));
        return SocialResult.Success;
    }

    /// <summary>Tira os convites vencidos; devolve os que venceram (o servidor avisa quem convidou).</summary>
    public IReadOnlyList<PartyInvite> Prune(DateTime nowUtc)
    {
        var gone = _invites.Where(i => i.ExpiresUtc <= nowUtc).ToList();
        foreach (var g in gone) _invites.Remove(g);
        return gone;
    }
}
