using System.Buffers.Binary;

namespace DW4.Formats.Audio;

/// <summary>
/// CRI ADX (formato público, documentado por terceiros): cabeçalho big-endian com 0x8000, offset do copyright em 0x02
/// (dados em offset + 4), tipo 3 (ADPCM padrão) em 0x04, bloco em 0x05, bits em 0x06, canais em 0x07, taxa em 0x08,
/// amostras em 0x0C, corte do filtro em 0x10 e versão em 0x12. Na versão 4, loop em 0x24 (ligado), 0x28 (início) e 0x30
/// (fim), em amostras (conferido nos 79 ADX do ndmw.afs em 2026-10-24).
/// </summary>
public sealed record AdxHeader(int DataOffset, int BlockSize, int Channels, int SampleRate, int Samples, int Cutoff, int Version,
    bool Loop, int LoopStart, int LoopEnd);

public static class Adx
{
    public static AdxHeader ReadHeader(ReadOnlySpan<byte> d)
    {
        if (d.Length < 0x14 || BinaryPrimitives.ReadUInt16BigEndian(d) != 0x8000) throw new InvalidDataException("não é ADX (0x8000)");
        int copyright = BinaryPrimitives.ReadUInt16BigEndian(d[2..]);
        if (d[4] != 3) throw new InvalidDataException($"ADX tipo {d[4]} não suportado (só o 3, ADPCM padrão)");
        if (d[6] != 4) throw new InvalidDataException($"ADX com {d[6]} bits por amostra");
        if (d[0x13] != 0) throw new InvalidDataException("ADX cifrado");
        int version = d[0x12];
        bool loop = false;
        int ls = 0, le = 0;
        if (version == 4 && copyright + 4 >= 0x34)
        {
            loop = BinaryPrimitives.ReadUInt32BigEndian(d[0x24..]) != 0;
            ls = (int)BinaryPrimitives.ReadUInt32BigEndian(d[0x28..]);
            le = (int)BinaryPrimitives.ReadUInt32BigEndian(d[0x30..]);
        }
        return new AdxHeader(copyright + 4, d[5], d[7], (int)BinaryPrimitives.ReadUInt32BigEndian(d[8..]),
            (int)BinaryPrimitives.ReadUInt32BigEndian(d[0x0C..]), BinaryPrimitives.ReadUInt16BigEndian(d[0x10..]), version, loop, ls, le);
    }

    /// <summary>Decodifica para PCM 16 bits intercalado por canal.</summary>
    public static short[] Decode(ReadOnlySpan<byte> d, out AdxHeader h)
    {
        h = ReadHeader(d);
        double z = Math.Cos(2 * Math.PI * h.Cutoff / h.SampleRate);
        double a = Math.Sqrt(2) - z, b = Math.Sqrt(2) - 1, c = (a - Math.Sqrt((a + b) * (a - b))) / b;
        int coef1 = (short)(c * 8192), coef2 = (short)(c * c * -4096);
        int perFrame = (h.BlockSize - 2) * 2;
        var pcm = new short[h.Samples * h.Channels];
        var hist1 = new int[h.Channels];
        var hist2 = new int[h.Channels];
        int frames = (h.Samples + perFrame - 1) / perFrame;
        for (int f = 0; f < frames; f++)
            for (int ch = 0; ch < h.Channels; ch++)
            {
                int at = h.DataOffset + (f * h.Channels + ch) * h.BlockSize;
                if (at + h.BlockSize > d.Length) return pcm;
                var frame = d.Slice(at, h.BlockSize);
                int scale = BinaryPrimitives.ReadUInt16BigEndian(frame) + 1;
                for (int i = 0; i < perFrame; i++)
                {
                    int s = f * perFrame + i;
                    if (s >= h.Samples) break;
                    int nib = (i & 1) == 0 ? frame[2 + i / 2] >> 4 : frame[2 + i / 2] & 0xF;
                    if (nib >= 8) nib -= 16;
                    int v = nib * scale + ((coef1 * hist1[ch] + coef2 * hist2[ch]) >> 12);
                    v = Math.Clamp(v, short.MinValue, short.MaxValue);
                    hist2[ch] = hist1[ch];
                    hist1[ch] = v;
                    pcm[s * h.Channels + ch] = (short)v;
                }
            }
        return pcm;
    }

    /// <summary>WAV PCM 16 bits; com loop, um chunk "smpl" (início e fim inclusivo em amostras), como o vgmstream grava.</summary>
    public static byte[] ToWav(short[] pcm, AdxHeader h)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = pcm.Length * 2;
        int smplBytes = h.Loop ? 60 : 0;
        w.Write("RIFF"u8);
        w.Write(4 + 8 + 16 + 8 + dataBytes + (h.Loop ? 8 + smplBytes : 0));
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)h.Channels);
        w.Write(h.SampleRate);
        w.Write(h.SampleRate * h.Channels * 2);
        w.Write((short)(h.Channels * 2));
        w.Write((short)16);
        if (h.Loop)
        {
            w.Write("smpl"u8);
            w.Write(smplBytes);
            w.Write(0); w.Write(0); w.Write(1_000_000_000 / h.SampleRate); w.Write(60); w.Write(0); w.Write(0); w.Write(0);
            w.Write(1); w.Write(0); // um loop, sem dados extras
            w.Write(0); w.Write(0); w.Write(h.LoopStart); w.Write(h.LoopEnd - 1); w.Write(0); w.Write(0);
        }
        w.Write("data"u8);
        w.Write(dataBytes);
        foreach (var s in pcm) w.Write(s);
        return ms.ToArray();
    }
}
