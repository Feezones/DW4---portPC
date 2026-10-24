using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Pose de um osso numa amostra: rotação (quatérnio x, y, z, w) e translação.</summary>
public readonly record struct EnbayaPose(float Qx, float Qy, float Qz, float Qw, float Tx, float Ty, float Tz);

/// <summary>Animação Enbaya decodificada: uma pose por trilha (osso) em cada amostra.</summary>
public sealed class EnbayaClip
{
    public int TrackCount { get; init; }
    public float QuantizationError { get; init; }
    public float Duration { get; init; }
    public int SampleRate { get; init; }
    /// <summary>Tempo de cada amostra, em segundos (a última é limitada à duração).</summary>
    public List<float> Times { get; } = [];
    /// <summary>Poses[amostra][trilha].</summary>
    public List<EnbayaPose[]> Poses { get; } = [];
    /// <summary>Quanto de cada subfluxo foi lido, para conferir que a decodificação consumiu tudo.</summary>
    public List<string> Consumption { get; } = [];
    public bool ConsumedExactly { get; set; } = true;
    public int TotalLength { get; init; }
}

/// <summary>
/// Decodificador do fluxo comprimido Enbaya (igEnbayaAnimationSource) do Alchemy, só no sentido direto, gerando a pose
/// de cada amostra sem interpolar. Portado do enbrip de korenkonder (MIT, https://github.com/korenkonder/enbrip,
/// src/enbaya.c); aviso de licença em THIRD_PARTY_NOTICES.md. Cabeçalho de 0x50 bytes: assinatura, nº de trilhas,
/// erro de quantização, duração, taxa de amostragem e 14 comprimentos de subfluxos; os subfluxos vêm agrupados por
/// largura (32, 16 e 8/2/4 bits).
/// </summary>
public static class Enbaya
{
    public const int HeaderSize = 0x50;

    static readonly int[] Shift2 = [6, 4, 2, 0];
    static readonly int[] Shift4 = [4, 0];
    static readonly int[] Value2 = [0, 1, 0, -1];
    static readonly int[] Value4 = [0, 8, 2, 3, 4, 5, 6, 7, -8, -7, -6, -5, -4, -3, -2, -9];

    public static EnbayaClip Decode(ReadOnlySpan<byte> s, bool littleEndian = true)
    {
        if (s.Length < HeaderSize) throw new InvalidDataException("fluxo Enbaya menor que o cabeçalho");
        var data = s.ToArray();
        uint U(int at) => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        float F(int at) => BitConverter.Int32BitsToSingle((int)U(at));

        int trackCount = (int)U(0x04);
        float q = F(0x08), duration = F(0x0C);
        int sampleRate = (int)U(0x10);
        // comprimentos, na ordem do cabeçalho
        int initI2 = (int)U(0x14), initI8 = (int)U(0x18), initI16 = (int)U(0x1C), initI32 = (int)U(0x20);
        int trI2 = (int)U(0x24), trI4 = (int)U(0x28), trI8 = (int)U(0x2C), trI16 = (int)U(0x30), trI32 = (int)U(0x34);
        int stU2 = (int)U(0x38), stU8 = (int)U(0x3C), stU16 = (int)U(0x40), stU32 = (int)U(0x44), flagsLen = (int)U(0x48);
        long total = (long)HeaderSize + initI2 + initI8 + initI16 + initI32 + trI2 + trI4 + trI8 + trI16 + trI32
            + stU2 + stU8 + stU16 + stU32 + flagsLen;
        if (trackCount <= 0 || trackCount > 1024 || sampleRate <= 0 || sampleRate > 1000 || !(duration >= 0) || duration > 3600)
            throw new InvalidDataException($"cabeçalho Enbaya implausível: {trackCount} trilhas, {sampleRate} Hz, {duration} s");
        if (total > data.Length) throw new InvalidDataException($"subfluxos somam {total} B, o bloco tem {data.Length} B");
        if (flagsLen < trackCount) throw new InvalidDataException($"{flagsLen} bytes de flags para {trackCount} trilhas");

        // posições dos subfluxos (mesma ordem de enb_anim_stream_get_*)
        int pInitI32 = HeaderSize, pTrI32 = pInitI32 + initI32, pStU32 = pTrI32 + trI32;
        int pInitI16 = pStU32 + stU32, pTrI16 = pInitI16 + initI16, pStU16 = pTrI16 + trI16;
        int pInitI2 = pStU16 + stU16, pInitI8 = pInitI2 + initI2, pTrI2 = pInitI8 + initI8, pTrI4 = pTrI2 + trI2;
        int pTrI8 = pTrI4 + trI4, pStU2 = pTrI8 + trI8, pStU8 = pStU2 + stU2, pFlags = pStU8 + stU8;

        int I16(int at) => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(at));
        int I32(int at) => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at));
        int U16(int at) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at));

        // cursores
        int ii2 = pInitI2, ii2c = 0, ii8 = pInitI8, ii16 = pInitI16, ii32 = pInitI32;
        int ti2 = pTrI2, ti2c = 0, ti4 = pTrI4, ti4c = 0, ti8 = pTrI8, ti16 = pTrI16, ti32 = pTrI32;
        int su2 = pStU2, su2c = 0, su8 = pStU8, su16 = pStU16, su32 = pStU32;

        int InitValue()
        {
            if (ii2c == 4) { ii2c = 0; ii2++; }
            int tag = (data[ii2] >> Shift2[ii2c++]) & 3;
            switch (tag)
            {
                case 1: return (sbyte)data[ii8++];
                case 2: { int v = I16(ii16); ii16 += 2; return v; }
                case 3: { int v = I32(ii32); ii32 += 4; return v; }
                default: return 0;
            }
        }

        int DeltaValue()
        {
            if (ti2c == 4) { ti2c = 0; ti2++; }
            int tag = (data[ti2] >> Shift2[ti2c++]) & 3;
            if (tag != 2) return Value2[tag];
            if (ti4c == 2) { ti4c = 0; ti4++; }
            int code = (data[ti4] >> Shift4[ti4c++]) & 0xF;
            if (code != 0) return Value4[code];
            int v = (sbyte)data[ti8++];
            if (v == 0)
            {
                v = I16(ti16); ti16 += 2;
                if (v == 0) { v = I32(ti32); ti32 += 4; }
            }
            else if (v is > 0 and < 9) v += 0x7F;
            else if (v is > -9 and < 0) v -= 0x80;
            return v;
        }

        uint StateValue()
        {
            if (su2c == 4) { su2c = 0; su2++; }
            int tag = (data[su2] >> Shift2[su2c++]) & 3;
            switch (tag)
            {
                case 1: return data[su8++];
                case 2: { uint v = (uint)U16(su16); su16 += 2; return v; }
                case 3: { uint v = (uint)I32(su32); su32 += 4; return v; }
                default: return 0;
            }
        }

        var clip = new EnbayaClip { TrackCount = trackCount, QuantizationError = q, Duration = duration, SampleRate = sampleRate, TotalLength = (int)total };
        var accum = new float[trackCount, 7];
        var flags = new byte[trackCount];
        var pose = new EnbayaPose[trackCount];

        // amostra 0: 7 valores iniciais por trilha × erro de quantização, quatérnio normalizado
        long nextStep = StateValue();
        for (int t = 0; t < trackCount; t++)
        {
            var v = new float[7];
            for (int c = 0; c < 7; c++) v[c] = InitValue() * q;
            pose[t] = Normalize(v);
            flags[t] = data[pFlags + t];
        }
        clip.Times.Add(0);
        clip.Poses.Add((EnbayaPose[])pose.Clone());

        float sps = 1f / sampleRate;
        int sample = 0;
        float sampleTime = 0;
        while (duration - sampleTime > 0.00001f)
        {
            if (sample > 0)
            {
                // liga e desliga componentes conforme as distâncias em StateValue (enb_state_step_forward)
                int comps = trackCount * 7;
                for (int i = 0; i < comps;)
                {
                    if (nextStep == 0)
                    {
                        flags[i / 7] ^= (byte)(1 << (i % 7));
                        nextStep = StateValue();
                        i++;
                    }
                    else
                    {
                        long step = Math.Min(nextStep, comps - i);
                        i += (int)step;
                        nextStep -= step;
                    }
                }
            }
            for (int t = 0; t < trackCount; t++)
                for (int c = 0; c < 7; c++)
                    if ((flags[t] & (1 << c)) != 0) accum[t, c] += DeltaValue();
            sample++;
            float time = Math.Min(sample * sps, duration);
            for (int t = 0; t < trackCount; t++)
            {
                var p = pose[t];
                pose[t] = Normalize([
                    accum[t, 0] * q + p.Qx, accum[t, 1] * q + p.Qy, accum[t, 2] * q + p.Qz, accum[t, 3] * q + p.Qw,
                    accum[t, 4] * q + p.Tx, accum[t, 5] * q + p.Ty, accum[t, 6] * q + p.Tz]);
            }
            clip.Times.Add(time);
            clip.Poses.Add((EnbayaPose[])pose.Clone());
            sampleTime = sample * sps;
            if (sample > 100000) throw new InvalidDataException("amostras demais");
        }

        // Conferência: no fim, cada subfluxo deve ter sido lido exatamente até o próprio limite
        // (os de 2 e 4 bits podem sobrar bits no último byte).
        Check("i8 iniciais", ii8, pInitI8, initI8);
        Check("i16 iniciais", ii16, pInitI16, initI16);
        Check("i32 iniciais", ii32, pInitI32, initI32);
        Check("i2 iniciais", ii2 + (ii2c > 0 ? 1 : 0), pInitI2, initI2);
        Check("i2 de delta", ti2 + (ti2c > 0 ? 1 : 0), pTrI2, trI2);
        Check("i4 de delta", ti4 + (ti4c > 0 ? 1 : 0), pTrI4, trI4);
        Check("i8 de delta", ti8, pTrI8, trI8);
        Check("i16 de delta", ti16, pTrI16, trI16);
        Check("i32 de delta", ti32, pTrI32, trI32);
        Check("u2 de estado", su2 + (su2c > 0 ? 1 : 0), pStU2, stU2);
        Check("u8 de estado", su8, pStU8, stU8);
        Check("u16 de estado", su16, pStU16, stU16);
        Check("u32 de estado", su32, pStU32, stU32);
        return clip;

        static EnbayaPose Normalize(float[] v)
        {
            float len = MathF.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2] + v[3] * v[3]);
            float k = len != 0 ? 1f / len : 0;
            return new EnbayaPose(v[0] * k, v[1] * k, v[2] * k, v[3] * k, v[4], v[5], v[6]);
        }

        void Check(string what, int cursor, int start, int length)
        {
            int used = cursor - start;
            clip.Consumption.Add($"{what}: {used}/{length}");
            if (used != length) clip.ConsumedExactly = false;
        }
    }
}
