namespace DW4.Formats.Sofdec;

/// <summary>Resumo de um fluxo elementar encontrado no programa MPEG.</summary>
public sealed class MpegPsStream
{
    public int StreamId { get; init; }
    public int Packets { get; set; }
    public MemoryStream Payload { get; } = new();
}

/// <summary>
/// Separador mínimo de MPEG Program Stream (ISO/IEC 11172-1 e 13818-1), usado para os vídeos Sofdec (.sfd) do DW4:
/// percorre pacotes (0xBA), cabeçalho de sistema (0xBB) e PES, e junta o conteúdo de cada fluxo. Só lê.
/// </summary>
public static class MpegPs
{
    public static (Dictionary<int, MpegPsStream> Streams, int Packs, List<string> Problems) Demux(ReadOnlySpan<byte> d)
    {
        var streams = new Dictionary<int, MpegPsStream>();
        var problems = new List<string>();
        int pos = 0, packs = 0;
        while (pos + 4 <= d.Length)
        {
            if (d[pos] != 0 || d[pos + 1] != 0 || d[pos + 2] != 1)
            {
                problems.Add($"código de início esperado em 0x{pos:X}");
                break;
            }
            int code = d[pos + 3];
            if (code == 0xB9) { pos += 4; break; } // fim do programa
            if (code == 0xBA)
            {
                packs++;
                if (pos + 5 > d.Length) break;
                if ((d[pos + 4] & 0xF0) == 0x20) pos += 12; // MPEG-1
                else if ((d[pos + 4] & 0xC0) == 0x40)
                {
                    if (pos + 14 > d.Length) break;
                    pos += 14 + (d[pos + 13] & 7); // MPEG-2 com enchimento
                }
                else { problems.Add($"pacote de formato desconhecido em 0x{pos:X}"); break; }
                continue;
            }
            if (code < 0xBB || pos + 6 > d.Length) { problems.Add($"código 0x{code:X2} inesperado em 0x{pos:X}"); break; }
            int length = (d[pos + 4] << 8) | d[pos + 5];
            int start = pos + 6, end = start + length;
            if (end > d.Length) { problems.Add($"PES 0x{code:X2} em 0x{pos:X} passa do fim"); break; }
            pos = end;
            if (code is 0xBB or 0xBE or 0xBF) continue; // cabeçalho de sistema, enchimento, privado 2

            int h = start;
            if (h < end && (d[h] & 0xC0) == 0x80)
            {
                // cabeçalho PES do MPEG-2
                if (h + 3 > end) continue;
                h += 3 + d[h + 2];
            }
            else
            {
                // cabeçalho PES do MPEG-1: enchimento 0xFF, buffer STD opcional, PTS/DTS
                while (h < end && d[h] == 0xFF) h++;
                if (h < end && (d[h] & 0xC0) == 0x40) h += 2;
                if (h < end && (d[h] & 0xF0) == 0x20) h += 5;
                else if (h < end && (d[h] & 0xF0) == 0x30) h += 10;
                else if (h < end && d[h] == 0x0F) h += 1;
            }
            if (h > end) { problems.Add($"cabeçalho PES maior que o pacote em 0x{start - 6:X}"); continue; }
            if (!streams.TryGetValue(code, out var s)) streams[code] = s = new MpegPsStream { StreamId = code };
            s.Packets++;
            s.Payload.Write(d[h..end]);
        }
        // Depois do código de fim (00 00 01 B9) o arquivo é completado até o fim do setor de 2048 B; isso não é erro.
        if (pos < d.Length && (pos & 0x7FF) != 0 && d.Length - pos < 0x800 && (d.Length & 0x7FF) == 0) pos = d.Length;
        if (pos < d.Length) problems.Add($"{d.Length - pos} bytes depois do fim do programa");
        return (streams, packs, problems);
    }

    /// <summary>Lê o primeiro cabeçalho de sequência MPEG-1/2 (00 00 01 B3): largura, altura e código de quadros por segundo.</summary>
    public static (int Width, int Height, int FrameRateCode)? SequenceHeader(ReadOnlySpan<byte> es)
    {
        for (int i = 0; i + 8 <= es.Length; i++)
            if (es[i] == 0 && es[i + 1] == 0 && es[i + 2] == 1 && es[i + 3] == 0xB3)
                return ((es[i + 4] << 4) | (es[i + 5] >> 4), ((es[i + 5] & 0xF) << 8) | es[i + 6], es[i + 7] & 0xF);
        return null;
    }
}
