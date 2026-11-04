using System.Buffers.Binary;
using System.Text;

namespace DW4.Formats.Igb;

public sealed record IgbMetaField(int Index, string Name, uint Major, uint Minor);

public sealed record IgbMetaObject(int Index, string Name, uint Major, uint Minor, int FieldCount, int ParentIndex, uint SlotCount);

/// <summary>Um registro do buffer de objetos: tipo (índice de metaobjeto) e tamanho incluindo os 8 bytes do cabeçalho.</summary>
public sealed record IgbObjectRecord(long Offset, int TypeIndex, uint Size);

public sealed record IgbTypeInfo(
    IReadOnlyList<IgbMetaField> MetaFields,
    IReadOnlyList<IgbMetaObject> MetaObjects,
    IReadOnlyList<IgbObjectRecord> Objects,
    bool ObjectBufferExact);

/// <summary>
/// Lê as tabelas de tipos (metacampos e metaobjetos) e percorre o buffer de objetos de um IGB v6/v7.
/// Layout segundo implementações públicas (igb-blender, igb_types.py / igb_reader.py):
///   metacampos: N × (u32 tamanho do nome, u32 major, u32 minor) seguidos dos nomes;
///   metaobjetos: N × (u32 tamanho do nome, u32 major, u32 minor, u32 nº campos, i32 pai, u32 slots)
///                seguidos de, por metaobjeto, o nome e nº campos × (u16 tipo, u16 slot, u16 tamanho);
///   objetos: sequência de (u32 tipo, u32 tamanho com cabeçalho) + dados.
/// </summary>
public static class IgbTypes
{
    public static IgbTypeInfo Read(ReadOnlySpan<byte> data, IgbHeader h)
    {
        var sections = IgbLayout.Walk(data, h);
        IgbSection Find(string name) => sections.First(s => s.Name == name);

        var mfSec = Find("metacampos");
        var moSec = Find("metaobjetos");
        var objSec = Find("objetos");

        var metaFields = ReadMetaFields(data.Slice((int)mfSec.Offset, (int)mfSec.Size), (int)h.MetaFieldCount, h.LittleEndian);
        var metaObjects = ReadMetaObjects(data.Slice((int)moSec.Offset, (int)moSec.Size), (int)h.MetaObjectCount, h.LittleEndian);

        var objects = new List<IgbObjectRecord>();
        var buf = data.Slice((int)objSec.Offset, (int)objSec.Size);
        int off = 0;
        while (off + 8 <= buf.Length)
        {
            int type = (int)U32(buf, off, h.LittleEndian);
            uint size = U32(buf, off + 4, h.LittleEndian);
            if (size < 8 || off + size > buf.Length) break;
            objects.Add(new IgbObjectRecord(objSec.Offset + off, type, size));
            off += (int)size;
        }
        return new IgbTypeInfo(metaFields, metaObjects, objects, off == buf.Length);
    }

    static List<IgbMetaField> ReadMetaFields(ReadOnlySpan<byte> buf, int count, bool le)
    {
        var list = new List<IgbMetaField>(count);
        int dyn = count * 12;
        for (int i = 0; i < count; i++)
        {
            int len = (int)U32(buf, i * 12, le);
            list.Add(new IgbMetaField(i, Name(buf, dyn, len), U32(buf, i * 12 + 4, le), U32(buf, i * 12 + 8, le)));
            dyn += len;
        }
        return list;
    }

    static List<IgbMetaObject> ReadMetaObjects(ReadOnlySpan<byte> buf, int count, bool le)
    {
        var list = new List<IgbMetaObject>(count);
        int dyn = count * 24;
        for (int i = 0; i < count; i++)
        {
            int b = i * 24;
            int len = (int)U32(buf, b, le);
            int fields = (int)U32(buf, b + 12, le);
            string name = Name(buf, dyn, len);
            dyn += len + fields * 6;
            list.Add(new IgbMetaObject(i, name, U32(buf, b + 4, le), U32(buf, b + 8, le), fields,
                (int)U32(buf, b + 16, le), U32(buf, b + 20, le)));
        }
        return list;
    }

    static string Name(ReadOnlySpan<byte> buf, int at, int len)
    {
        if (at < 0 || len < 0 || at + len > buf.Length) throw new InvalidDataException($"nome fora do buffer em 0x{at:X}");
        var raw = buf.Slice(at, len);
        int z = raw.IndexOf((byte)0);
        return Encoding.ASCII.GetString(z < 0 ? raw : raw[..z]);
    }

    static uint U32(ReadOnlySpan<byte> buf, int at, bool le)
    {
        if (at < 0 || at + 4 > buf.Length) throw new InvalidDataException($"leitura fora do buffer em 0x{at:X}");
        var s = buf.Slice(at, 4);
        return le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);
    }
}
