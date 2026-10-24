using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DW4.Formats.Afs;
using DW4.Formats.Analysis;
using DW4.Formats.Igb;
using DW4.Formats.Lua;
using System.Buffers.Binary;

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
        "igb-check" when args.Length == 2 => IgbCheck(args[1]),
        "igb-dump" when args.Length is 3 or 4 => IgbDump(args[1], args[2], args.Length == 4 ? args[3] : null),
        "igb-lua" when args.Length == 2 => IgbLua(args[1]),
        "lua-scan" when args.Length is 2 or 3 => LuaScan(args[1], args.Length == 3 ? args[2] : null),
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
          dw4 igb-check    <arquivo.afs | arquivo.igb>
          dw4 igb-dump     <arquivo.afs> <indice> [classe]   (ou <arquivo.igb> - [classe])
          dw4 igb-lua      <arquivo.afs>
          dw4 lua-scan     <arquivo.afs> [pasta-de-trabalho]   (com pasta: grava listagens fora do repositório)
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

// Itera os IGB de um .igb solto ou de um AFS, devolvendo (id, categoria, bytes).
IEnumerable<(string id, string cat, byte[] data)> IgbSources(string path)
{
    if (path.EndsWith(".igb", StringComparison.OrdinalIgnoreCase))
    {
        yield return (Path.GetFileName(path), "(solto)", File.ReadAllBytes(path));
        yield break;
    }
    using var afs = AfsArchive.Open(path);
    foreach (var e in afs.Entries)
    {
        if (!IgbHeader.TryRead(afs.ReadHead(e, IgbHeader.Size), out _)) continue;
        using var ms = new MemoryStream((int)e.Size);
        afs.CopyEntry(e, ms);
        yield return ($"#{e.Index} {e.Name}", e.Name ?? "?", ms.ToArray());
    }
}

// Lê o diretório completo (entradas, índice, objetos, memória) de cada IGB e confere se tudo fecha.
int IgbCheck(string path)
{
    int files = 0, ok = 0;
    long objects = 0, blocks = 0;
    var failures = new List<string>();
    foreach (var (id, _, data) in IgbSources(path))
    {
        files++;
        try
        {
            var f = IgbFile.Read(data);
            objects += f.Slots.Count(s => s is IgbObject);
            blocks += f.Slots.Count(s => s is IgbMemoryBlock);
            if (f.Problems.Count == 0) ok++;
            else failures.Add($"{id}: {string.Join("; ", f.Problems.Take(3))}");
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            failures.Add($"{id}: {ex.GetType().Name}: {ex.Message}");
        }
    }
    Console.WriteLine($"{files} IGB: diretório completo fecha em {ok}; falhas {failures.Count}; {objects} objetos, {blocks} blocos de memória");
    foreach (var fl in failures.Take(20)) Console.WriteLine($"  {fl}");
    return failures.Count == 0 ? 0 : 3;
}

string FormatField(IgbFile f, IgbFieldValue v)
{
    bool le = f.Header.LittleEndian;
    var raw = v.Raw;
    float F(int at) => le ? BinaryPrimitives.ReadSingleLittleEndian(raw.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(raw.AsSpan(at));
    string Floats(int n) => string.Join(" ", Enumerable.Range(0, n).Select(i => F(4 * i).ToString("0.####", inv)));
    switch (v.Desc.ShortName)
    {
        case "ObjectRef" or "MemoryRef":
        {
            int r = v.AsInt32(le);
            if (r < 0 || r >= f.Slots.Count) return r == -1 ? "nulo" : $"#{r}?";
            var t = f.Slots[r];
            return t is IgbMemoryBlock mb
                ? $"-> #{r} memória {mb.TypeName} {mb.Size} B"
                : $"-> #{r} {t.TypeName}";
        }
        case "String": return $"\"{v.Text}\"";
        case "Float" when raw.Length >= 4: return F(0).ToString("0.####", inv);
        case "Vec2f" when raw.Length >= 8: return $"({Floats(2)})";
        case "Vec3f" when raw.Length >= 12: return $"({Floats(3)})";
        case "Vec4f" when raw.Length >= 16: return $"({Floats(4)})";
        case "Int" or "Enum" or "UnsignedInt" or "Long" when raw.Length == 4: return v.AsInt32(le).ToString(inv);
        case "Bool" or "Char" or "UnsignedChar" when raw.Length == 1: return raw[0].ToString(inv);
        case "Short" or "UnsignedShort" when raw.Length == 2:
            return (le ? BinaryPrimitives.ReadUInt16LittleEndian(raw) : BinaryPrimitives.ReadUInt16BigEndian(raw)).ToString(inv);
        default: return $"[{raw.Length} B] {ByteStats.Hex(raw.AsSpan(0, Math.Min(32, raw.Length)))}";
    }
}

byte[] MemoryOf(byte[] data, IgbMemoryBlock mb) => data.AsSpan((int)mb.FileOffset, mb.Size).ToArray();

// Mostra objetos de um IGB (opcionalmente só de uma classe, incluindo subclasses), com campos e alvos de referência.
int IgbDump(string path, string which, string? cls)
{
    byte[] data;
    if (which == "-") data = File.ReadAllBytes(path);
    else
    {
        using var afs = AfsArchive.Open(path);
        if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[idx], ms);
        data = ms.ToArray();
    }
    var f = IgbFile.Read(data);
    Console.WriteLine($"{f.Slots.Count} slots ({f.Slots.Count(s => s is IgbObject)} objetos); problemas: {f.Problems.Count}");
    int shown = 0;
    foreach (var o in f.Slots.OfType<IgbObject>())
    {
        if (cls != null)
        {
            int mo = f.MetaObjectNames.ToList().IndexOf(o.TypeName);
            if (mo < 0 || !f.IsA(mo, cls)) continue;
        }
        if (++shown > 40) { Console.WriteLine("... (limite de 40 objetos)"); break; }
        Console.WriteLine($"#{o.Index} {o.TypeName} @0x{o.FileOffset:X}");
        foreach (var v in o.Fields)
        {
            Console.WriteLine($"    {v.Desc.ShortName}@{v.Desc.Slot} = {FormatField(f, v)}");
            if (v.Desc.ShortName == "MemoryRef")
            {
                int r = v.AsInt32(f.Header.LittleEndian);
                if (r >= 0 && r < f.Slots.Count && f.Slots[r] is IgbMemoryBlock mb && mb.Size > 0)
                {
                    var head = MemoryOf(data, mb).AsSpan(0, Math.Min(16, mb.Size));
                    Console.WriteLine($"        {ByteStats.Hex(head)}  {ByteStats.Ascii(head)}");
                }
            }
        }
    }
    return 0;
}

// Procura os scripts referenciados por objetos beLuaData* e classifica o conteúdo (bytecode Lua, texto ou binário).
int IgbLua(string path)
{
    int files = 0, luaObjs = 0;
    var kinds = new Dictionary<string, int>();
    var samples = new List<string>();
    var names = new SortedSet<string>(StringComparer.Ordinal);
    var seenBlocks = new HashSet<(string, int)>();
    foreach (var (id, _, data) in IgbSources(path))
    {
        IgbFile f;
        try { f = IgbFile.Read(data); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException) { continue; }
        bool any = false;
        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName.StartsWith("beLua", StringComparison.Ordinal)))
        {
            any = true;
            luaObjs++;
            foreach (var v in o.Fields)
            {
                if (v.Text is { Length: > 0 } t && names.Count < 400) names.Add($"{o.TypeName}: {t}");
                if (v.Desc.ShortName != "MemoryRef") continue;
                int r = v.AsInt32(f.Header.LittleEndian);
                if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb || mb.Size == 0) continue;
                if (!seenBlocks.Add((id, r))) continue;
                var mem = MemoryOf(data, mb);
                string kind = mem.Length >= 5 && mem[0] == 0x1B && mem[1] == (byte)'L' && mem[2] == (byte)'u' && mem[3] == (byte)'a'
                    ? $"bytecode Lua (versão 0x{mem[4]:X2})"
                    : mem.Count(b => b is 9 or 10 or 13 || (b >= 32 && b < 127)) >= mem.Length * 0.95 ? "texto"
                    : "binário";
                kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                if (samples.Count < 12)
                    samples.Add($"{id} #{o.Index} {o.TypeName} -> #{r} {mb.TypeName} {mb.Size} B [{kind}] {ByteStats.Hex(mem.AsSpan(0, Math.Min(16, mem.Length)))}  {ByteStats.Ascii(mem.AsSpan(0, Math.Min(16, mem.Length)))}");
            }
        }
        if (any) files++;
    }
    Console.WriteLine($"{luaObjs} objetos beLua* em {files} arquivos");
    foreach (var (k, n) in kinds.OrderByDescending(k => k.Value)) Console.WriteLine($"  blocos {k}: {n}");
    Console.WriteLine("amostras:");
    foreach (var s in samples) Console.WriteLine($"  {s}");
    Console.WriteLine($"strings em objetos beLua* ({names.Count}, até 60):");
    foreach (var n in names.Take(60)) Console.WriteLine($"  {n}");
    return 0;
}

// Lê todos os chunks Lua 4.0 referenciados por beLuaDataObject, valida a estrutura e resume o uso de nomes globais.
// Com pasta de trabalho, grava uma listagem (estilo luac -l) por chunk distinto; nunca dentro de um repositório.
int LuaScan(string path, string? outDir)
{
    string? full = null;
    if (outDir != null)
    {
        full = Path.GetFullPath(outDir);
        if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
        Directory.CreateDirectory(full);
    }

    int chunks = 0, failed = 0, functions = 0, instructions = 0, written = 0;
    var globals = new Dictionary<string, int>(StringComparer.Ordinal);
    var methods = new Dictionary<string, int>(StringComparer.Ordinal);
    var opcodes = new int[Lua40Chunk.OpNames.Length + 1];
    var failures = new List<string>();
    var seenHashes = new HashSet<string>();

    foreach (var (id, _, data) in IgbSources(path))
    {
        IgbFile f;
        try { f = IgbFile.Read(data); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException) { continue; }

        // nomes: beLuaData (String = nome do script, ObjectRef = beLuaDataObject)
        var namesByTarget = new Dictionary<int, List<string>>();
        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beLuaData"))
        {
            var nm = o.Fields.FirstOrDefault(v => v.Text != null)?.Text ?? "?";
            var rf = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "ObjectRef");
            if (rf == null) continue;
            int t = rf.AsInt32(f.Header.LittleEndian);
            if (!namesByTarget.TryGetValue(t, out var list)) namesByTarget[t] = list = [];
            list.Add(nm);
        }

        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beLuaDataObject"))
        {
            foreach (var v in o.Fields.Where(v => v.Desc.ShortName == "MemoryRef"))
            {
                int r = v.AsInt32(f.Header.LittleEndian);
                if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb || mb.Size == 0) continue;
                var mem = MemoryOf(data, mb);
                if (!Lua40Chunk.LooksLikeChunk(mem)) continue;
                chunks++;
                Lua40Function main;
                try { main = Lua40Chunk.Read(mem); }
                catch (InvalidDataException ex)
                {
                    failed++;
                    if (failures.Count < 20) failures.Add($"{id} #{o.Index}: {ex.Message}");
                    continue;
                }
                foreach (var fn in Lua40Chunk.All(main))
                {
                    functions++;
                    foreach (var ins in fn.Code)
                    {
                        instructions++;
                        int op = Lua40Chunk.Op(ins);
                        opcodes[Math.Min(op, opcodes.Length - 1)]++;
                        int u = Lua40Chunk.ArgU(ins);
                        if ((op == 12 || op == 19) && u < fn.Strings.Count && fn.Strings[u] is { } g)
                            globals[g] = globals.GetValueOrDefault(g) + 1;
                        if ((op == 14 || op == 16) && u < fn.Strings.Count && fn.Strings[u] is { } m)
                            methods[m] = methods.GetValueOrDefault(m) + 1;
                    }
                }
                if (full != null)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(mem));
                    if (!seenHashes.Add(hash)) continue;
                    var names = namesByTarget.GetValueOrDefault(o.Index) ?? ["sem_nome"];
                    var safe = string.Concat(names[0].Select(c => char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_'));
                    var file = Path.Combine(full, $"{safe}.{hash[..8]}.lst");
                    using var w = new StreamWriter(file, false, new UTF8Encoding(false));
                    w.WriteLine($"-- origem: {path} {id}, objeto #{o.Index}, bloco #{r} ({mb.Size} B), sha256 {hash}");
                    w.WriteLine($"-- nomes: {string.Join(", ", names)}");
                    Lua40Chunk.List(main, w);
                    written++;
                }
            }
        }
    }

    Console.WriteLine($"{chunks} chunks Lua 4.0: {chunks - failed} lidos sem erro, {failed} com erro; {functions} funções, {instructions} instruções");
    foreach (var fl in failures) Console.WriteLine($"  erro {fl}");
    if (full != null) Console.WriteLine($"{written} listagens distintas gravadas em {full}");
    Console.WriteLine("opcodes:");
    Console.WriteLine("  " + string.Join(", ", Enumerable.Range(0, opcodes.Length).Where(i => opcodes[i] > 0)
        .Select(i => $"{(i < Lua40Chunk.OpNames.Length ? Lua40Chunk.OpNames[i] : "inválido")} {opcodes[i]}")));
    Console.WriteLine($"globais lidas/escritas ({globals.Count} distintas, até 150 mais usadas):");
    foreach (var (k, n) in globals.OrderByDescending(g => g.Value).Take(150)) Console.WriteLine($"  {n,6}  {k}");
    Console.WriteLine($"campos/métodos acessados ({methods.Count} distintos, até 60):");
    foreach (var (k, n) in methods.OrderByDescending(g => g.Value).Take(60)) Console.WriteLine($"  {n,6}  {k}");
    return failed == 0 ? 0 : 3;
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
