using System;
using System.Collections.Generic;
using DW4.Net;
using Godot;
using LiteNetLib;

namespace DW4.Client;

/// <summary>Outro jogador visto pelo servidor: nomes e os dois últimos estados (para interpolar).</summary>
public sealed class RemotePlayer
{
    public required int Id;
    public required string Character;
    public required string Tamer;
    public PlayerState State = null!;
    public double ReceivedAt;
}

/// <summary>
/// Conexão com o servidor do DW4 (ADR 0003), viva entre as trocas de fase. Liga só com o ticket do launcher e o endereço do
/// servidor: DW4.exe -- --conta X --ticket Y [--servidor host:porta] (ou DW4_SERVIDOR; sem nenhum, usa o oficial, DefaultServer;
/// para teste local, DW4_TICKET=dev:Nome).
/// Sem isso o jogo segue local, como antes. O cliente manda o próprio estado a 20 Hz e recebe os outros da mesma instância.
/// </summary>
public static class Online
{
    public enum Status { Off, Connecting, Online, Failed }

    static NetManager? s_net;
    static NetPeer? s_server;
    static Hello? s_hello;
    static double s_lastSend;
    static PlayerState? s_lastState;
    // Resgate do ticket na abertura (ver Claim): conexão só para gastar o ticket dentro dos 2 min; o pedido de entrar com o
    // personagem que chegar nesse meio tempo espera aqui.
    static bool s_claiming;
    static (string Character, string Model, int Level)? s_wanted;

    public static Status State { get; private set; } = Status.Off;
    public static string? Error { get; private set; }
    public static int MyId { get; private set; }
    public static string? Tamer { get; private set; }
    public static readonly Dictionary<int, RemotePlayer> Remotes = [];
    /// <summary>Avisos para o chat do jogo ("Conectado", "Ana entrou"...).</summary>
    public static event Action<string>? Notice;
    /// <summary>Resposta do servidor ao menu do F.</summary>
    public static event Action<InteractResult>? Result;
    /// <summary>Pedido de amizade ou convite de grupo de outro jogador.</summary>
    public static event Action<SocialPrompt>? Prompt;
    /// <summary>Grupo de combate atual (a party na mesma fase; anfitrião primeiro), ou null fora do online.</summary>
    public static CoopGroup? Group { get; private set; }
    /// <summary>Membros da party que estão no DW4 (você incluído), com nível, HP e MP; vazio sem party.</summary>
    public static PartyEntry[] Party { get; private set; } = [];
    /// <summary>Pacote do combate em grupo (<see cref="CoopCodec"/>) de outro membro: (quem mandou, mensagem).</summary>
    public static event Action<int, object>? Coop;

    /// <summary>Combate em grupo: manda o pacote para os outros membros (sem garantia de entrega quando <paramref name="reliable"/> é false).</summary>
    public static void SendCoop(object msg, bool reliable)
    {
        if (State != Status.Online || Group is not { Members.Length: > 1 }) return;
        s_server?.Send(Codec.Write(new CoopRelay(reliable, CoopCodec.Write(msg))), reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
    }

    /// <summary>Menu do F: pede ao servidor (false se não está online).</summary>
    public static bool Interact(int target, string action, string inviteId = "")
    {
        if (State != Status.Online) return false;
        Send(new Interact(target, action, inviteId), true);
        return true;
    }

    static string? Arg(string name)
    {
        var a = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1];
        return null;
    }

    /// <summary>Servidor oficial (VPS da DMO.API). Usado quando o jogo abre pelo launcher (com ticket) sem --servidor nem DW4_SERVIDOR.</summary>
    public const string DefaultServer = "209.126.83.192:7031";

    public static string? ServerAddress => Arg("--servidor") ?? (OS.GetEnvironment("DW4_SERVIDOR") is { Length: > 0 } s ? s : DefaultServer);
    static string? Ticket => Session.LauncherTicket ?? (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TICKET") is { Length: > 0 } t ? t : null);

    /// <summary>
    /// Na abertura do jogo: gasta o ticket já. O ticket do launcher só vale 2 min para o primeiro uso, e a abertura, o menu e a
    /// criação do personagem passam disso (o amigo do responsável foi recusado assim em 2026-11-02). Depois do primeiro uso a
    /// DMO.API aceita o mesmo ticket do mesmo IP por 12 h, então a entrada com o personagem (Connect) reconecta à vontade.
    /// </summary>
    public static void Claim()
    {
        if (s_claiming || State != Status.Off || Ticket == null) return;
        s_claiming = true;
        Open("", "", 0);
        if (State != Status.Connecting) s_claiming = false;
    }

    /// <summary>
    /// Conecta com o personagem da vaga (a cada fase, se ainda não está online: assim volta sozinho depois de uma queda). Sem
    /// ticket, fica local.
    /// </summary>
    public static void Connect(string character, string model, int level)
    {
        if (s_claiming) { s_wanted = (character, model, level); return; }
        if (State is Status.Connecting or Status.Online) return;
        Open(character, model, level);
    }

    static void EndClaim()
    {
        s_claiming = false;
        s_net?.Stop();
        (s_net, s_server) = (null, null);
        Remotes.Clear(); Group = null; Party = [];
        if (State != Status.Failed) State = Status.Off;
        if (s_wanted is { } w && State == Status.Off) { s_wanted = null; Open(w.Character, w.Model, w.Level); }
        s_wanted = null;
    }

    static void Open(string character, string model, int level)
    {
        if (Ticket is not { } ticket || ServerAddress is not { } address) { State = Status.Off; return; }
        s_net?.Stop(); // tentativa anterior que falhou
        (s_net, s_server) = (null, null);
        Remotes.Clear(); Group = null; Party = [];
        var (host, port) = Split(address);
        s_hello = new Hello(Protocol.Version, ticket, character, model, level);
        var l = new EventBasedNetListener();
        NetManager? net = null; // eventos atrasados de uma conexão já trocada (fim do resgate) são ignorados
        l.PeerConnectedEvent += p => { if (s_net != net) return; s_server = p; Send(s_hello!, true); };
        l.PeerDisconnectedEvent += (_, info) =>
        {
            if (s_net != net) return;
            // Recusa do servidor: o motivo vem junto do pedido de desconexão.
            if (info.AdditionalData is { AvailableBytes: > 0 } extra)
                try { if (Codec.Read(extra.GetRemainingBytes()) is Reject r) { Fail(r.Reason); return; } } catch (FormatException) { }
            if (State != Status.Failed) Fail(State == Status.Online ? "Conexão com o servidor caiu (" + info.Reason + ")." : "Não conectou ao servidor (" + info.Reason + ").");
        };
        l.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            if (s_net != net) return;
            try { OnMessage(Codec.Read(reader.GetRemainingBytes())); }
            catch (FormatException ex) { GD.PushWarning("[online] pacote inválido: " + ex.Message); }
        };
        s_net = net = new NetManager(l) { DisconnectTimeout = 15000, AutoRecycle = true };
        s_net.Start();
        s_net.Connect(host, port, Protocol.ConnectionKey);
        State = Status.Connecting;
        GD.Print($"[online] conectando a {host}:{port}");
    }

    static (string, int) Split(string address)
    {
        int c = address.LastIndexOf(':');
        return c > 0 && int.TryParse(address[(c + 1)..], out var p) ? (address[..c], p) : (address, Protocol.DefaultPort);
    }

    static void OnMessage(object m)
    {
        switch (m)
        {
            case Welcome w when s_claiming:
                GD.Print($"[online] ticket aceito ({w.Tamer})");
                EndClaim();
                break;
            case Welcome w:
                (MyId, Tamer, State) = (w.PlayerId, w.Tamer, Status.Online);
                Notice?.Invoke($"Online como {w.Tamer}.");
                if (s_lastState != null) Send(s_lastState, false);
                break;
            case Reject r:
                Fail(r.Reason);
                break;
            case Spawn s:
                Remotes[s.PlayerId] = new RemotePlayer { Id = s.PlayerId, Character = s.Character, Tamer = s.Tamer, State = s.State, ReceivedAt = Now };
                break;
            case InteractResult ir:
                Result?.Invoke(ir);
                break;
            case SocialPrompt sp:
                Prompt?.Invoke(sp);
                break;
            case Despawn d:
                Remotes.Remove(d.PlayerId);
                break;
            case CoopGroup g:
                Group = g;
                break;
            case PartyRoster pr:
                Party = pr.Members;
                break;
            case CoopFrom cf:
                try { Coop?.Invoke(cf.FromId, CoopCodec.Read(cf.Payload)); }
                catch (FormatException ex) { GD.PushWarning("[online] pacote do grupo inválido: " + ex.Message); }
                break;
            case RemoteState rs when Remotes.TryGetValue(rs.PlayerId, out var r):
                r.State = rs.State;
                r.ReceivedAt = Now;
                break;
        }
    }

    static void Fail(string why)
    {
        State = Status.Failed;
        Error = why;
        Remotes.Clear(); Group = null; Party = [];
        GD.PushWarning("[online] " + why);
        Notice?.Invoke(why + " Jogando sem os outros jogadores.");
        s_server = null;
        if (s_claiming) { s_claiming = false; s_wanted = null; }
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Uma vez por quadro: recebe pacotes e manda o estado (no máximo 20 por segundo, e só o que mudou ou 1 por segundo).</summary>
    public static void Poll(PlayerState? me)
    {
        s_net?.PollEvents();
        if (me == null) return;
        double now = Now;
        bool changed = s_lastState != me;
        if (now - s_lastSend < 1.0 / Protocol.StateRate || (!changed && now - s_lastSend < 1.0)) return;
        s_lastState = me;
        s_lastSend = now;
        if (State == Status.Online) Send(me, false);
    }

    static void Send(object msg, bool reliable) =>
        s_server?.Send(Codec.Write(msg), reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Sequenced);

    /// <summary>Volta ao menu ou fecha o jogo.</summary>
    public static void Disconnect()
    {
        if (s_claiming) return; // o resgate da abertura termina sozinho
        s_net?.Stop();
        s_net = null;
        s_server = null;
        Remotes.Clear(); Group = null; Party = [];
        if (State != Status.Failed) State = Status.Off;
    }
}
