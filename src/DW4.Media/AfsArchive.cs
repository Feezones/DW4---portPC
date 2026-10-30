using System.Buffers.Binary;
using System.Text;

namespace DW4.Formats.Afs;

/// <summary>Uma entrada da tabela do AFS.</summary>
/// <param name="Name">Nome da tabela de nomes (no DW4 são só 4 caracteres, funcionam como categoria), ou null.</param>
/// <param name="Timestamp">Data e hora da tabela de nomes, ou null se ausente ou inválida.</param>
public sealed record AfsEntry(int Index, uint Offset, uint Size, string? Name, DateTime? Timestamp);

/// <summary>
/// Leitor somente leitura de contêineres AFS da CRI, na variante encontrada em Digimon World 4 (GameCube).
/// Layout validado em 2026-10-24 (docs/formatos/afs.md):
///   0x0  "AFS\0"
///   0x4  u32 LE: N entradas
///   0x8  N pares (u32 offset, u32 tamanho) LE
///   8+8N par (u32 offset, u32 tamanho) da tabela de nomes: N registros de 0x30 bytes
///        (32 bytes de nome ASCII, 6 × u16 ano/mês/dia/hora/min/seg, 1 × u32)
/// </summary>
public sealed class AfsArchive : IDisposable
{
    public const int NameRecordSize = 0x30;
    static readonly byte[] Magic = "AFS\0"u8.ToArray();

    readonly FileStream _stream;

    public string Path { get; }
    public long Length => _stream.Length;
    public IReadOnlyList<AfsEntry> Entries { get; }
    public uint NameTableOffset { get; }
    public uint NameTableSize { get; }
    public bool HasNameTable { get; }

    AfsArchive(string path, FileStream stream, IReadOnlyList<AfsEntry> entries, uint nameOffset, uint nameSize, bool hasNames)
    {
        Path = path;
        _stream = stream;
        Entries = entries;
        NameTableOffset = nameOffset;
        NameTableSize = nameSize;
        HasNameTable = hasNames;
    }

    public static AfsArchive Open(string path)
    {
        // Nunca abre para escrita; permite outros leitores ao mesmo tempo.
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            Span<byte> head = stackalloc byte[8];
            fs.ReadExactly(head);
            if (!head[..4].SequenceEqual(Magic))
                throw new InvalidDataException($"{path}: assinatura não é AFS\\0");

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(head[4..]);
            // Tabela + par da tabela de nomes precisam caber no arquivo.
            if (8L + 8L * count + 8 > fs.Length)
                throw new InvalidDataException($"{path}: N = {count} não cabe no arquivo");

            var table = new byte[8 * count + 8];
            fs.ReadExactly(table);

            var offsets = new uint[count];
            var sizes = new uint[count];
            for (int i = 0; i < count; i++)
            {
                offsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * i));
                sizes[i] = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * i + 4));
            }
            uint nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * (int)count));
            uint nameSize = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(8 * (int)count + 4));

            var names = new string?[count];
            var stamps = new DateTime?[count];
            bool hasNames = nameOffset != 0
                && nameSize >= (long)count * NameRecordSize
                && (long)nameOffset + nameSize <= fs.Length;
            if (hasNames)
            {
                var raw = new byte[count * NameRecordSize];
                fs.Position = nameOffset;
                fs.ReadExactly(raw);
                for (int i = 0; i < count; i++)
                {
                    var rec = raw.AsSpan(i * NameRecordSize, NameRecordSize);
                    int z = rec[..32].IndexOf((byte)0);
                    names[i] = Encoding.ASCII.GetString(rec[..(z < 0 ? 32 : z)]);
                    stamps[i] = ReadStamp(rec[32..44]);
                }
            }

            var entries = new AfsEntry[count];
            for (int i = 0; i < count; i++)
                entries[i] = new AfsEntry(i, offsets[i], sizes[i], names[i], stamps[i]);

            return new AfsArchive(path, fs, entries, nameOffset, nameSize, hasNames);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    static DateTime? ReadStamp(ReadOnlySpan<byte> s)
    {
        int y = BinaryPrimitives.ReadUInt16LittleEndian(s);
        int mo = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]);
        int d = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]);
        int h = BinaryPrimitives.ReadUInt16LittleEndian(s[6..]);
        int mi = BinaryPrimitives.ReadUInt16LittleEndian(s[8..]);
        int se = BinaryPrimitives.ReadUInt16LittleEndian(s[10..]);
        try { return new DateTime(y, mo, d, h, mi, se); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// <summary>Confere as invariantes observadas no DW4. Lista vazia = estrutura consistente.</summary>
    public List<string> Validate(uint alignment = 0x800)
    {
        var problems = new List<string>();
        for (int i = 0; i < Entries.Count; i++)
        {
            var e = Entries[i];
            if (e.Size == 0) problems.Add($"#{i}: tamanho 0");
            if (e.Offset % alignment != 0) problems.Add($"#{i}: offset 0x{e.Offset:X} não alinhado a 0x{alignment:X}");
            if ((long)e.Offset + e.Size > Length) problems.Add($"#{i}: passa do fim do arquivo");
            if (i > 0)
            {
                var p = Entries[i - 1];
                if (e.Offset <= p.Offset) problems.Add($"#{i}: offset não crescente");
                else if (e.Offset < (long)p.Offset + p.Size) problems.Add($"#{i}: sobrepõe a entrada anterior");
            }
        }
        if (!HasNameTable) problems.Add("tabela de nomes ausente ou inválida");
        return problems;
    }

    /// <summary>Lê até <paramref name="count"/> bytes do início da entrada.</summary>
    public byte[] ReadHead(AfsEntry e, int count)
    {
        int n = (int)Math.Min(count, e.Size);
        var buf = new byte[n];
        _stream.Position = e.Offset;
        _stream.ReadExactly(buf);
        return buf;
    }

    /// <summary>Copia a entrada inteira para <paramref name="destination"/>.</summary>
    public void CopyEntry(AfsEntry e, Stream destination)
    {
        _stream.Position = e.Offset;
        var buf = new byte[81920];
        long left = e.Size;
        while (left > 0)
        {
            int n = _stream.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) throw new EndOfStreamException($"#{e.Index}: fim do arquivo antes do esperado");
            destination.Write(buf, 0, n);
            left -= n;
        }
    }

    public void Dispose() => _stream.Dispose();
}
