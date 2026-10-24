using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>
/// Cabeçalho de 0x30 bytes de um arquivo IGB (Intrinsic Alchemy): 12 × u32.
/// Ordem dos campos tirada de implementações públicas para X-Men Legends / Marvel Ultimate Alliance
/// (igb-blender, IGBConverter); conferência com os arquivos do DW4 em docs/formatos/igb.md.
/// A ordem de bytes é detectada pela marca 0xFADA em 0x28.
/// </summary>
public sealed record IgbHeader(
    bool LittleEndian,
    uint EntryBufferSize, uint EntryCount,
    uint MetaObjectBufferSize, uint MetaObjectCount,
    uint ObjectBufferSize, uint ObjectCount,
    uint MemoryRefBufferSize, uint MemoryRefCount,
    uint MetaFieldBufferSize, uint MetaFieldCount,
    uint VersionFlags)
{
    public const int Size = 0x30;
    public const uint MagicCookie = 0xFADA;

    public int Version => (int)(VersionFlags & 0xFFFF);
    public bool HasInfo => (VersionFlags & 0x80000000) != 0;
    public bool HasExternal => (VersionFlags & 0x40000000) != 0;
    public bool SharedEntries => (VersionFlags & 0x20000000) != 0;
    public bool HasMemoryPoolNames => (VersionFlags & 0x10000000) != 0;

    /// <summary>Soma dos buffers declarados mais o cabeçalho; deve caber no arquivo.</summary>
    public long DeclaredSize => (long)Size + MetaFieldBufferSize + MetaObjectBufferSize + EntryBufferSize
        + ObjectBufferSize + MemoryRefBufferSize;

    public static bool TryRead(ReadOnlySpan<byte> data, out IgbHeader? header)
    {
        header = null;
        if (data.Length < Size) return false;

        bool le;
        if (BinaryPrimitives.ReadUInt32LittleEndian(data[0x28..]) == MagicCookie) le = true;
        else if (BinaryPrimitives.ReadUInt32BigEndian(data[0x28..]) == MagicCookie) le = false;
        else return false;

        var f = new uint[12];
        for (int i = 0; i < 12; i++)
            f[i] = le ? BinaryPrimitives.ReadUInt32LittleEndian(data[(4 * i)..])
                      : BinaryPrimitives.ReadUInt32BigEndian(data[(4 * i)..]);

        header = new IgbHeader(le, f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[11]);
        return true;
    }
}
