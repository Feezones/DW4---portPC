using System.Buffers.Binary;

namespace DW4.Formats.Ppc;

/// <summary>Uma seção de um DOL: offset no arquivo, endereço de carga e tamanho.</summary>
public sealed record DolSection(string Name, uint Offset, uint Address, uint Size, bool IsText)
{
    public bool Contains(uint address) => address >= Address && address < Address + Size;
}

/// <summary>
/// Executável de GameCube (.dol), lido só como dados (nunca executado). Cabeçalho de 0x100 bytes, big-endian:
/// offsets de 7 seções de texto e 11 de dados em 0x00, endereços em 0x48, tamanhos em 0x90, BSS em 0xD8/0xDC,
/// entrada em 0xE0 (formato público, ver docs/formatos).
/// </summary>
public sealed class DolFile
{
    public byte[] Data { get; }
    public IReadOnlyList<DolSection> Sections { get; }
    public uint BssAddress { get; }
    public uint BssSize { get; }
    public uint Entry { get; }

    DolFile(byte[] data, List<DolSection> sections, uint bss, uint bssSize, uint entry)
    {
        Data = data;
        Sections = sections;
        BssAddress = bss;
        BssSize = bssSize;
        Entry = entry;
    }

    public static DolFile Read(byte[] data)
    {
        if (data.Length < 0x100) throw new InvalidDataException("DOL menor que o cabeçalho");
        uint U(int at) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        var sections = new List<DolSection>();
        for (int i = 0; i < 18; i++)
        {
            uint off = U(i * 4), addr = U(0x48 + i * 4), size = U(0x90 + i * 4);
            if (size == 0) continue;
            if ((ulong)off + size > (ulong)data.Length) throw new InvalidDataException($"seção {i} fora do arquivo");
            bool text = i < 7;
            sections.Add(new DolSection(text ? $"text{i}" : $"data{i - 7}", off, addr, size, text));
        }
        return new DolFile(data, sections, U(0xD8), U(0xDC), U(0xE0));
    }

    public DolSection? SectionAt(uint address) => Sections.FirstOrDefault(s => s.Contains(address));

    /// <summary>Offset no arquivo de um endereço, ou -1 se ele não está em nenhuma seção.</summary>
    public long OffsetOf(uint address) => SectionAt(address) is { } s ? s.Offset + (address - s.Address) : -1;

    public uint Word(uint address)
    {
        long at = OffsetOf(address);
        if (at < 0 || at + 4 > Data.Length) throw new ArgumentOutOfRangeException(nameof(address), $"0x{address:X8} fora das seções");
        return BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)at));
    }

    public bool TryWord(uint address, out uint word)
    {
        long at = OffsetOf(address);
        word = at >= 0 && at + 4 <= Data.Length ? BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)at)) : 0;
        return at >= 0 && at + 4 <= Data.Length;
    }

    /// <summary>String C (ASCII) num endereço, ou null.</summary>
    public string? CString(uint address, int max = 256)
    {
        long at = OffsetOf(address);
        if (at < 0) return null;
        int end = (int)at;
        while (end < Data.Length && end - at < max && Data[end] != 0) end++;
        if (end == at) return "";
        for (long i = at; i < end; i++) if (Data[i] < 0x20 || Data[i] > 0x7E) return null;
        return System.Text.Encoding.ASCII.GetString(Data, (int)at, (int)(end - at));
    }

    /// <summary>Endereços (alinhados a 4) de palavras iguais a <paramref name="value"/> nas seções de dados.</summary>
    public IEnumerable<uint> FindPointers(uint value)
    {
        foreach (var s in Sections.Where(s => !s.IsText))
            for (uint a = 0; a + 4 <= s.Size; a += 4)
                if (BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)(s.Offset + a))) == value) yield return s.Address + a;
    }

    /// <summary>
    /// Bases de small data (r13 e r2) montadas no código de partida com lis rN, hi + addi/ori rN, rN, lo. Null se não achar.
    /// </summary>
    public (uint? R13, uint? R2) SmallDataBases()
    {
        uint? r13 = null, r2 = null;
        foreach (var s in Sections.Where(s => s.IsText))
            for (uint a = 0; a + 8 <= s.Size; a += 4)
            {
                uint w0 = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)(s.Offset + a)));
                uint w1 = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)(s.Offset + a + 4)));
                foreach (int r in new[] { 13, 2 })
                {
                    if (w0 >> 26 != 15 || ((w0 >> 21) & 31) != r || ((w0 >> 16) & 31) != 0) continue;
                    if (((w1 >> 21) & 31) != r || ((w1 >> 16) & 31) != r) continue;
                    uint hi = (w0 & 0xFFFF) << 16;
                    uint? v = (w1 >> 26) switch { 14 => (uint)(hi + (short)(w1 & 0xFFFF)), 24 => hi | (w1 & 0xFFFF), _ => null };
                    if (v == null) continue;
                    if (r == 13) r13 ??= v; else r2 ??= v;
                }
                if (r13 != null && r2 != null) return (r13, r2);
            }
        return (r13, r2);
    }

    /// <summary>Endereços de todas as ocorrências da string C exata (terminada em zero) nas seções de dados.</summary>
    public IEnumerable<uint> FindString(string text)
    {
        var needle = System.Text.Encoding.ASCII.GetBytes(text + "\0");
        foreach (var s in Sections.Where(s => !s.IsText))
        {
            var span = Data.AsSpan((int)s.Offset, (int)s.Size);
            int from = 0;
            while (from < span.Length)
            {
                int i = span[from..].IndexOf(needle);
                if (i < 0) break;
                int at = from + i;
                if (at == 0 || span[at - 1] == 0) yield return s.Address + (uint)at;
                from = at + 1;
            }
        }
    }

    /// <summary>
    /// Instruções que montam <paramref name="target"/> com o par lis rX, hi + addi/ori/lwz…(rX, lo), procurando o lis até
    /// 32 instruções antes. Também reconhece acesso relativo a r13/r2 (small data) quando os valores-base são dados.
    /// </summary>
    public IEnumerable<uint> FindCodeRefs(uint target, uint? r13 = null, uint? r2 = null)
    {
        foreach (var s in Sections.Where(s => s.IsText))
        {
            var hi = new Dictionary<int, (uint Value, uint At)>();
            for (uint a = 0; a + 4 <= s.Size; a += 4)
            {
                uint w = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan((int)(s.Offset + a)));
                uint pc = s.Address + a;
                int op = (int)(w >> 26), rd = (int)(w >> 21) & 31, ra = (int)(w >> 16) & 31;
                short imm = (short)(w & 0xFFFF);
                if (op == 15 && ra == 0) { hi[rd] = ((uint)(ushort)imm << 16, pc); continue; }
                bool usesBase = op is 14 or 24 or 32 or 34 or 36 or 38 or 40 or 42 or 44 or 48 or 50 or 52 or 54;
                if (!usesBase) continue;
                uint value;
                if (ra == 13 && r13 is { } b13 && op != 24) value = (uint)(b13 + imm);
                else if (ra == 2 && r2 is { } b2 && op != 24) value = (uint)(b2 + imm);
                else if (hi.TryGetValue(ra, out var h) && pc - h.At <= 32 * 4) value = op == 24 ? h.Value | (ushort)imm : (uint)(h.Value + imm);
                else continue;
                if (value == target) yield return pc;
            }
        }
    }
}
