using System.Buffers.Binary;

namespace DW4.Formats.Analysis;

public enum ContentKind { Unknown, Igb, Adx, MpegPs }

/// <summary>
/// Classificação pelo cabeçalho, com as regras observadas nas amostras de 2026-10-24
/// (docs/formatos/afs-conteudo.md). Não valida o conteúdo; só diz qual leitor tentar.
/// </summary>
public static class Classifier
{
    /// <summary>u32 LE em 0x28 de todo IGB visto até agora (ndmw.igb e entradas dng1, Load, BecT).</summary>
    public const uint IgbMagic = 0x0000FADA;

    public static ContentKind Classify(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 4 && head[0] == 0 && head[1] == 0 && head[2] == 1 && head[3] == 0xBA)
            return ContentKind.MpegPs;

        if (head.Length >= 0x2C && BinaryPrimitives.ReadUInt32LittleEndian(head[0x28..]) == IgbMagic)
            return ContentKind.Igb;

        // ADX: 0x8000, u16 BE = deslocamento dos dados - 4, tipo 3; "(c)CRI" logo antes dos dados.
        if (head.Length >= 8 && head[0] == 0x80 && head[1] == 0x00 && head[4] == 0x03)
        {
            int copyright = BinaryPrimitives.ReadUInt16BigEndian(head[2..]) - 2;
            if (copyright >= 0 && copyright + 6 <= head.Length && head.Slice(copyright, 6).SequenceEqual("(c)CRI"u8))
                return ContentKind.Adx;
        }

        return ContentKind.Unknown;
    }
}
