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

    public static Status State { get; private set; } = Status.Off;
    public static string? Error { get; private set; }
    public static int MyId { get; private set; }
    public static string? Tamer { get; private set; }
    public static readonly Dictionary<int, RemotePlayer> Remotes = [];
    /// <summary>Avisos para o chat do jogo ("Conectado", "Ana entrou"...).</summary>
    public static event Action<string>? Notice;

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

    /// <summary>Conecta (uma vez por abertura do jogo) com o personagem da vaga. Sem ticket ou sem servidor, fica local.</summary>
    public static void Connect(string character, string model, int level)
    {
        if (State is Status.Connecting or Status.Online) return;
        if (Ticket is not { } ticket || ServerAddress is not { } address) { State = Status.Off; return; }
        var (host, port) = Split(address);
        s_hello = new Hello(Protocol.Version, ticket, character, model, level);
        var l = new EventBasedNetListener();
        l.PeerConnectedEvent += p => { s_server = p; Send(s_hello!, true); };
        l.PeerDisconnectedEvent += (_, info) =>
        {
            // Recusa do servidor: o motivo vem junto do pedido de desconexão.
            if (info.AdditionalData is { AvailableBytes: > 0 } extra)
                try { if (Codec.Read(extra.GetRemainingBytes()) is Reject r) { Fail(r.Reason); return; } } catch (FormatException) { }
            if (State != Status.Failed) Fail(State == Status.Online ? "Conexão com o servidor caiu (" + info.Reason + ")." : "Não conectou ao servidor (" + info.Reason + ").");
        };
        l.NetworkReceiveEvent += (_, reader, _, _) =>
        {
            try { OnMessage(Codec.Read(reader.GetRemainingBytes())); }
            catch (FormatException ex) { GD.PushWarning("[online] pacote inválido: " + ex.Message); }
        };
        s_net = new NetManager(l) { DisconnectTimeout = 15000, AutoRecycle = true };
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
            case Despawn d:
                Remotes.Remove(d.PlayerId);
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
        Remotes.Clear();
        GD.PushWarning("[online] " + why);
        Notice?.Invoke(why + " Jogando sem os outros jogadores.");
        s_server = null;
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
        s_net?.Stop();
        s_net = null;
        s_server = null;
        Remotes.Clear();
        if (State != Status.Failed) State = Status.Off;
    }
}
