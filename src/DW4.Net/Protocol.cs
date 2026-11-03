using System.Text;

namespace DW4.Net;

/// <summary>
/// Protocolo do online do DW4 (ADR 0003). Cada pacote começa com um byte de <see cref="MsgType"/>; números em little-endian,
/// textos em UTF-8 com tamanho (ushort). Sem rede aqui: o servidor e o cliente passam os bytes pela LiteNetLib.
/// </summary>
public static class Protocol
{
    /// <summary>Muda quando o formato muda; cliente e servidor de versões diferentes não conversam.</summary>
    public const int Version = 3; // 2 (0.6.1): arma, menu do F; 3 (0.6.2): combate em grupo (CoopGroup, CoopRelay), HP/MP e PartyRoster
    /// <summary>Chave de conexão da LiteNetLib (recusa quem não é o DW4 antes de qualquer pacote).</summary>
    public const string ConnectionKey = "DW4-online-1";
    public const int DefaultPort = 7031;
    /// <summary>Estados de posição por segundo que o cliente manda.</summary>
    public const int StateRate = 20;
    public const int MaxText = 64;
    /// <summary>Membros de um grupo de combate (a party do original tem até 4).</summary>
    public const int MaxGroup = 4;
    /// <summary>Tamanho máximo de um pacote do combate em grupo (cabe num datagrama sem fragmentar).</summary>
    public const int MaxPayload = 1100;
}

public enum MsgType : byte
{
    // cliente → servidor
    Hello = 1,
    State = 2,
    Interact = 3,
    CoopRelay = 4,
    // servidor → cliente
    Welcome = 10,
    Reject = 11,
    Spawn = 12,
    Despawn = 13,
    RemoteState = 14,
    InteractResult = 15,
    SocialPrompt = 16,
    CoopGroup = 17,
    CoopFrom = 18,
    PartyRoster = 19,
}

/// <summary>Cliente → servidor, ao conectar: ticket do launcher e o personagem da vaga.</summary>
public sealed record Hello(int Version, string Ticket, string Character, string Model, int Level);

/// <summary>Estado do jogador: fase, posição, direção, animação. Também anuncia troca de fase, forma e nível.</summary>
public sealed record PlayerState(string Stage, float X, float Y, float Z, float Facing, string Anim, string Model, int Level, string Weapon = "",
    int Hp = 0, int MaxHp = 0, int Mp = 0, int MaxMp = 0);

/// <summary>Um membro da party online no DW4 (em qualquer fase): para o quadro do grupo no canto esquerdo da tela.</summary>
public sealed record PartyEntry(int PlayerId, string Character, string Model, int Level, int Hp, int MaxHp, int Mp, int MaxMp, string Stage);

/// <summary>
/// Servidor → membros da party: quem da party está no DW4 agora, com nível, HP e MP (vem quando algo disso muda; vazio = sem party).
/// </summary>
public sealed record PartyRoster(PartyEntry[] Members);

public sealed record Welcome(int PlayerId, string Tamer);
public sealed record Reject(string Reason);
/// <summary>Outro jogador apareceu na mesma instância da fase.</summary>
public sealed record Spawn(int PlayerId, string Character, string Tamer, PlayerState State);
public sealed record Despawn(int PlayerId);
public sealed record RemoteState(int PlayerId, PlayerState State);

/// <summary>
/// Menu do F (cliente → servidor): <see cref="Action"/> sobre o jogador <see cref="TargetId"/>. Ações: relation (montar o menu),
/// friend-request, party-invite; e as respostas a um <see cref="SocialPrompt"/>: friend-accept, friend-decline, party-accept,
/// party-decline (o alvo é quem perguntou; <see cref="InviteId"/> no convite de grupo).
/// </summary>
public sealed record Interact(int TargetId, string Action, string InviteId = "");

/// <summary>
/// Resposta do servidor a um <see cref="Interact"/>: deu certo ou o aviso (Message, os textos da DMO.API). Em "relation", o que
/// o menu precisa: Relation (none | friend | requestSent | requestReceived | blockedByYou | self) e a party.
/// </summary>
public sealed record InteractResult(int TargetId, string Action, bool Ok, string Message, string Relation = "", bool HasParty = false,
    bool AmLeader = false, bool TargetInMyParty = false, int PartyOccupied = 0, bool TargetInOtherParty = false);

/// <summary>Pergunta para quem recebeu um pedido de amizade (Kind "friend") ou um convite de grupo (Kind "party").</summary>
public sealed record SocialPrompt(string Kind, int FromId, string FromTamer, string InviteId = "");

/// <summary>
/// Grupo de combate (inimigos, dano e drops compartilhados: a party na mesma fase). <see cref="HostId"/> roda os inimigos; os
/// outros mostram cópias. Vem a cada mudança (alguém entrou, saiu, trocou de fase ou de party). Sozinho: Members = [você].
/// </summary>
public sealed record CoopGroup(int HostId, int[] Members);

/// <summary>Cliente → servidor: pacote do combate em grupo (<see cref="CoopCodec"/>) para os outros membros do grupo.</summary>
public sealed record CoopRelay(bool Reliable, byte[] Payload);

/// <summary>Servidor → cliente: pacote do combate em grupo vindo de <see cref="FromId"/>.</summary>
public sealed record CoopFrom(int FromId, byte[] Payload);

/// <summary>Grava e lê os pacotes. Erro de formato vira <see cref="FormatException"/> (o servidor derruba quem mandou).</summary>
public static class Codec
{
    public static byte[] Write(object msg)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        switch (msg)
        {
            case Hello h: w.Write((byte)MsgType.Hello); w.Write(h.Version); Str(w, h.Ticket, 128); Str(w, h.Character); Str(w, h.Model); w.Write(h.Level); break;
            case PlayerState s: w.Write((byte)MsgType.State); State(w, s); break;
            case Welcome x: w.Write((byte)MsgType.Welcome); w.Write(x.PlayerId); Str(w, x.Tamer); break;
            case Reject x: w.Write((byte)MsgType.Reject); Str(w, x.Reason, 200); break;
            case Spawn x: w.Write((byte)MsgType.Spawn); w.Write(x.PlayerId); Str(w, x.Character); Str(w, x.Tamer); State(w, x.State); break;
            case Despawn x: w.Write((byte)MsgType.Despawn); w.Write(x.PlayerId); break;
            case Interact x: w.Write((byte)MsgType.Interact); w.Write(x.TargetId); Str(w, x.Action, 32); Str(w, x.InviteId, 64); break;
            case InteractResult x:
                w.Write((byte)MsgType.InteractResult); w.Write(x.TargetId); Str(w, x.Action, 32); w.Write(x.Ok); Str(w, x.Message, 200);
                Str(w, x.Relation, 32); w.Write(x.HasParty); w.Write(x.AmLeader); w.Write(x.TargetInMyParty); w.Write(x.PartyOccupied);
                w.Write(x.TargetInOtherParty);
                break;
            case CoopGroup x:
                w.Write((byte)MsgType.CoopGroup); w.Write(x.HostId); w.Write((byte)Math.Min(x.Members.Length, Protocol.MaxGroup));
                foreach (var m in x.Members.Take(Protocol.MaxGroup)) w.Write(m);
                break;
            case CoopRelay x: w.Write((byte)MsgType.CoopRelay); w.Write(x.Reliable); Bytes(w, x.Payload); break;
            case CoopFrom x: w.Write((byte)MsgType.CoopFrom); w.Write(x.FromId); Bytes(w, x.Payload); break;
            case PartyRoster x:
                w.Write((byte)MsgType.PartyRoster); w.Write((byte)Math.Min(x.Members.Length, Protocol.MaxGroup));
                foreach (var m in x.Members.Take(Protocol.MaxGroup))
                {
                    w.Write(m.PlayerId); Str(w, m.Character); Str(w, m.Model); w.Write(m.Level);
                    w.Write(m.Hp); w.Write(m.MaxHp); w.Write(m.Mp); w.Write(m.MaxMp); Str(w, m.Stage);
                }
                break;
            case SocialPrompt x: w.Write((byte)MsgType.SocialPrompt); Str(w, x.Kind, 16); w.Write(x.FromId); Str(w, x.FromTamer); Str(w, x.InviteId, 64); break;
            case RemoteState x: w.Write((byte)MsgType.RemoteState); w.Write(x.PlayerId); State(w, x.State); break;
            default: throw new ArgumentException("mensagem desconhecida: " + msg.GetType().Name);
        }
        return ms.ToArray();
    }

    public static object Read(ReadOnlySpan<byte> data) => Read(data.ToArray());

    public static object Read(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            var type = (MsgType)r.ReadByte();
            object msg = type switch
            {
                MsgType.Hello => new Hello(r.ReadInt32(), Str(r, 128), Str(r), Str(r), r.ReadInt32()),
                MsgType.State => State(r),
                MsgType.Welcome => new Welcome(r.ReadInt32(), Str(r)),
                MsgType.Reject => new Reject(Str(r, 200)),
                MsgType.Spawn => new Spawn(r.ReadInt32(), Str(r), Str(r), State(r)),
                MsgType.Despawn => new Despawn(r.ReadInt32()),
                MsgType.Interact => new Interact(r.ReadInt32(), Str(r, 32), Str(r, 64)),
                MsgType.InteractResult => new InteractResult(r.ReadInt32(), Str(r, 32), r.ReadBoolean(), Str(r, 200), Str(r, 32), r.ReadBoolean(),
                    r.ReadBoolean(), r.ReadBoolean(), r.ReadInt32(), r.ReadBoolean()),
                MsgType.CoopGroup => ReadGroup(r),
                MsgType.CoopRelay => new CoopRelay(r.ReadBoolean(), Bytes(r)),
                MsgType.CoopFrom => new CoopFrom(r.ReadInt32(), Bytes(r)),
                MsgType.PartyRoster => ReadRoster(r),
                MsgType.SocialPrompt => new SocialPrompt(Str(r, 16), r.ReadInt32(), Str(r), Str(r, 64)),
                MsgType.RemoteState => new RemoteState(r.ReadInt32(), State(r)),
                _ => throw new FormatException($"tipo {(byte)type}"),
            };
            if (r.BaseStream.Position != data.Length) throw new FormatException("sobra no pacote");
            return msg;
        }
        catch (EndOfStreamException) { throw new FormatException("pacote curto"); }
    }

    static CoopGroup ReadGroup(BinaryReader r)
    {
        int host = r.ReadInt32(), n = r.ReadByte();
        if (n > Protocol.MaxGroup) throw new FormatException("grupo grande demais");
        var m = new int[n];
        for (int i = 0; i < n; i++) m[i] = r.ReadInt32();
        return new CoopGroup(host, m);
    }

    static PartyRoster ReadRoster(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > Protocol.MaxGroup) throw new FormatException("party grande demais");
        var m = new PartyEntry[n];
        for (int i = 0; i < n; i++)
            m[i] = new PartyEntry(r.ReadInt32(), Str(r), Str(r), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), Str(r));
        return new PartyRoster(m);
    }

    static void Bytes(BinaryWriter w, byte[] b)
    {
        if (b.Length > Protocol.MaxPayload) throw new ArgumentException("pacote do grupo grande demais");
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    static byte[] Bytes(BinaryReader r)
    {
        int n = r.ReadUInt16();
        if (n > Protocol.MaxPayload) throw new FormatException("pacote do grupo grande demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return b;
    }

    static void State(BinaryWriter w, PlayerState s)
    {
        Str(w, s.Stage); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write(s.Facing); Str(w, s.Anim); Str(w, s.Model); w.Write(s.Level); Str(w, s.Weapon);
        w.Write(s.Hp); w.Write(s.MaxHp); w.Write(s.Mp); w.Write(s.MaxMp);
    }

    static PlayerState State(BinaryReader r)
    {
        var s = new PlayerState(Str(r), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), Str(r), Str(r), r.ReadInt32(), Str(r),
            r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32());
        if (!float.IsFinite(s.X) || !float.IsFinite(s.Y) || !float.IsFinite(s.Z) || !float.IsFinite(s.Facing)) throw new FormatException("número inválido");
        return s;
    }

    static void Str(BinaryWriter w, string s, int max = Protocol.MaxText)
    {
        var b = Encoding.UTF8.GetBytes(s.Length > max ? s[..max] : s);
        w.Write((ushort)b.Length);
        w.Write(b);
    }

    static string Str(BinaryReader r, int max = Protocol.MaxText)
    {
        int n = r.ReadUInt16();
        if (n > max * 4) throw new FormatException("texto grande demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(b);
    }
}
