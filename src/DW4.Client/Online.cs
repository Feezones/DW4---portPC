using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    /// <summary>Mensagem de chat que chegou do servidor (geral, grupo ou privada).</summary>
    public static event Action<ChatFrom>? ChatIn;

    /// <summary>Chat: manda a mensagem ao servidor (false se não está online; aí ela não sai).</summary>
    public static bool SendChat(ChatTo channel, string to, string text)
    {
        if (State != Status.Online) return false;
        Send(new ChatSend(channel, to, text), true);
        return true;
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
    /// Na abertura do jogo: conecta já, sem personagem (a "conexão do menu"). O ticket do launcher só vale 2 min para o primeiro
    /// uso, e a abertura, o menu e a criação do personagem passam disso (o amigo do responsável foi recusado assim em 2026-11-02).
    /// Depois do primeiro uso a DMO.API aceita o mesmo ticket do mesmo IP por 12 h, então a entrada com o personagem (Connect)
    /// reconecta à vontade. Desde os saves no servidor (2026-11-04) a conexão do menu fica aberta: é por ela que o menu lista,
    /// cria e apaga as vagas (<see cref="Request"/>).
    /// </summary>
    public static void Claim()
    {
        if (s_kicked || State is Status.Connecting or Status.Online || Ticket == null) return;
        s_claiming = true;
        Open("", "", 0);
        if (State != Status.Connecting) s_claiming = false;
    }

    /// <summary>Tem como falar com o servidor (ticket do launcher ou do login do celular)? Sem isso não há vagas.</summary>
    public static bool Available => Ticket != null && ServerAddress != null;

    /// <summary>A conta entrou em outro aparelho e derrubou esta: não reconecta sozinho (ver <see cref="Protocol.KickedReason"/>).</summary>
    public static bool Kicked => s_kicked;
    static bool s_kicked;
    /// <summary>Esta conexão foi derrubada porque a conta entrou em outro lugar.</summary>
    public static event Action? KickedOut;
    /// <summary>Conectou (ou reconectou) com o ticket aceito: o que ficou para gravar pode ir agora.</summary>
    public static event Action? Connected;

    /// <summary>"Tentar de novo" no menu depois de derrubado: libera a reconexão (que derruba o outro aparelho).</summary>
    public static void ResetKick()
    {
        s_kicked = false;
        if (State == Status.Failed) State = Status.Off;
    }

    /// <summary>
    /// Conecta com o personagem da vaga (a cada fase, se ainda não está online: assim volta sozinho depois de uma queda). Sem
    /// ticket, fica local.
    /// </summary>
    public static void Connect(string character, string model, int level)
    {
        if (s_kicked) return;
        if (s_claiming) { s_wanted = (character, model, level); return; }
        // A conexão do menu (sem personagem) vira a do jogo: reconecta anunciando o personagem.
        if (State is Status.Connecting or Status.Online && !s_menu) return;
        Open(character, model, level);
    }

    // ---------- pedidos com resposta (vagas no servidor) ----------

    static int s_nextReq;
    static readonly Dictionary<int, (TaskCompletionSource<SaveReply> Done, double At)> s_pending = [];
    static readonly List<(int Id, object Msg)> s_queue = [];
    /// <summary>Tempo para a resposta de um pedido de vaga (a API grava em milissegundos; isto é para a conexão caída).</summary>
    const double RequestTimeout = 20;

    /// <summary>
    /// Manda um pedido de vaga (SaveList/SaveWrite/SaveDelete, montado com o id dado) e espera a <see cref="SaveReply"/>. Fora do
    /// ar, (re)conecta e manda quando o ticket for aceito. Nunca lança: falha vira Ok=false com o motivo.
    /// </summary>
    public static System.Threading.Tasks.Task<SaveReply> Request(Func<int, object> make)
    {
        int id = ++s_nextReq;
        var done = new TaskCompletionSource<SaveReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (s_kicked) { done.SetResult(new SaveReply(id, false, Protocol.KickedReason, [])); return done.Task; }
        if (!Available) { done.SetResult(new SaveReply(id, false, "Sem conexão com o servidor: abra o jogo pelo launcher RE:DIGITAL.", [])); return done.Task; }
        s_pending[id] = (done, Now);
        var msg = make(id);
        if (State == Status.Online && !s_claiming && s_server != null) Send(msg, true);
        else
        {
            s_queue.Add((id, msg));
            if (State is Status.Off or Status.Failed)
            {
                if (s_hello is { } h) Open(h.Character, h.Model, h.Level); else Claim();
            }
        }
        return done.Task;
    }

    static void Finish(int id, SaveReply reply)
    {
        if (!s_pending.Remove(id, out var p)) return;
        s_queue.RemoveAll(q => q.Id == id);
        p.Done.TrySetResult(reply);
    }

    static void FailAll(string why)
    {
        foreach (var id in new List<int>(s_pending.Keys)) Finish(id, new SaveReply(id, false, why, []));
        s_queue.Clear();
    }

    static void Open(string character, string model, int level)
    {
        if (Ticket is not { } ticket || ServerAddress is not { } address) { State = Status.Off; return; }
        s_net?.Stop(); // tentativa anterior que falhou, ou a conexão do menu virando a do jogo
        (s_net, s_server) = (null, null);
        Remotes.Clear(); Group = null; Party = [];
        var (host, port) = Split(address);
        s_menu = character.Length == 0;
        s_hello = new Hello(Protocol.Version, ticket, character, model, level);
        var l = new EventBasedNetListener();
        NetManager? net = null; // eventos atrasados de uma conexão já trocada são ignorados
        l.PeerConnectedEvent += p => { if (s_net != net) return; s_server = p; Send(s_hello!, true); };
        l.PeerDisconnectedEvent += (_, info) =>
        {
            if (s_net != net) return;
            // Recusa do servidor: o motivo vem junto do pedido de desconexão.
            if (info.AdditionalData is { AvailableBytes: > 0 } extra)
                try
                {
                    if (Codec.Read(extra.GetRemainingBytes()) is Reject r)
                    {
                        if (r.Reason == Protocol.KickedReason) { s_kicked = true; Fail(r.Reason); KickedOut?.Invoke(); }
                        else Fail(r.Reason);
                        return;
                    }
                }
                catch (FormatException) { }
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
        GD.Print($"[online] conectando a {host}:{port}{(s_menu ? " (menu)" : "")}");
    }

    static bool s_menu;

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
                if (s_claiming)
                {
                    GD.Print($"[online] ticket aceito ({w.Tamer})");
                    s_claiming = false;
                    // O jogador já escolheu a vaga enquanto o ticket era conferido: reconecta com o personagem (os pedidos esperam).
                    if (s_wanted is { } want) { s_wanted = null; Open(want.Character, want.Model, want.Level); break; }
                }
                else if (!s_menu)
                {
                    Notice?.Invoke($"Online como {w.Tamer}.");
                    if (s_lastState != null) Send(s_lastState, false);
                }
                foreach (var (_, msg) in s_queue) Send(msg, true);
                s_queue.Clear();
                Connected?.Invoke();
                break;
            case SaveReply sr:
                Finish(sr.ReqId, sr);
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
            case ChatFrom c:
                ChatIn?.Invoke(c);
                break;
            case ChatNotice n:
                Session.SystemMessage(n.Text);
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
        if (!s_menu) Notice?.Invoke(why + (s_kicked ? "" : " Jogando sem os outros jogadores; o progresso grava quando a conexão voltar."));
        s_server = null;
        if (s_claiming) { s_claiming = false; s_wanted = null; }
        FailAll(why);
    }

    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Uma vez por quadro: recebe pacotes e manda o estado (no máximo 20 por segundo, e só o que mudou ou 1 por segundo).</summary>
    public static void Poll(PlayerState? me)
    {
        s_net?.PollEvents();
        if (s_pending.Count > 0)
        {
            double t = Now;
            foreach (var (id, p) in new List<KeyValuePair<int, (TaskCompletionSource<SaveReply>, double)>>(s_pending))
                if (t - p.Item2 > RequestTimeout) Finish(id, new SaveReply(id, false, "O servidor não respondeu. Confira a internet e tente de novo.", []));
        }
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
        FailAll("Conexão fechada.");
        s_net?.Stop();
        s_net = null;
        s_server = null;
        Remotes.Clear(); Group = null; Party = [];
        if (State != Status.Failed) State = Status.Off;
    }
}
