using System.Buffers.Binary;

namespace DW4.Formats.Igb;

public sealed record IgbSection(string Name, long Offset, long Size);

/// <summary>
/// Percorre as seções de um IGB versão 6/7 na ordem descrita por implementações públicas
/// (igb-blender, igb_reader.py): cabeçalho, metacampos, alinhamento, metaobjetos,
/// diretórios externos, nomes de pools, entradas, índice, info, objetos, memória.
/// Não interpreta o conteúdo; serve para provar que a soma das seções fecha com o arquivo.
/// </summary>
public static class IgbLayout
{
    public static List<IgbSection> Walk(ReadOnlySpan<byte> data, IgbHeader h)
    {
        if (h.Version is < 6 or >= 8)
            throw new NotSupportedException($"IGB versão {h.Version}: só 6 e 7 por enquanto");

        var sections = new List<IgbSection>();
        long pos = 0;
        long length = data.Length;

        // Seções de tamanho fixo vêm do cabeçalho; as demais começam com um u32 com o próprio tamanho.
        Add(sections, ref pos, length, "cabeçalho", IgbHeader.Size);
        Add(sections, ref pos, length, "metacampos", h.MetaFieldBufferSize);
        Add(sections, ref pos, length, "alinhamento", U32(data, pos, h.LittleEndian));
        Add(sections, ref pos, length, "metaobjetos", h.MetaObjectBufferSize);
        if (h.HasExternal) Add(sections, ref pos, length, "diretórios externos", U32(data, pos, h.LittleEndian));
        if (h.HasMemoryPoolNames) Add(sections, ref pos, length, "nomes de pools", U32(data, pos, h.LittleEndian));
        Add(sections, ref pos, length, "entradas", h.EntryBufferSize);
        Add(sections, ref pos, length, "índice", U32(data, pos, h.LittleEndian));
        if (h.HasInfo) Add(sections, ref pos, length, "info", 4);
        Add(sections, ref pos, length, "objetos", h.ObjectBufferSize);
        Add(sections, ref pos, length, "memória", h.MemoryRefBufferSize);
        return sections;
    }

    /// <summary>Bytes que sobram depois da última seção (0 = layout fecha exatamente).</summary>
    public static long Trailing(ReadOnlySpan<byte> data, List<IgbSection> sections) =>
        data.Length - (sections.Count == 0 ? 0 : sections[^1].Offset + sections[^1].Size);

    static uint U32(ReadOnlySpan<byte> data, long at, bool littleEndian)
    {
        if (at < 0 || at + 4 > data.Length) throw new InvalidDataException($"leitura fora do arquivo em 0x{at:X}");
        var s = data.Slice((int)at, 4);
        return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
    }

    static void Add(List<IgbSection> sections, ref long pos, long length, string name, long size)
    {
        if (size < 0 || pos + size > length)
            throw new InvalidDataException($"seção {name} (0x{pos:X}, {size} B) passa do fim do arquivo");
        sections.Add(new IgbSection(name, pos, size));
        pos += size;
    }
}
