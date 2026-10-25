using System.Buffers.Binary;
using System.Text;

namespace DW4.Formats.Igb;

/// <summary>Uma coluna de beDataBase: nome (ASCII), tipo ("char", "short", "int", "float", "bool", "text") e células.</summary>
public sealed record IgbDbColumn(string Name, string Kind, IReadOnlyList<object> Cells);

/// <summary>Uma tabela do jogo (beDBManagerInfo → beDataBase), por exemplo "weapon", "enemy" ou "shinka".</summary>
public sealed record IgbDbTable(string Name, int InfoIndex, int DataIndex, int Rows, IReadOnlyList<IgbDbColumn> Columns)
{
    public IgbDbColumn? Column(string name) => Columns.FirstOrDefault(c => c.Name == name);

    /// <summary>Índices das linhas cuja coluna <paramref name="column"/> vale <paramref name="value"/> (texto ou número).</summary>
    public IEnumerable<int> RowsWhere(string column, object value) =>
        Column(column) is { } c ? Enumerable.Range(0, Rows).Where(r => Equals(c.Cells[r], value)) : [];
}

/// <summary>
/// Tabelas de balanceamento do jogo, embutidas em cada IGB de fase (FATO, conferido no area.afs #3 e #441 em 2026-10-24:
/// as 12 tabelas são iguais byte a byte). Layout lido dos metaobjetos do próprio arquivo:
///  · beDBManagerInfo (igNamedObject → igInfo → beBaseInfo): String@2 = nome da tabela, ObjectRef@7 = beDataBase.
///  · beDataBase: seis pares ObjectRef (nomes, valores), @2/@3 char, @4/@5 short, @6/@7 int, @8/@9 float, @10/@11 bool,
///    @12/@13 texto. Nomes = igNamedObjectList (String@2 de cada igNamedObject); valores = igDataList (Int@2 = quantidade,
///    MemoryRef@4 = bloco) com a célula [linha · colunas + coluna], na ordem de bytes do arquivo; texto = um igNamedObject
///    por célula, em Shift-JIS.
///  · O número de linhas não é gravado: é quantidade de valores / quantidade de nomes (as divisões batem nas 12 tabelas).
/// </summary>
public static class IgbDatabase
{
    static readonly (int Names, int Values, string Kind, int Size)[] Pairs =
        [(2, 3, "char", 1), (4, 5, "short", 2), (6, 7, "int", 4), (8, 9, "float", 4), (10, 11, "bool", 1), (12, 13, "text", 4)];

    public static List<IgbDbTable> Read(IgbFile f, byte[] data, List<string> notes)
    {
        bool le = f.Header.LittleEndian;
        Encoding sjis = ShiftJis();
        IgbObject? Obj(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
        int Ref(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot) is { } v ? v.AsInt32(le) : -1;
        string Text(IgbObject? o, bool shiftJis)
        {
            var v = o?.Fields.FirstOrDefault(x => x.Desc.ShortName == "String" && x.Desc.Slot == 2);
            if (v?.Text == null) return "";
            return shiftJis ? sjis.GetString(Encoding.Latin1.GetBytes(v.Text)) : v.Text;
        }
        (int Count, byte[] Memory) List(int i)
        {
            if (Obj(i) is not { } o) return (0, []);
            int count = o.Fields.FirstOrDefault(v => v.Desc.Slot == 2)?.AsInt32(le) ?? 0;
            int mem = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef")?.AsInt32(le) ?? -1;
            if (mem < 0 || mem >= f.Slots.Count || f.Slots[mem] is not IgbMemoryBlock mb) return (count, []);
            return (count, data.AsSpan((int)mb.FileOffset, mb.Size).ToArray());
        }
        int I32(byte[] m, int at) => le ? BinaryPrimitives.ReadInt32LittleEndian(m.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(m.AsSpan(at));
        short I16(byte[] m, int at) => le ? BinaryPrimitives.ReadInt16LittleEndian(m.AsSpan(at)) : BinaryPrimitives.ReadInt16BigEndian(m.AsSpan(at));

        var tables = new List<IgbDbTable>();
        foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beDBManagerInfo"))
        {
            string name = Text(info, false);
            if (Obj(Ref(info, 7)) is not { } db || db.TypeName != "beDataBase") { notes.Add($"{name}: sem beDataBase"); continue; }
            var columns = new List<IgbDbColumn>();
            int rows = -1;
            foreach (var (namesSlot, valuesSlot, kind, size) in Pairs)
            {
                var (nameCount, nameMem) = List(Ref(db, namesSlot));
                var names = Enumerable.Range(0, Math.Min(nameCount, nameMem.Length / 4)).Select(k => Text(Obj(I32(nameMem, 4 * k)), false)).ToArray();
                if (names.Length == 0) continue;
                var (count, mem) = List(Ref(db, valuesSlot));
                if (count % names.Length != 0) notes.Add($"{name}/{kind}: {count} valores não dividem por {names.Length} colunas");
                int r = count / names.Length;
                if (rows < 0) rows = r;
                else if (r != rows) { notes.Add($"{name}/{kind}: {r} linhas, esperado {rows}"); continue; }
                if (mem.Length < count * size) { notes.Add($"{name}/{kind}: bloco curto"); continue; }
                for (int c = 0; c < names.Length; c++)
                {
                    var cells = new object[r];
                    for (int row = 0; row < r; row++)
                    {
                        int cell = row * names.Length + c;
                        cells[row] = kind switch
                        {
                            "char" => (sbyte)mem[cell],
                            "short" => I16(mem, 2 * cell),
                            "int" => I32(mem, 4 * cell),
                            "float" => BitConverter.Int32BitsToSingle(I32(mem, 4 * cell)),
                            "bool" => mem[cell] != 0,
                            _ => Text(Obj(I32(mem, 4 * cell)), true),
                        };
                    }
                    columns.Add(new IgbDbColumn(names[c], kind, cells));
                }
            }
            tables.Add(new IgbDbTable(name, info.Index, db.Index, Math.Max(rows, 0), columns));
        }
        return tables;
    }

    static Encoding ShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}
