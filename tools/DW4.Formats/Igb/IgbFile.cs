using System.Buffers.Binary;
using System.Text;

namespace DW4.Formats.Igb;

/// <summary>Descritor de campo de um metaobjeto: tipo (índice de metacampo), slot e tamanho serializado.</summary>
public sealed record IgbFieldDesc(int TypeIndex, int Slot, int Size, string TypeName)
{
    /// <summary>Nome curto do tipo, sem "ig" e "MetaField" (ex.: "MemoryRef", "Float").</summary>
    public string ShortName { get; } = TypeName.StartsWith("ig") && TypeName.EndsWith("MetaField")
        ? TypeName[2..^"MetaField".Length] : TypeName;
}

public sealed record IgbFieldValue(IgbFieldDesc Desc, int Offset, byte[] Raw, string? Text)
{
    /// <summary>Valor como i32 (para ObjectRef/MemoryRef: índice no diretório, -1 = nulo).</summary>
    public int AsInt32(bool le) => Raw.Length >= 4
        ? (le ? BinaryPrimitives.ReadInt32LittleEndian(Raw) : BinaryPrimitives.ReadInt32BigEndian(Raw)) : 0;
}

public abstract record IgbSlot(int Index, string TypeName);

public sealed record IgbObject(int Index, string TypeName, long FileOffset, IReadOnlyList<IgbFieldValue> Fields)
    : IgbSlot(Index, TypeName);

public sealed record IgbMemoryBlock(int Index, string TypeName, long FileOffset, int Size)
    : IgbSlot(Index, TypeName);

/// <summary>
/// Leitura completa de um IGB v6/v7: diretório (entradas + índice), objetos com campos crus e blocos de memória.
/// Segue a descrição de implementações públicas (igb-blender, igb_reader.py), validada contra os arquivos do DW4
/// pelas checagens de <see cref="Problems"/>. Os campos não são interpretados além do tipo declarado.
/// </summary>
public sealed class IgbFile
{
    public IgbHeader Header { get; }
    public IReadOnlyList<IgbMetaField> MetaFields { get; }
    public IReadOnlyList<string> MetaObjectNames { get; }
    public IReadOnlyList<IReadOnlyList<IgbFieldDesc>> MetaObjectFields { get; }
    public IReadOnlyList<int> MetaObjectParents { get; }
    public IReadOnlyList<IgbSlot> Slots { get; }
    public List<string> Problems { get; } = [];

    IgbFile(IgbHeader h, IReadOnlyList<IgbMetaField> mf, List<string> moNames, List<IReadOnlyList<IgbFieldDesc>> moFields,
        List<int> moParents, List<IgbSlot> slots)
    {
        Header = h;
        MetaFields = mf;
        MetaObjectNames = moNames;
        MetaObjectFields = moFields;
        MetaObjectParents = moParents;
        Slots = slots;
    }

    public bool IsA(int metaObjectIndex, string className)
    {
        for (int i = metaObjectIndex, guard = 0; i >= 0 && i < MetaObjectNames.Count && guard < 64; i = MetaObjectParents[i], guard++)
            if (MetaObjectNames[i] == className) return true;
        return false;
    }

    public static IgbFile Read(byte[] data)
    {
        if (!IgbHeader.TryRead(data, out var hdr)) throw new InvalidDataException("não é IGB");
        var h = hdr!;
        bool le = h.LittleEndian;
        var sections = IgbLayout.Walk(data, h);
        IgbSection Sec(string name) => sections.First(s => s.Name == name);

        var types = IgbTypes.Read(data, h);
        var metaFields = types.MetaFields;

        // Metaobjetos com descritores de campo.
        var moSec = Sec("metaobjetos");
        int moCount = (int)h.MetaObjectCount;
        var moNames = new List<string>(moCount);
        var moFields = new List<IReadOnlyList<IgbFieldDesc>>(moCount);
        var moParents = new List<int>(moCount);
        int dyn = (int)moSec.Offset + moCount * 24;
        for (int i = 0; i < moCount; i++)
        {
            int b = (int)moSec.Offset + i * 24;
            int nameLen = (int)U32(data, b, le);
            int nFields = (int)U32(data, b + 12, le);
            moParents.Add((int)U32(data, b + 16, le));
            moNames.Add(types.MetaObjects[i].Name);
            dyn += nameLen;
            var fields = new List<IgbFieldDesc>(nFields);
            for (int f = 0; f < nFields; f++, dyn += 6)
            {
                int t = U16(data, dyn, le), slot = U16(data, dyn + 2, le), size = U16(data, dyn + 4, le);
                fields.Add(new IgbFieldDesc(t, slot, size, t < metaFields.Count ? metaFields[t].Name : $"?{t}"));
            }
            moFields.Add(fields);
        }
        if (dyn != moSec.Offset + moSec.Size)
            throw new InvalidDataException($"metaobjetos: lidos até 0x{dyn:X}, seção termina em 0x{moSec.Offset + moSec.Size:X}");

        // Entradas do diretório: campos de 1, 2 ou 4 bytes alinhados a 4.
        var entSec = Sec("entradas");
        var entries = new List<(string type, Dictionary<int, int> bySlot)>();
        int pos = (int)entSec.Offset;
        for (int i = 0; i < h.EntryCount; i++)
        {
            int type = (int)U32(data, pos, le);
            int size = (int)U32(data, pos + 4, le);
            var bySlot = new Dictionary<int, int>();
            int fo = pos + 8;
            foreach (var fd in moFields[type])
            {
                bySlot[fd.Slot] = fd.Size switch
                {
                    4 => (int)U32(data, fo, le),
                    2 => (short)U16(data, fo, le),
                    1 => data[fo],
                    _ => throw new InvalidDataException($"entrada {i}: campo de {fd.Size} bytes"),
                };
                fo += (fd.Size + 3) & ~3;
            }
            entries.Add((moNames[type], bySlot));
            pos += size;
        }
        if (pos != entSec.Offset + entSec.Size) throw new InvalidDataException("buffer de entradas não fecha");

        // Índice: u16 por slot do diretório.
        var idxSec = Sec("índice");
        int nIdx = (int)U32(data, (int)idxSec.Offset + 4, le);
        var index = new int[nIdx];
        for (int i = 0; i < nIdx; i++) index[i] = U16(data, (int)idxSec.Offset + 8 + 2 * i, le);

        // Objetos (na ordem dos slots de objeto) e blocos de memória (na ordem dos slots de memória).
        var objSec = Sec("objetos");
        var memSec = Sec("memória");
        int op = (int)objSec.Offset, mp = (int)memSec.Offset;
        var slots = new List<IgbSlot>(nIdx);
        var problems = new List<string>();
        for (int s = 0; s < nIdx; s++)
        {
            var (etype, bySlot) = entries[index[s]];
            if (etype == "igMemoryDirEntry")
            {
                int memSize = bySlot.GetValueOrDefault(7);
                int memType = bySlot.GetValueOrDefault(10, -1);
                string tn = memType >= 0 && memType < moNames.Count ? moNames[memType] : "(memória)";
                slots.Add(new IgbMemoryBlock(s, tn, mp, memSize));
                mp += (memSize + 3) & ~3;
            }
            else if (etype == "igObjectDirEntry")
            {
                int type = (int)U32(data, op, le);
                int size = (int)U32(data, op + 4, le);
                var fields = new List<IgbFieldValue>();
                int fo = op + 8, end = op + size;
                foreach (var fd in moFields[type])
                {
                    if (fo + fd.Size > end) break;
                    int actual = fd.Size;
                    string? text = null;
                    if (fd.ShortName == "String")
                    {
                        int len = (int)U32(data, fo, le);
                        actual = fd.Size + len;
                        if (fo + actual > end) break;
                        var raw = data.AsSpan(fo + 4, len);
                        int z = raw.IndexOf((byte)0);
                        text = Encoding.Latin1.GetString(z < 0 ? raw : raw[..z]);
                    }
                    fields.Add(new IgbFieldValue(fd, fo, data.AsSpan(fo, actual).ToArray(), text));
                    fo += (actual + 3) & ~3;
                }
                slots.Add(new IgbObject(s, moNames[type], op, fields));
                op += size;
            }
            else problems.Add($"slot {s}: entrada de tipo inesperado {etype}");
        }
        if (op != objSec.Offset + objSec.Size) problems.Add($"objetos: fim em 0x{op:X}, seção termina em 0x{objSec.Offset + objSec.Size:X}");
        if (mp != memSec.Offset + memSec.Size) problems.Add($"memória: fim em 0x{mp:X}, seção termina em 0x{memSec.Offset + memSec.Size:X}");

        var file = new IgbFile(h, metaFields, moNames, moFields, moParents, slots);
        file.Problems.AddRange(problems);
        return file;
    }

    static uint U32(byte[] d, int at, bool le)
    {
        if (at < 0 || at + 4 > d.Length) throw new InvalidDataException($"leitura fora do arquivo em 0x{at:X}");
        return le ? BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(at));
    }

    static int U16(byte[] d, int at, bool le)
    {
        if (at < 0 || at + 2 > d.Length) throw new InvalidDataException($"leitura fora do arquivo em 0x{at:X}");
        return le ? BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(at));
    }
}
