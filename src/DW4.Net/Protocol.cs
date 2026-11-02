using System.Text;

namespace DW4.Net;

/// <summary>
/// Protocolo do online do DW4 (ADR 0003). Cada pacote começa com um byte de <see cref="MsgType"/>; números em little-endian,
/// textos em UTF-8 com tamanho (ushort). Sem rede aqui: o servidor e o cliente passam os bytes pela LiteNetLib.
/// </summary>
public static class Protocol
{
    /// <summary>Muda quando o formato muda; cliente e servidor de versões diferentes não conversam.</summary>
    public const int Version = 1;
    /// <summary>Chave de conexão da LiteNetLib (recusa quem não é o DW4 antes de qualquer pacote).</summary>
    public const string ConnectionKey = "DW4-online-1";
    public const int DefaultPort = 7031;
    /// <summary>Estados de posição por segundo que o cliente manda.</summary>
    public const int StateRate = 20;
    public const int MaxText = 64;
}

public enum MsgType : byte
{
    // cliente → servidor
    Hello = 1,
    State = 2,
    // servidor → cliente
    Welcome = 10,
    Reject = 11,
    Spawn = 12,
    Despawn = 13,
    RemoteState = 14,
}

/// <summary>Cliente → servidor, ao conectar: ticket do launcher e o personagem da vaga.</summary>
public sealed record Hello(int Version, string Ticket, string Character, string Model, int Level);

/// <summary>Estado do jogador: fase, posição, direção, animação. Também anuncia troca de fase, forma e nível.</summary>
public sealed record PlayerState(string Stage, float X, float Y, float Z, float Facing, string Anim, string Model, int Level);

public sealed record Welcome(int PlayerId, string Tamer);
public sealed record Reject(string Reason);
/// <summary>Outro jogador apareceu na mesma instância da fase.</summary>
public sealed record Spawn(int PlayerId, string Character, string Tamer, PlayerState State);
public sealed record Despawn(int PlayerId);
public sealed record RemoteState(int PlayerId, PlayerState State);

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
                MsgType.RemoteState => new RemoteState(r.ReadInt32(), State(r)),
                _ => throw new FormatException($"tipo {(byte)type}"),
            };
            if (r.BaseStream.Position != data.Length) throw new FormatException("sobra no pacote");
            return msg;
        }
        catch (EndOfStreamException) { throw new FormatException("pacote curto"); }
    }

    static void State(BinaryWriter w, PlayerState s)
    {
        Str(w, s.Stage); w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write(s.Facing); Str(w, s.Anim); Str(w, s.Model); w.Write(s.Level);
    }

    static PlayerState State(BinaryReader r)
    {
        var s = new PlayerState(Str(r), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), Str(r), Str(r), r.ReadInt32());
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
