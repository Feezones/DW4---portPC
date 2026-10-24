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

        uint U32(long at)
        {
            if (at < 0 || at + 4 > data.Length) throw new InvalidDataException($"leitura fora do arquivo em 0x{at:X}");
            var s = data.Slice((int)at, 4);
            return h.LittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
        }

        void Add(string name, long size)
        {
            if (size < 0 || pos + size > data.Length)
                throw new InvalidDataException($"seção {name} (0x{pos:X}, {size} B) passa do fim do arquivo");
            sections.Add(new IgbSection(name, pos, size));
            pos += size;
        }

        Add("cabeçalho", IgbHeader.Size);
        Add("metacampos", h.MetaFieldBufferSize);
        Add("alinhamento", U32(pos));                 // tamanho inclui o próprio prefixo
        Add("metaobjetos", h.MetaObjectBufferSize);
        if (h.HasExternal) Add("diretórios externos", U32(pos));
        if (h.HasMemoryPoolNames) Add("nomes de pools", U32(pos));
        Add("entradas", h.EntryBufferSize);
        Add("índice", U32(pos));
        if (h.HasInfo) Add("info", 4);
        Add("objetos", h.ObjectBufferSize);
        Add("memória", h.MemoryRefBufferSize);
        return sections;
    }

    /// <summary>Bytes que sobram depois da última seção (0 = layout fecha exatamente).</summary>
    public static long Trailing(ReadOnlySpan<byte> data, List<IgbSection> sections) =>
        data.Length - (sections.Count == 0 ? 0 : sections[^1].Offset + sections[^1].Size);
}
