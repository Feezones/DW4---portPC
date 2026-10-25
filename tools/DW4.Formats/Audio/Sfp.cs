using System.Buffers.Binary;

namespace DW4.Formats.Audio;

/// <summary>Amostra DSP ADPCM de um banco .sfp (bloco de parâmetros do AX do GameCube, registro de 0x44 bytes).</summary>
public sealed record SfpSample(int Index, int DataOffset, int LoopAddress, int EndAddress, short[] Coefs, bool Loop, int Rate, int Size);

/// <summary>Cue de um banco: id 0xA9GGCC00 (GG = grupo, CC = cue), voz e amostra, volume (0x7F) e pan (0x80 = centro).</summary>
public sealed record SfpCue(int Group, int Cue, int Voice, int Sample, int Volume, int Pan);

/// <summary>
/// Banco de efeitos CRI SNDF "gcax" (.sfp), estrutura levantada em 2026-10-24 nos bancos e_nume, common_blst, dng00 e
/// allse (docs/gameplay/fase4-levantamento-audio.md). Inteiros big-endian, offsets absolutos. DTPK: +0C tamanho total,
/// +1C offset do PCMD; TBLD em 0x20 com 9 ponteiros em 0x58 ([3] cues, [4] vozes, [8] amostras); dados das amostras a
/// partir de PCMD + 0x20. Cue → voz → amostra (a ordem não é direta). Bytes ainda sem significado: "c0 df" e o último do cue.
/// </summary>
public sealed class Sfp
{
    public const string Magic = "gcaxDTPK";
    public IReadOnlyList<SfpSample> Samples { get; }
    public IReadOnlyList<SfpCue> Cues { get; }
    readonly byte[] _d;
    readonly int _pcmd;

    Sfp(byte[] d, int pcmd, List<SfpSample> samples, List<SfpCue> cues)
    {
        _d = d;
        _pcmd = pcmd;
        Samples = samples;
        Cues = cues;
    }

    /// <summary>Posições de bancos .sfp dentro de um arquivo (ex.: um IGB), pelo "gcaxDTPK" e o tamanho em +0C.</summary>
    public static IEnumerable<(int Offset, int Size)> Find(byte[] data)
    {
        var magic = System.Text.Encoding.ASCII.GetBytes(Magic);
        for (int from = 0; from + 0x20 <= data.Length;)
        {
            int i = data.AsSpan(from).IndexOf(magic);
            if (i < 0) yield break;
            int at = from + i;
            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at + 0x0C));
            if (size > 0 && at + size <= data.Length) { yield return (at, size); from = at + size; }
            else from = at + 1;
        }
    }

    public static Sfp Read(byte[] d)
    {
        uint U(int at) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
        ushort H(int at) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
        if (System.Text.Encoding.ASCII.GetString(d, 0, 8) != Magic) throw new InvalidDataException("não é gcaxDTPK");
        int pcmd = (int)U(0x1C);
        if (System.Text.Encoding.ASCII.GetString(d, pcmd, 8) != "gcaxPCMD") throw new InvalidDataException("PCMD fora do lugar");
        int cuesAt = (int)U(0x58 + 3 * 4), voicesAt = (int)U(0x58 + 4 * 4), samplesAt = (int)U(0x58 + 8 * 4);

        var samples = new List<SfpSample>();
        int n = (int)U(samplesAt) + 1;
        for (int i = 0; i < n; i++)
        {
            int r = samplesAt + 4 + i * 0x44;
            var coefs = new short[16];
            for (int k = 0; k < 16; k++) coefs[k] = (short)H(r + 0x0C + k * 2);
            // HIPÓTESE forte: bit 0x400 das flags = loop (0x200 sem loop, 0x600 com loop nos 4 bancos).
            samples.Add(new SfpSample(i, (int)U(r), (int)U(r + 4), (int)U(r + 8), coefs, (H(r + 0x3A) & 0x400) != 0, H(r + 0x3C), (int)U(r + 0x40)));
        }

        // Vozes: cabeçalho de 0x30, registros de 0x40 (+00 número da voz, +03 amostra).
        var voiceSample = new Dictionary<int, int>();
        int voices = d[voicesAt + 0x11] + 1;
        for (int v = 0; v < voices; v++)
        {
            int r = voicesAt + 0x30 + v * 0x40;
            voiceSample[d[r]] = d[r + 3];
        }

        // Cues: u8 nº de grupos − 1 (+3 bytes; allse = 3); por grupo {u16 0xA9GG, u16 offset}; no grupo, u8 maior cue + 3 bytes e u32 offsets
        // dos registros de 6 bytes "c0 df VV vol pan ?" (offsets relativos à tabela de cues).
        var cues = new List<SfpCue>();
        int groups = d[cuesAt] + 1;
        for (int g = 0; g < groups; g++)
        {
            int id = H(cuesAt + 4 + g * 4);
            int gAt = cuesAt + H(cuesAt + 6 + g * 4);
            int count = d[gAt] + 1;
            for (int c = 0; c < count; c++)
            {
                int r = cuesAt + (int)U(gAt + 4 + c * 4);
                int voice = d[r + 2];
                cues.Add(new SfpCue(id & 0xFF, c, voice, voiceSample.GetValueOrDefault(voice, -1), d[r + 3], d[r + 4]));
            }
        }
        return new Sfp(d, pcmd, samples, cues);
    }

    /// <summary>Endereço em nibbles (com os cabeçalhos de quadro) → índice da amostra PCM.</summary>
    public static int NibbleToSample(int address) => address / 16 * 14 + address % 16 - 2;

    /// <summary>DSP ADPCM do GameCube (formato público): quadros de 8 bytes, cabeçalho ps (preditor e escala) + 14 nibbles.</summary>
    public short[] Decode(SfpSample s, out int? loopStart, out int? loopEnd)
    {
        int count = NibbleToSample(s.EndAddress) + 1;
        loopStart = s.Loop ? NibbleToSample(s.LoopAddress) : null;
        loopEnd = s.Loop ? count : null;
        var pcm = new short[count];
        int at = _pcmd + s.DataOffset, h1 = 0, h2 = 0;
        for (int i = 0; i < count; i++)
        {
            int f = at + i / 14 * 8;
            if (f + 8 > _d.Length) break;
            int ps = _d[f], scale = 1 << (ps & 0xF), p = (ps >> 4) & 7;
            int k = i % 14, b = _d[f + 1 + k / 2];
            int nib = (k & 1) == 0 ? b >> 4 : b & 0xF;
            if (nib >= 8) nib -= 16;
            int v = (((nib * scale) << 11) + 1024 + s.Coefs[p * 2] * h1 + s.Coefs[p * 2 + 1] * h2) >> 11;
            v = Math.Clamp(v, short.MinValue, short.MaxValue);
            h2 = h1;
            h1 = v;
            pcm[i] = (short)v;
        }
        return pcm;
    }
}
