using System.Text;

namespace DW4.Net;

/// <summary>
/// Combate em grupo (etapa 6d, ADR 0003; DECISÃO DO RESPONSÁVEL em 2026-11-03: só dentro da party). Quem é o anfitrião do grupo
/// (<see cref="CoopGroup.HostId"/>) roda os inimigos como no jogo sozinho; os outros mostram cópias guiadas por estes pacotes e
/// mandam os próprios golpes. O servidor só repassa (<see cref="CoopRelay"/>), sem abrir o conteúdo.
/// </summary>
public enum CoopType : byte
{
    EnemySpawn = 1,
    EnemySync = 2,
    EnemySeq = 3,
    EnemyHit = 4,
    EnemyKilled = 5,
    EnemyRemoved = 6,
    Wipeout = 7,
}

/// <summary>Anfitrião → grupo: inimigo novo (linha da tabela enemy, posição, giro, HP e o gerador que o gerou, "" = nenhum).</summary>
public sealed record EnemySpawn(int NetId, int Row, float X, float Y, float Z, float Yaw, int Hp, string Gen = "");

/// <summary>
/// Posição, giro e HP de um inimigo (vários num <see cref="EnemySync"/>), com a linha da tabela enemy: quem não tem a cópia
/// (perdeu o EnemySpawn, chegou depois) cria pela sincronia.
/// </summary>
public readonly record struct EnemyPose(int NetId, float X, float Y, float Z, float Yaw, int Hp, int Row = 0);

/// <summary>
/// Anfitrião → grupo, ~10 por segundo: onde estão TODOS os inimigos vivos dele (sem garantia de entrega; o próximo corrige).
/// É a lista que vale: cópia que não aparece por um tempo some, inimigo que falta é criado.
/// </summary>
public sealed record EnemySync(EnemyPose[] Poses);

/// <summary>Anfitrião → grupo: o inimigo começou uma sequência do jogo (SQ_ATCK1, SQ_SDMG…), com o bloco (estilo).</summary>
public sealed record EnemySeq(int NetId, string Sequence, string Style);

/// <summary>Membro → anfitrião: o golpe do membro acertou (dano já calculado com os status dele) e a reação do inimigo.</summary>
public sealed record EnemyHit(int NetId, int Damage, string Reaction);

/// <summary>
/// Anfitrião → grupo: o inimigo morreu; <see cref="KillerId"/> leva a parte de quem matou (P2EEXPUP). <see cref="Drop"/>: cada um
/// sorteia o próprio drop (false quando o script de acerto do chefe deu o EXP sem drop).
/// </summary>
public sealed record EnemyKilled(int NetId, int KillerId, bool Drop = true);

/// <summary>Anfitrião → grupo: o corpo sumiu (fim da sequência de morte, ou STOPREVERS).</summary>
public sealed record EnemyRemoved(int NetId);

/// <summary>Anfitrião → grupo: a onda do gerador foi vencida; cada um roda o _callLua dele (portas, próxima onda).</summary>
public sealed record Wipeout(string Generator);

public static class CoopCodec
{
    public static byte[] Write(object msg)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8);
        switch (msg)
        {
            case EnemySpawn x: w.Write((byte)CoopType.EnemySpawn); w.Write(x.NetId); w.Write(x.Row); w.Write(x.X); w.Write(x.Y); w.Write(x.Z); w.Write(x.Yaw); w.Write(x.Hp); Str(w, x.Gen); break;
            case EnemySync x:
                w.Write((byte)CoopType.EnemySync); w.Write((byte)Math.Min(x.Poses.Length, MaxPoses));
                foreach (var p in x.Poses.Take(MaxPoses)) { w.Write(p.NetId); w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(p.Yaw); w.Write(p.Hp); w.Write(p.Row); }
                break;
            case EnemySeq x: w.Write((byte)CoopType.EnemySeq); w.Write(x.NetId); Str(w, x.Sequence); Str(w, x.Style); break;
            case EnemyHit x: w.Write((byte)CoopType.EnemyHit); w.Write(x.NetId); w.Write(x.Damage); Str(w, x.Reaction); break;
            case EnemyKilled x: w.Write((byte)CoopType.EnemyKilled); w.Write(x.NetId); w.Write(x.KillerId); w.Write(x.Drop); break;
            case EnemyRemoved x: w.Write((byte)CoopType.EnemyRemoved); w.Write(x.NetId); break;
            case Wipeout x: w.Write((byte)CoopType.Wipeout); Str(w, x.Generator); break;
            default: throw new ArgumentException("mensagem do grupo desconhecida: " + msg.GetType().Name);
        }
        return ms.ToArray();
    }

    /// <summary>Poses por <see cref="EnemySync"/> (28 bytes cada; 36 cabem com folga em <see cref="Protocol.MaxPayload"/>).</summary>
    public const int MaxPoses = 36;

    public static object Read(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            var type = (CoopType)r.ReadByte();
            object msg = type switch
            {
                CoopType.EnemySpawn => new EnemySpawn(r.ReadInt32(), r.ReadInt32(), F(r), F(r), F(r), F(r), r.ReadInt32(), Str(r)),
                CoopType.EnemySync => ReadSync(r),
                CoopType.EnemySeq => new EnemySeq(r.ReadInt32(), Str(r), Str(r)),
                CoopType.EnemyHit => new EnemyHit(r.ReadInt32(), r.ReadInt32(), Str(r)),
                CoopType.EnemyKilled => new EnemyKilled(r.ReadInt32(), r.ReadInt32(), r.ReadBoolean()),
                CoopType.EnemyRemoved => new EnemyRemoved(r.ReadInt32()),
                CoopType.Wipeout => new Wipeout(Str(r)),
                _ => throw new FormatException($"tipo do grupo {(byte)type}"),
            };
            if (r.BaseStream.Position != data.Length) throw new FormatException("sobra no pacote do grupo");
            return msg;
        }
        catch (EndOfStreamException) { throw new FormatException("pacote do grupo curto"); }
    }

    static EnemySync ReadSync(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > MaxPoses) throw new FormatException("poses demais");
        var poses = new EnemyPose[n];
        for (int i = 0; i < n; i++) poses[i] = new EnemyPose(r.ReadInt32(), F(r), F(r), F(r), F(r), r.ReadInt32(), r.ReadInt32());
        return new EnemySync(poses);
    }

    static float F(BinaryReader r)
    {
        float f = r.ReadSingle();
        if (!float.IsFinite(f)) throw new FormatException("número inválido");
        return f;
    }

    static void Str(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s.Length > 32 ? s[..32] : s);
        w.Write((byte)b.Length);
        w.Write(b);
    }

    static string Str(BinaryReader r)
    {
        int n = r.ReadByte();
        if (n > 128) throw new FormatException("texto longo demais");
        var b = r.ReadBytes(n);
        if (b.Length != n) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(b);
    }
}
