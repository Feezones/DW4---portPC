namespace DW4.Net;

/// <summary>Quem é o jogador para o servidor, depois do ticket conferido na DMO.API.</summary>
/// <summary>Conta autenticada pelo ticket. <paramref name="HasTamer"/> = a conta tem um Tamer do RE:DIGITAL ONLINE; sem ele,
/// <paramref name="Tamer"/> é o usuário gerado da conta de jogo ("rd" + 10 letras) e amigos/party da API não o acham.</summary>
public sealed record Identity(long AccountId, string Tamer, string? PartyId, bool HasTamer = true);

/// <summary>Pacote para mandar: destino, mensagem e se precisa chegar (confiável) ou pode perder (estado).</summary>
public readonly record struct Outgoing(int To, object Message, bool Reliable);

/// <summary>
/// Regras do servidor sem socket: quem está em qual instância de fase e quem vê quem.
///
/// Instância (DECISÃO DO RESPONSÁVEL, 2026-11-03): todo mundo na mesma fase se vê, em qualquer fase (cidade ou dungeon). O que é do
/// grupo é o combate: inimigos, dano e drops são compartilhados só entre os membros da mesma party na mesma fase
/// (<see cref="CombatGroup"/>, etapa 6d); quem não está no grupo vê os outros andando, mas tem os próprios inimigos.
/// </summary>
public sealed class World
{
    sealed class Player
    {
        public required int Id;
        public required Identity Who;
        public required string Character;
        public PlayerState? State;
        public string? Instance;
        public string? Group;
        /// <summary>Guilda do DW4 (0 = sem guilda) e a tag que aparece na placa (protocolo 8).</summary>
        public long GuildId;
        public string GuildTag = "";
    }

    readonly Dictionary<int, Player> _players = [];
    // Grupo de combate → membros na ordem de chegada; o primeiro é o anfitrião (roda os inimigos).
    readonly Dictionary<string, List<int>> _groups = [];
    // Último quadro da party mandado (party → texto do conteúdo), para só mandar quando muda.
    readonly Dictionary<string, string> _rosters = [];
    // Líder de cada party (party → conta), como a DMO.API disse por último.
    readonly Dictionary<string, long> _leaders = [];

    public int Count => _players.Count;
    public bool IsOnline(int id) => _players.ContainsKey(id);
    public IEnumerable<(int Id, long AccountId)> Accounts => _players.Values.Select(p => (p.Id, p.Who.AccountId));

    public static bool IsPublic(string stage) => stage.StartsWith("stagml", StringComparison.OrdinalIgnoreCase);

    /// <summary>Instância de visão: a fase (todos na mesma fase se veem).</summary>
    public static string InstanceOf(string stage, Identity who) => stage.ToLowerInvariant();

    /// <summary>
    /// Grupo de combate (inimigos, dano e drops compartilhados): a party na fase; sem party, só a conta. A cidade (stagml*) não
    /// tem combate, mas segue a mesma regra.
    /// </summary>
    public static string CombatGroup(string stage, Identity who) =>
        $"{stage.ToLowerInvariant()}#{(who.PartyId is { Length: > 0 } p ? "p:" + p : "a:" + who.AccountId)}";

    /// <summary>Jogador entrou (ticket aceito). Devolve o id de quem já estava com a mesma conta (para derrubar), ou null.</summary>
    public int? Join(int id, Identity who, string character)
    {
        var old = _players.Values.FirstOrDefault(p => p.Who.AccountId == who.AccountId)?.Id;
        _players[id] = new Player { Id = id, Who = who, Character = character };
        return old;
    }

    public List<Outgoing> Leave(int id)
    {
        var res = new List<Outgoing>();
        if (!_players.Remove(id, out var p)) return res;
        if (p.Instance != null) foreach (var o in In(p.Instance)) res.Add(new(o.Id, new Despawn(id), true));
        SetGroup(p, null, res);
        Roster(p.Who.PartyId, res);
        return res;
    }

    /// <summary>Estado novo: repassa para quem está na mesma instância; troca de instância some de uns e aparece para outros.</summary>
    public List<Outgoing> Update(int id, PlayerState state)
    {
        var res = new List<Outgoing>();
        if (!_players.TryGetValue(id, out var p)) return res;
        p.State = state;
        Move(p, InstanceOf(state.Stage, p.Who), res);
        SetGroup(p, CombatGroup(state.Stage, p.Who), res);
        if (p.Instance != null)
            foreach (var o in In(p.Instance))
                if (o.Id != id) res.Add(new(o.Id, new RemoteState(id, state), false));
        Roster(p.Who.PartyId, res);
        return res;
    }

    /// <summary>
    /// A party da conta mudou (a DMO.API avisa no heartbeat). A visão não muda (é por fase); muda o grupo de combate.
    /// <paramref name="leader"/> = conta do líder da party (0 = não sabe): troca de líder manda o quadro de novo.
    /// </summary>
    public List<Outgoing> SetParty(int id, string? partyId, long leader = 0)
    {
        var res = new List<Outgoing>();
        if (!_players.TryGetValue(id, out var p)) return res;
        bool newLeader = partyId is { Length: > 0 } && leader != 0 && _leaders.GetValueOrDefault(partyId) != leader;
        if (newLeader) _leaders[partyId!] = leader;
        if (p.Who.PartyId == partyId) { if (newLeader) Roster(partyId, res); return res; }
        var old = p.Who.PartyId;
        p.Who = p.Who with { PartyId = partyId };
        Roster(old, res);
        Roster(partyId, res);
        // Saiu da party: o quadro dele esvazia.
        if (partyId is not { Length: > 0 }) res.Add(new(id, new PartyRoster([]), true));
        if (p.State != null)
        {
            Move(p, InstanceOf(p.State.Stage, p.Who), res);
            SetGroup(p, CombatGroup(p.State.Stage, p.Who), res);
        }
        return res;
    }

    /// <summary>Membros do grupo de combate de <paramref name="id"/> (o primeiro é o anfitrião); vazio sem estado.</summary>
    public IReadOnlyList<int> GroupOf(int id) =>
        _players.TryGetValue(id, out var p) && p.Group != null && _groups.TryGetValue(p.Group, out var g) ? g : [];

    /// <summary>Pacote do combate em grupo: vai para os outros membros do grupo de quem mandou.</summary>
    public List<Outgoing> Relay(int from, byte[] payload, bool reliable)
    {
        var res = new List<Outgoing>();
        foreach (var m in GroupOf(from))
            if (m != from) res.Add(new(m, new CoopFrom(from, payload), reliable));
        return res;
    }

    // Troca de grupo de combate (fase ou party): sai do antigo, entra no novo (no fim da fila) e avisa os dois grupos.
    void SetGroup(Player p, string? group, List<Outgoing> res)
    {
        if (p.Group == group) return;
        if (p.Group != null && _groups.TryGetValue(p.Group, out var old))
        {
            old.Remove(p.Id);
            if (old.Count == 0) _groups.Remove(p.Group);
            else Announce(old, res);
        }
        p.Group = group;
        if (group == null) return;
        if (!_groups.TryGetValue(group, out var g)) _groups[group] = g = [];
        g.Add(p.Id);
        Announce(g, res);
    }

    static void Announce(List<int> g, List<Outgoing> res)
    {
        var msg = new CoopGroup(g[0], g.Take(Protocol.MaxGroup).ToArray());
        foreach (var m in g) res.Add(new(m, msg, true));
    }

    // Quadro da party (membros no DW4 com estado): manda para todos eles quando o conteúdo muda (entrou, saiu, HP, MP, nível, fase).
    void Roster(string? party, List<Outgoing> res)
    {
        if (party is not { Length: > 0 }) return;
        var members = _players.Values.Where(m => m.Who.PartyId == party && m.State != null).OrderBy(m => m.Id).ToList();
        if (members.Count == 0) { _rosters.Remove(party); _leaders.Remove(party); return; }
        long leader = _leaders.GetValueOrDefault(party);
        var entries = members.Take(Protocol.MaxGroup).Select(m => new PartyEntry(m.Id, m.Character, m.State!.Model, m.State.Level,
            m.State.Hp, m.State.MaxHp, m.State.Mp, m.State.MaxMp, m.State.Stage.ToLowerInvariant(), m.Who.AccountId == leader)).ToArray();
        var key = string.Join("|", entries.Select(e => e.ToString()));
        if (_rosters.TryGetValue(party, out var last) && last == key) return;
        _rosters[party] = key;
        var msg = new PartyRoster(entries);
        foreach (var m in members) res.Add(new(m.Id, msg, true));
    }

    /// <summary>
    /// Guilda do jogador mudou (entrou, saiu, criou, a guilda acabou): guarda para o chat da guilda e manda a tag nova para quem o
    /// vê na fase (e para ele mesmo, que mostra a própria placa).
    /// </summary>
    public List<Outgoing> SetGuild(int id, long guildId, string tag)
    {
        var res = new List<Outgoing>();
        if (!_players.TryGetValue(id, out var p)) return res;
        p.GuildId = guildId;
        if (p.GuildTag == tag) return res;
        p.GuildTag = tag;
        var msg = new PlayerTag(id, tag);
        res.Add(new(id, msg, true));
        if (p.Instance != null) foreach (var o in In(p.Instance)) if (o.Id != id) res.Add(new(o.Id, msg, true));
        return res;
    }

    public long GuildOf(int id) => _players.TryGetValue(id, out var p) ? p.GuildId : 0;

    /// <summary>Jogadores da guilda no DW4 agora (em qualquer fase).</summary>
    public List<int> GuildOnline(long guildId) => guildId == 0 ? [] : _players.Values.Where(p => p.GuildId == guildId).Select(p => p.Id).ToList();

    /// <summary>Tem colega da mesma guilda no grupo de combate (party na mesma fase)? Para o XP da guilda em dobro.</summary>
    public bool GuildmateInGroup(int id)
    {
        long g = GuildOf(id);
        return g != 0 && GroupOf(id).Any(m => m != id && GuildOf(m) == g);
    }

    /// <summary>Jogador online pelo nome do personagem (ou do Tamer), sem diferença de maiúsculas; -1 se não está.</summary>
    public int FindByName(string name)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        var p = _players.Values.FirstOrDefault(p => p.Character.Length > 0 && cmp.Equals(p.Character, name))
             ?? _players.Values.FirstOrDefault(p => cmp.Equals(p.Who.Tamer, name));
        return p?.Id ?? -1;
    }

    /// <summary>Fase e nível do jogador (do último estado), para a lista da guilda.</summary>
    public (string Stage, int Level) Where(int id) =>
        _players.TryGetValue(id, out var p) && p.State != null ? (p.State.Stage.ToLowerInvariant(), p.State.Level) : ("", 0);

    public Identity? Who(int id) => _players.TryGetValue(id, out var p) ? p.Who : null;

    /// <summary>É o líder da party dele (pelo que a DMO.API disse por último)?</summary>
    public bool IsLeader(int id) =>
        _players.TryGetValue(id, out var p) && p.Who.PartyId is { Length: > 0 } party && _leaders.GetValueOrDefault(party) == p.Who.AccountId;

    /// <summary>Jogadores no DW4 da party (ids), em qualquer fase.</summary>
    public List<(int Id, long AccountId)> PartyMembers(string? party) => party is not { Length: > 0 } ? []
        : _players.Values.Where(m => m.Who.PartyId == party).Select(m => (m.Id, m.Who.AccountId)).ToList();

    // Nome no chat: o personagem da placa; sem personagem (só o ticket gasto), o Tamer.
    /// <summary>Nome que os outros veem (personagem; sem personagem, o Tamer). Vazio se o jogador não está no mundo.</summary>
    public string Name(int id) => _players.TryGetValue(id, out var p) ? ChatName(p) : "";

    static string ChatName(Player p) => p.Character is { Length: > 0 } c ? c : p.Who.Tamer;

    /// <summary>
    /// Chat (DECISÃO DO PORT, 0.6.8): geral = todos no DW4; grupo = a party da conta, em qualquer fase; privada = o jogador cujo
    /// personagem ou Tamer se chama <paramref name="to"/> (sem diferença de maiúsculas), e quem mandou recebe a cópia. O texto já
    /// vem limpo. Se não dá para entregar, nada sai e <paramref name="notice"/> diz por quê (só para quem mandou).
    /// </summary>
    public List<Outgoing> Chat(int from, ChatTo channel, string to, string text, out string? notice)
    {
        var res = new List<Outgoing>();
        notice = null;
        if (!_players.TryGetValue(from, out var me) || text.Length == 0) return res;
        string name = ChatName(me);
        switch (channel)
        {
            case ChatTo.Global:
                foreach (var p in _players.Values) res.Add(new(p.Id, new ChatFrom(channel, from, name, "", text), true));
                break;
            case ChatTo.Party:
                if (me.Who.PartyId is not { Length: > 0 } party) { notice = "Você não está em um grupo."; break; }
                foreach (var p in _players.Values)
                    if (p.Who.PartyId == party) res.Add(new(p.Id, new ChatFrom(channel, from, name, "", text), true));
                break;
            case ChatTo.Guild:
                if (me.GuildId == 0) { notice = "Você não está em uma guilda."; break; }
                foreach (var p in _players.Values)
                    if (p.GuildId == me.GuildId) res.Add(new(p.Id, new ChatFrom(channel, from, name, "", text), true));
                break;
            case ChatTo.Whisper:
                var cmp = StringComparer.OrdinalIgnoreCase;
                var target = _players.Values.FirstOrDefault(p => p.Character.Length > 0 && cmp.Equals(p.Character, to))
                          ?? _players.Values.FirstOrDefault(p => cmp.Equals(p.Who.Tamer, to));
                if (target == null) { notice = $"{to} não está online."; break; }
                if (target.Id == from) { notice = "Essa mensagem é para você mesmo."; break; }
                var msg = new ChatFrom(channel, from, name, ChatName(target), text);
                res.Add(new(target.Id, msg, true));
                res.Add(new(from, msg, true));
                break;
        }
        return res;
    }

    /// <summary>Os dois estão na mesma instância de fase (o menu do F só vale para quem o jogador vê).</summary>
    public bool SameInstance(int a, int b) =>
        _players.TryGetValue(a, out var pa) && _players.TryGetValue(b, out var pb) && pa.Instance != null && pa.Instance == pb.Instance;

    void Move(Player p, string instance, List<Outgoing> res)
    {
        if (p.Instance == instance) return;
        if (p.Instance != null)
            foreach (var o in In(p.Instance))
                if (o.Id != p.Id) { res.Add(new(o.Id, new Despawn(p.Id), true)); res.Add(new(p.Id, new Despawn(o.Id), true)); }
        p.Instance = instance;
        foreach (var o in In(instance))
            if (o.Id != p.Id && o.State != null)
            {
                res.Add(new(o.Id, new Spawn(p.Id, p.Character, p.Who.Tamer, p.State!, p.GuildTag), true));
                res.Add(new(p.Id, new Spawn(o.Id, o.Character, o.Who.Tamer, o.State, o.GuildTag), true));
            }
    }

    IEnumerable<Player> In(string instance) => _players.Values.Where(p => p.Instance == instance).ToList();
}
