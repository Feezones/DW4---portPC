using System.Collections.Concurrent;
using System.Diagnostics;
using DW4.Net;
using LiteNetLib;

namespace DW4.Server;

/// <summary>
/// Laço do servidor: aceita conexões com a chave do DW4, confere o ticket na DMO.API, repassa posições pela <see cref="World"/>
/// e manda o heartbeat de presença. Tudo roda numa thread; as respostas da API voltam por uma fila.
/// </summary>
public sealed class GameServer
{
    static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    static readonly TimeSpan HeartbeatEvery = TimeSpan.FromSeconds(30);
    const int MaxStatesPerSecond = 40;

    readonly IAccountApi _api;
    readonly World _world = new();
    readonly NetManager _net;
    readonly ConcurrentQueue<Action> _main = new();
    readonly Dictionary<int, NetPeer> _peers = [];
    readonly Dictionary<int, DateTime> _connectedAt = [];
    readonly HashSet<int> _authenticating = [];
    readonly Dictionary<int, (long Second, int Count)> _rate = [];
    DateTime _nextHeartbeat = DateTime.UtcNow + HeartbeatEvery;

    public GameServer(IAccountApi api)
    {
        _api = api;
        var l = new EventBasedNetListener();
        _net = new NetManager(l) { DisconnectTimeout = 15000, AutoRecycle = true };
        l.ConnectionRequestEvent += r => r.AcceptIfKey(Protocol.ConnectionKey);
        l.PeerConnectedEvent += p => { _peers[p.Id] = p; _connectedAt[p.Id] = DateTime.UtcNow; };
        l.PeerDisconnectedEvent += (p, _) => Drop(p.Id);
        l.NetworkReceiveEvent += (p, reader, _, _) =>
        {
            var data = reader.GetRemainingBytes();
            try { OnPacket(p, Codec.Read(data)); }
            catch (FormatException ex) { Log($"[{p.Id}] pacote inválido ({ex.Message}); desconectado"); p.Disconnect(); }
        };
    }

    public int Port => _net.LocalPort;

    public bool Start(int port) => _net.Start(port);

    /// <summary>Uma volta do laço (chamada a cada ~15 ms).</summary>
    public void Tick()
    {
        _net.PollEvents();
        while (_main.TryDequeue(out var a)) a();
        var now = DateTime.UtcNow;
        foreach (var (id, at) in _connectedAt.ToList())
            if (!_world.IsOnline(id) && !_authenticating.Contains(id) && now - at > HelloTimeout && _peers.TryGetValue(id, out var p))
            {
                Log($"[{id}] sem Hello em {HelloTimeout.TotalSeconds} s; desconectado");
                p.Disconnect();
            }
        if (now >= _nextHeartbeat) { _nextHeartbeat = now + HeartbeatEvery; Heartbeat(); }
    }

    public void Stop()
    {
        foreach (var (_, acc) in _world.Accounts.ToList()) _ = SafeAsync(() => _api.PresenceAsync(acc, false));
        _net.Stop();
    }

    void OnPacket(NetPeer p, object msg)
    {
        switch (msg)
        {
            case Hello h when !_world.IsOnline(p.Id) && !_authenticating.Contains(p.Id):
                if (h.Version != Protocol.Version) { Reject(p, "Versão do jogo diferente da do servidor. Atualize pelo launcher."); return; }
                _authenticating.Add(p.Id);
                var ip = p.Address.ToString();
                _ = Task.Run(async () =>
                {
                    Identity? who = null; string? error = null;
                    try { who = await _api.RedeemAsync(h.Ticket, ip); }
                    catch (Exception ex) { error = ex.Message; }
                    _main.Enqueue(() => Admit(p, h, who, error));
                });
                break;
            case PlayerState s when _world.IsOnline(p.Id):
                if (!Allow(p.Id)) return;
                Send(_world.Update(p.Id, s));
                break;
            default:
                Log($"[{p.Id}] {msg.GetType().Name} fora de hora; desconectado");
                p.Disconnect();
                break;
        }
    }

    void Admit(NetPeer p, Hello h, Identity? who, string? error)
    {
        _authenticating.Remove(p.Id);
        if (!_peers.ContainsKey(p.Id)) return; // caiu enquanto conferia
        if (who == null)
        {
            Log($"[{p.Id}] entrada recusada ({error ?? "ticket inválido"})");
            Reject(p, error == null ? "Sessão do launcher inválida ou vencida. Feche o jogo e clique em Jogar de novo." : "Servidor da conta fora do ar. Tente de novo em instantes.");
            return;
        }
        if (_world.Join(p.Id, who, h.Character) is int old && _peers.TryGetValue(old, out var oldPeer))
        {
            Log($"[{old}] mesma conta entrou de novo; sessão antiga derrubada");
            Send(_world.Leave(old));
            Reject(oldPeer, "Sua conta entrou em outro lugar.");
        }
        Log($"[{p.Id}] entrou: {who.Tamer} (conta {who.AccountId}) com {h.Character}");
        Send(p, new Welcome(p.Id, who.Tamer), true);
        _ = SafeAsync(async () => { var party = await _api.PresenceAsync(who.AccountId, true); _main.Enqueue(() => Send(_world.SetParty(p.Id, party))); });
    }

    void Drop(int id)
    {
        _peers.Remove(id);
        _connectedAt.Remove(id);
        _rate.Remove(id);
        if (_world.Who(id) is { } who)
        {
            Log($"[{id}] saiu: {who.Tamer}");
            Send(_world.Leave(id));
            _ = SafeAsync(() => _api.PresenceAsync(who.AccountId, false));
        }
    }

    void Heartbeat()
    {
        foreach (var (id, acc) in _world.Accounts.ToList())
            _ = SafeAsync(async () => { var party = await _api.PresenceAsync(acc, true); _main.Enqueue(() => Send(_world.SetParty(id, party))); });
    }

    bool Allow(int id)
    {
        long sec = Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        var (s, n) = _rate.TryGetValue(id, out var v) ? v : (sec, 0);
        if (s != sec) (s, n) = (sec, 0);
        _rate[id] = (s, n + 1);
        return n < MaxStatesPerSecond;
    }

    void Send(List<Outgoing> list)
    {
        foreach (var o in list) if (_peers.TryGetValue(o.To, out var p)) Send(p, o.Message, o.Reliable);
    }

    static void Send(NetPeer p, object msg, bool reliable) =>
        p.Send(Codec.Write(msg), reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);

    /// <summary>Recusa: o motivo vai junto do pedido de desconexão (um pacote mandado logo antes se perderia).</summary>
    void Reject(NetPeer p, string reason) => _net.DisconnectPeer(p, Codec.Write(new Reject(reason)));

    async Task SafeAsync(Func<Task> f)
    {
        try { await f(); }
        catch (Exception ex) { Log("API: " + ex.Message); }
    }

    static void Log(string s) => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {s}");
}
