namespace DW4.Net;

/// <summary>Quem é o jogador para o servidor, depois do ticket conferido na DMO.API.</summary>
public sealed record Identity(long AccountId, string Tamer, string? PartyId);

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
    }

    readonly Dictionary<int, Player> _players = [];

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
        return res;
    }

    /// <summary>Estado novo: repassa para quem está na mesma instância; troca de instância some de uns e aparece para outros.</summary>
    public List<Outgoing> Update(int id, PlayerState state)
    {
        var res = new List<Outgoing>();
        if (!_players.TryGetValue(id, out var p)) return res;
        p.State = state;
        Move(p, InstanceOf(state.Stage, p.Who), res);
        if (p.Instance != null)
            foreach (var o in In(p.Instance))
                if (o.Id != id) res.Add(new(o.Id, new RemoteState(id, state), false));
        return res;
    }

    /// <summary>A party da conta mudou (a DMO.API avisa no heartbeat). A visão não muda (é por fase); muda o grupo de combate.</summary>
    public List<Outgoing> SetParty(int id, string? partyId)
    {
        var res = new List<Outgoing>();
        if (!_players.TryGetValue(id, out var p) || p.Who.PartyId == partyId) return res;
        p.Who = p.Who with { PartyId = partyId };
        if (p.State != null) Move(p, InstanceOf(p.State.Stage, p.Who), res);
        return res;
    }

    public Identity? Who(int id) => _players.TryGetValue(id, out var p) ? p.Who : null;

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
                res.Add(new(o.Id, new Spawn(p.Id, p.Character, p.Who.Tamer, p.State!), true));
                res.Add(new(p.Id, new Spawn(o.Id, o.Character, o.Who.Tamer, o.State), true));
            }
    }

    IEnumerable<Player> In(string instance) => _players.Values.Where(p => p.Instance == instance).ToList();
}
