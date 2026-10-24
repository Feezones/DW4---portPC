using System.Buffers.Binary;

namespace DW4.Formats.Dolphin;

/// <summary>
/// Save state do Dolphin (.sav / .sNN) para leitura da RAM do GameCube. Cabeçalho visto no Dolphin 2609a
/// (2026-10-24, docs/fidelidade/fase3-roteiro-medicao.md): game id em 0x00, versão em 0x18–0x2C, tamanho descomprimido
/// (u64) em 0x35, tamanho comprimido (u32) em 0x3D e um único bloco LZ4 a partir de 0x41.
/// </summary>
public static class SaveState
{
    public const int Mem1Size = 0x1800000;
    const uint DiscMagic = 0xC2339F3D;

    public static byte[] Decompress(ReadOnlySpan<byte> file)
    {
        if (file.Length < 0x41) throw new InvalidDataException("save state curto demais");
        long raw = BinaryPrimitives.ReadInt64LittleEndian(file[0x35..]);
        uint packed = BinaryPrimitives.ReadUInt32LittleEndian(file[0x3D..]);
        if (packed != file.Length - 0x41 || raw <= 0 || raw > int.MaxValue)
            throw new InvalidDataException($"cabeçalho inesperado: comprimido {packed}, arquivo {file.Length}, descomprimido {raw}");
        return Lz4.DecompressBlock(file[0x41..], (int)raw);
    }

    /// <summary>Acha a MEM1 (24 MiB a partir de 0x80000000) no estado descomprimido: começa com o id do disco e o magic GC em 0x1C.</summary>
    public static int FindMem1(ReadOnlySpan<byte> state, string gameId)
    {
        var id = System.Text.Encoding.ASCII.GetBytes(gameId);
        for (int at = state.IndexOf(id); at >= 0;)
        {
            if (at + Mem1Size <= state.Length && BinaryPrimitives.ReadUInt32BigEndian(state[(at + 0x1C)..]) == DiscMagic) return at;
            int next = state[(at + 1)..].IndexOf(id);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return -1;
    }
}

/// <summary>Descompressor de bloco LZ4 (formato de bloco, sem cabeçalho de frame).</summary>
public static class Lz4
{
    public static byte[] DecompressBlock(ReadOnlySpan<byte> src, int outputSize)
    {
        var dst = new byte[outputSize];
        int s = 0, d = 0;
        while (s < src.Length)
        {
            int token = src[s++];
            int lit = token >> 4;
            if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
            if (d + lit > dst.Length || s + lit > src.Length) throw new InvalidDataException("LZ4: literais fora do limite");
            src.Slice(s, lit).CopyTo(dst.AsSpan(d));
            s += lit;
            d += lit;
            if (s >= src.Length) break; // último bloco só de literais
            int offset = src[s] | (src[s + 1] << 8);
            s += 2;
            int len = token & 15;
            if (len == 15) { int b; do { b = src[s++]; len += b; } while (b == 255); }
            len += 4;
            if (offset == 0 || offset > d || d + len > dst.Length) throw new InvalidDataException("LZ4: cópia inválida");
            for (int i = 0; i < len; i++) dst[d + i] = dst[d - offset + i];
            d += len;
        }
        if (d != outputSize) throw new InvalidDataException($"LZ4: {d} bytes, esperado {outputSize}");
        return dst;
    }
}
