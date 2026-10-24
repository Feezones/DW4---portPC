using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DW4.Formats.Afs;
using DW4.Formats.Analysis;
using DW4.Formats.Igb;

// Ferramenta de linha de comando da Fase 1. Só lê os contêineres; a única escrita é afs-extract,
// que grava amostras numa pasta de trabalho fora de qualquer repositório Git.

const int MaxExtractPerCall = 20;
var inv = CultureInfo.InvariantCulture;

if (args.Length == 0) return Usage();

try
{
    return args[0] switch
    {
        "afs-info" when args.Length == 2 => AfsInfo(args[1]),
        "afs-csv" when args.Length == 3 => AfsCsv(args[1], args[2]),
        "afs-verify" when args.Length == 3 => AfsVerify(args[1], args[2]),
        "afs-extract" when args.Length >= 4 => AfsExtract(args[1], args[2], args[3..]),
        "afs-classify" when args.Length == 2 => AfsClassify(args[1]),
        "igb-headers" when args.Length == 2 => IgbHeaders(args[1]),
        "igb-layout" when args.Length == 2 => IgbLayoutCheck(args[1]),
        "igb-types" when args.Length == 2 => IgbTypesReport(args[1]),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"erro: {ex.Message}");
    return 2;
}

static int Usage()
{
    Console.Error.WriteLine("""
        uso:
          dw4 afs-info    <arquivo.afs>
          dw4 afs-csv     <arquivo.afs> <saida.csv>
          dw4 afs-verify  <arquivo.afs> <inventario.csv>
          dw4 afs-extract <arquivo.afs> <pasta-de-trabalho> <indice> [indice...]   (máx. 20)
          dw4 afs-classify <arquivo.afs>
          dw4 igb-headers  <arquivo.afs | arquivo.igb>
          dw4 igb-layout   <arquivo.afs | arquivo.igb>
          dw4 igb-types    <arquivo.afs | arquivo.igb>
        """);
    return 1;
}

int AfsInfo(string path)
{
    using var afs = AfsArchive.Open(path);
    Console.WriteLine($"arquivo: {path}");
    Console.WriteLine($"tamanho: {afs.Length}");
    Console.WriteLine($"entradas: {afs.Entries.Count}");
    Console.WriteLine($"tabela de nomes: {(afs.HasNameTable ? $"0x{afs.NameTableOffset:X}, {afs.NameTableSize} bytes" : "ausente")}");

    var problems = afs.Validate();
    Console.WriteLine($"problemas de estrutura: {problems.Count}");
    foreach (var p in problems.Take(50)) Console.WriteLine($"  {p}");

    Console.WriteLine("categorias (nome):");
    foreach (var g in afs.Entries.GroupBy(e => e.Name ?? "(sem nome)").OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Key,-10} {g.Count(),5} entradas {g.Sum(e => (long)e.Size),12} bytes");
    return problems.Count == 0 ? 0 : 3;
}

IEnumerable<string[]> InventoryRows(AfsArchive afs)
{
    foreach (var e in afs.Entries)
    {
        var head = afs.ReadHead(e, 65536);
        var first = head.AsSpan(0, Math.Min(16, head.Length));
        yield return
        [
            e.Index.ToString(inv),
            $"0x{e.Offset:X}",
            e.Size.ToString(inv),
            afs.HasNameTable ? e.Name ?? "" : "",
            ByteStats.Hex(first),
            ByteStats.Ascii(first),
            ByteStats.Entropy(head).ToString("0.0000", inv),
        ];
    }
}

int AfsCsv(string path, string output)
{
    using var afs = AfsArchive.Open(path);
    using var w = new StreamWriter(output, false, new UTF8Encoding(true));
    w.WriteLine(CsvLine(["indice", "offset", "tamanho", "nome", "hex16", "ascii16", "entropia"]));
    int n = 0;
    foreach (var row in InventoryRows(afs)) { w.WriteLine(CsvLine(row)); n++; }
    Console.WriteLine($"{n} linhas gravadas em {output}");
    return 0;
}

// Compara o leitor C# com o inventário gerado de forma independente (tools/scripts/afs.ps1).
int AfsVerify(string path, string csvPath)
{
    using var afs = AfsArchive.Open(path);
    var expected = File.ReadAllLines(csvPath).Skip(1).Where(l => l.Length > 0).Select(ParseCsvLine).ToList();
    string[] cols = ["indice", "offset", "tamanho", "nome", "hex16", "ascii16", "entropia"];

    int diffs = 0, rows = 0;
    if (expected.Count != afs.Entries.Count)
    {
        Console.WriteLine($"contagem diferente: csv {expected.Count}, afs {afs.Entries.Count}");
        diffs++;
    }
    foreach (var (actual, i) in InventoryRows(afs).Select((r, i) => (r, i)))
    {
        if (i >= expected.Count) break;
        rows++;
        var exp = expected[i];
        for (int c = 0; c < cols.Length; c++)
        {
            string ev = c < exp.Length ? exp[c] : "";
            bool same = cols[c] == "entropia"
                ? double.TryParse(ev, NumberStyles.Float, inv, out var ed)
                  && Math.Abs(ed - double.Parse(actual[c], inv)) <= 0.00011
                : ev == actual[c];
            if (same) continue;
            if (++diffs <= 50) Console.WriteLine($"linha {i}, {cols[c]}: csv '{ev}' / leitor '{actual[c]}'");
        }
    }
    Console.WriteLine($"{rows} linhas comparadas, {diffs} diferenças");
    return diffs == 0 ? 0 : 3;
}

int AfsExtract(string path, string outDir, string[] indexArgs)
{
    if (indexArgs.Length > MaxExtractPerCall)
    {
        Console.Error.WriteLine($"no máximo {MaxExtractPerCall} entradas por chamada (sem extração em lote nesta fase)");
        return 1;
    }
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full))
    {
        Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git; recursos do jogo não podem ir para o repositório");
        return 1;
    }
    Directory.CreateDirectory(full);

    using var afs = AfsArchive.Open(path);
    var stem = Path.GetFileNameWithoutExtension(path);
    foreach (var a in indexArgs)
    {
        if (!int.TryParse(a, NumberStyles.None, inv, out int idx) || idx < 0 || idx >= afs.Entries.Count)
        {
            Console.Error.WriteLine($"índice inválido: {a}");
            return 1;
        }
        var e = afs.Entries[idx];
        var file = Path.Combine(full, $"{stem}_{idx:D4}_{e.Name ?? "x"}.bin");
        using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            afs.CopyEntry(e, fs);
        using var check = File.OpenRead(file);
        var sha = Convert.ToHexString(SHA256.HashData(check));
        Console.WriteLine($"#{idx} {e.Name} {e.Size} bytes -> {file} sha256 {sha}");
    }
    return 0;
}

int AfsClassify(string path)
{
    using var afs = AfsArchive.Open(path);
    var kinds = afs.Entries.Select(e => (e, kind: Classifier.Classify(afs.ReadHead(e, 0x1000)))).ToList();

    Console.WriteLine($"{path}: {afs.Entries.Count} entradas");
    foreach (var g in kinds.GroupBy(k => k.kind).OrderByDescending(g => g.Count()))
        Console.WriteLine($"  {g.Key,-8} {g.Count(),5} entradas {g.Sum(k => (long)k.e.Size),12} bytes");

    Console.WriteLine("por categoria:");
    foreach (var g in kinds.GroupBy(k => k.e.Name ?? "(sem nome)").OrderBy(g => g.Key, StringComparer.Ordinal))
        Console.WriteLine($"  {g.Key,-10} " + string.Join(", ", g.GroupBy(k => k.kind).Select(k => $"{k.Key} {k.Count()}")));

    var unknown = kinds.Where(k => k.kind == ContentKind.Unknown).ToList();
    if (unknown.Count > 0)
    {
        Console.WriteLine("não classificadas (até 30):");
        foreach (var (e, _) in unknown.Take(30))
            Console.WriteLine($"  #{e.Index} {e.Name} {e.Size} bytes  {ByteStats.Hex(afs.ReadHead(e, 16))}");
    }
    return 0;
}

// Lê o cabeçalho de cada IGB (de um .igb solto ou de todas as entradas IGB de um AFS)
// e confere se os buffers declarados cabem no tamanho real.
int IgbHeaders(string path)
{
    var items = new List<(string id, uint size, IgbHeader h)>();
    if (path.EndsWith(".igb", StringComparison.OrdinalIgnoreCase))
    {
        var data = File.ReadAllBytes(path);
        if (!IgbHeader.TryRead(data, out var h)) { Console.WriteLine("não é IGB"); return 3; }
        items.Add((Path.GetFileName(path), (uint)data.Length, h!));
    }
    else
    {
        using var afs = AfsArchive.Open(path);
        foreach (var e in afs.Entries)
            if (IgbHeader.TryRead(afs.ReadHead(e, IgbHeader.Size), out var h))
                items.Add(($"#{e.Index} {e.Name}", e.Size, h!));
    }

    Console.WriteLine($"{items.Count} IGB");
    if (items.Count == 0) return 3;
    foreach (var g in items.GroupBy(i => (i.h.Version, i.h.LittleEndian, flags: i.h.VersionFlags & 0xFFFF0000)))
        Console.WriteLine($"  versão {g.Key.Version}, {(g.Key.LittleEndian ? "LE" : "BE")}, flags 0x{g.Key.flags:X8}: {g.Count()}");

    var exact = items.Count(i => i.h.DeclaredSize == i.size);
    var fits = items.Count(i => i.h.DeclaredSize <= i.size);
    Console.WriteLine($"  soma dos buffers == tamanho: {exact}; <= tamanho: {fits}; > tamanho: {items.Count - fits}");
    foreach (var (id, size, h) in items.Where(i => i.h.DeclaredSize > i.size).Take(20))
        Console.WriteLine($"    excede: {id} tamanho {size} declarado {h.DeclaredSize}");
    var slack = items.Where(i => i.h.DeclaredSize < i.size).Select(i => i.size - i.h.DeclaredSize).ToList();
    if (slack.Count > 0)
        Console.WriteLine($"  sobra além dos buffers: mín {slack.Min()} máx {slack.Max()} (em {slack.Count} arquivos)");

    Console.WriteLine($"  metacampos: {items.Min(i => i.h.MetaFieldCount)}–{items.Max(i => i.h.MetaFieldCount)}; " +
        $"metaobjetos: {items.Min(i => i.h.MetaObjectCount)}–{items.Max(i => i.h.MetaObjectCount)}; " +
        $"objetos: {items.Min(i => i.h.ObjectCount)}–{items.Max(i => i.h.ObjectCount)}");
    foreach (var (id, size, h) in items.Take(5))
        Console.WriteLine($"  {id}: {size} B, entradas {h.EntryCount}/{h.EntryBufferSize}, metaobj {h.MetaObjectCount}/{h.MetaObjectBufferSize}, " +
            $"obj {h.ObjectCount}/{h.ObjectBufferSize}, memref {h.MemoryRefCount}/{h.MemoryRefBufferSize}, metacampo {h.MetaFieldCount}/{h.MetaFieldBufferSize}");
    return 0;
}

// Percorre as seções de cada IGB e conta em quantos a última seção termina exatamente no fim do arquivo.
int IgbLayoutCheck(string path)
{
    var results = new List<(string id, long trailing, string? error, List<IgbSection>? sections)>();

    void Check(string id, byte[] data)
    {
        if (!IgbHeader.TryRead(data, out var h)) return;
        try
        {
            var s = IgbLayout.Walk(data, h!);
            results.Add((id, IgbLayout.Trailing(data, s), null, s));
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            results.Add((id, -1, ex.Message, null));
        }
    }

    if (path.EndsWith(".igb", StringComparison.OrdinalIgnoreCase))
        Check(Path.GetFileName(path), File.ReadAllBytes(path));
    else
    {
        using var afs = AfsArchive.Open(path);
        foreach (var e in afs.Entries)
        {
            var head = afs.ReadHead(e, IgbHeader.Size);
            if (!IgbHeader.TryRead(head, out _)) continue;
            using var ms = new MemoryStream((int)e.Size);
            afs.CopyEntry(e, ms);
            Check($"#{e.Index} {e.Name}", ms.ToArray());
        }
    }

    int exact = results.Count(r => r.error == null && r.trailing == 0);
    var errors = results.Where(r => r.error != null).ToList();
    var trailing = results.Where(r => r.error == null && r.trailing != 0).ToList();
    Console.WriteLine($"{results.Count} IGB: layout fecha exatamente em {exact}; sobra bytes em {trailing.Count}; erro em {errors.Count}");
    foreach (var g in trailing.GroupBy(r => r.trailing).OrderByDescending(g => g.Count()).Take(10))
        Console.WriteLine($"  sobra {g.Key} B: {g.Count()} (ex.: {string.Join(", ", g.Take(3).Select(r => r.id))})");
    foreach (var (id, _, error, _) in errors.Take(20))
        Console.WriteLine($"  erro {id}: {error}");
    var sample = results.FirstOrDefault(r => r.sections != null);
    if (sample.sections != null)
    {
        Console.WriteLine($"seções de {sample.id}:");
        foreach (var s in sample.sections) Console.WriteLine($"  0x{s.Offset:X8} {s.Size,10}  {s.Name}");
    }
    return errors.Count == 0 && trailing.Count == 0 ? 0 : 3;
}

// Histograma das classes do Alchemy presentes nos objetos dos IGB, com checagem de contagem e de fim de buffer.
int IgbTypesReport(string path)
{
    var files = new List<(string id, string cat, IgbHeader h, IgbTypeInfo t)>();
    var errors = new List<string>();

    void Load(string id, string cat, byte[] data)
    {
        if (!IgbHeader.TryRead(data, out var h)) return;
        try { files.Add((id, cat, h!, IgbTypes.Read(data, h!))); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException)
        { errors.Add($"{id}: {ex.Message}"); }
    }

    if (path.EndsWith(".igb", StringComparison.OrdinalIgnoreCase))
        Load(Path.GetFileName(path), "(solto)", File.ReadAllBytes(path));
    else
    {
        using var afs = AfsArchive.Open(path);
        foreach (var e in afs.Entries)
        {
            if (!IgbHeader.TryRead(afs.ReadHead(e, IgbHeader.Size), out _)) continue;
            using var ms = new MemoryStream((int)e.Size);
            afs.CopyEntry(e, ms);
            Load($"#{e.Index} {e.Name}", e.Name ?? "?", ms.ToArray());
        }
    }

    int countOk = files.Count(f => f.t.Objects.Count == f.h.ObjectCount);
    int exact = files.Count(f => f.t.ObjectBufferExact);
    int typeOk = files.Count(f => f.t.Objects.All(o => o.TypeIndex >= 0 && o.TypeIndex < f.t.MetaObjects.Count));
    Console.WriteLine($"{files.Count} IGB lidos, {errors.Count} com erro");
    Console.WriteLine($"  nº de objetos == cabeçalho: {countOk}; buffer de objetos termina exato: {exact}; tipos válidos: {typeOk}");
    foreach (var err in errors.Take(20)) Console.WriteLine($"  erro {err}");
    foreach (var f in files.Where(f => f.t.Objects.Count != f.h.ObjectCount || !f.t.ObjectBufferExact).Take(10))
        Console.WriteLine($"  divergente {f.id}: objetos lidos {f.t.Objects.Count}, cabeçalho {f.h.ObjectCount}, exato {f.t.ObjectBufferExact}");

    string TypeName((string id, string cat, IgbHeader h, IgbTypeInfo t) f, IgbObjectRecord o) =>
        o.TypeIndex >= 0 && o.TypeIndex < f.t.MetaObjects.Count ? f.t.MetaObjects[o.TypeIndex].Name : $"?{o.TypeIndex}";

    var all = files.SelectMany(f => f.t.Objects.Select(o => (f.id, f.cat, type: TypeName(f, o), (long)o.Size))).ToList();
    Console.WriteLine($"classes nos objetos ({all.Select(a => a.type).Distinct().Count()} distintas, {all.Count} objetos):");
    foreach (var g in all.GroupBy(a => a.type).OrderByDescending(g => g.Count()).Take(80))
        Console.WriteLine($"  {g.Key,-40} {g.Count(),7} obj em {g.Select(a => a.id).Distinct().Count(),5} arquivos {g.Sum(a => a.Item4),11} B");

    Console.WriteLine("classes mais frequentes por categoria:");
    foreach (var c in files.GroupBy(f => f.cat).OrderBy(c => c.Key, StringComparer.Ordinal))
    {
        var top = c.SelectMany(f => f.t.Objects.Select(o => TypeName(f, o)))
            .GroupBy(t => t).OrderByDescending(g => g.Count()).Take(12).Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"  {c.Key} ({c.Count()} arquivos): {string.Join(", ", top)}");
    }

    var mf = files.FirstOrDefault().t?.MetaFields;
    if (mf != null)
        Console.WriteLine($"metacampos do primeiro arquivo: {string.Join(", ", mf.Select(m => m.Name))}");
    return errors.Count == 0 ? 0 : 3;
}

static bool InsideGitRepo(string dir)
{
    for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, ".git")))
            return true;
    return false;
}

static string CsvLine(IEnumerable<string> fields) =>
    string.Join(',', fields.Select(f => "\"" + f.Replace("\"", "\"\"") + "\""));

static string[] ParseCsvLine(string line)
{
    var fields = new List<string>();
    var sb = new StringBuilder();
    bool quoted = false;
    for (int i = 0; i < line.Length; i++)
    {
        char ch = line[i];
        if (quoted)
        {
            if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
            else if (ch == '"') quoted = false;
            else sb.Append(ch);
        }
        else if (ch == '"') quoted = true;
        else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
        else sb.Append(ch);
    }
    fields.Add(sb.ToString());
    return fields.ToArray();
}
