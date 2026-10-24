using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DW4.Formats.Afs;
using DW4.Formats.Analysis;
using DW4.Formats.Igb;
using DW4.Formats.Images;
using DW4.Formats.Export;
using System.Text.Json.Nodes;
using DW4.Formats.Lua;
using DW4.Formats.Sofdec;
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
        "igb-export" when args.Length == 4 => IgbExport(args[1], args[2], args[3]),
        "igb-anim" when args.Length is 2 or 3 => IgbAnim(args[1], args.Length == 3 ? args[2] : null),
        "igb-mem" when args.Length is 4 or 5 => IgbMem(args[1], args[2], args[3], args.Length == 5 ? args[4] : "hex"),
        "igb-tree" when args.Length is 3 or 4 => IgbTree(args[1], args[2], args.Length == 4 ? int.Parse(args[3], inv) : 6),
        "igb-gltf" when args.Length >= 4 => IgbGltf(args[1], args[2], args[3], args[4..]),
        "igb-survey" when args.Length == 3 => IgbSurvey(args[1], args[2]),
        "sfd-demux" when args.Length == 4 => SfdDemux(args[1], args[2], args[3]),
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
          dw4 igb-export   <arquivo.afs> <indice> <pasta-de-trabalho>   (OBJ + PNG + relatório, fora do repositório)
          dw4 igb-anim     <arquivo.afs> [indice]   (decodifica as animações Enbaya; sem índice, resume o arquivo todo)
          dw4 igb-mem      <arquivo.afs> <indice> <slot> [hex|u8|i32|u32|f32|hist]   (conteúdo de um bloco de memória, ou de um igDataList)
          dw4 igb-tree     <arquivo.afs> <indice> [profundidade]   (grafo de cena com nomes de nós)
          dw4 igb-gltf     <arquivo.afs> <indice> <pasta-de-trabalho> [--quat=conj] [--trans=rest] [--anim=N]
                           (personagem com esqueleto, pele e animações em glTF, fora do repositório)
          dw4 igb-survey   <arquivo.afs> <saida.csv>   (uma linha por IGB: contagens de classes-chave e nomes; fora do repositório)
          dw4 sfd-demux    <arquivo.afs> <indice> <pasta-de-trabalho>   (separa áudio e vídeo de um Sofdec, fora do repositório)
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

// Exporta um item IGB de um AFS para OBJ/MTL + PNG numa pasta de trabalho fora de qualquer repositório Git.
// É uma conversão de amostra para inspeção visual; o relatório lista o que não foi decodificado.
int IgbExport(string path, string which, string outDir)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    var entry = afs.Entries[idx];
    using var ms = new MemoryStream();
    afs.CopyEntry(entry, ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    var scene = IgbScene.Read(f, data);

    var safeName = string.Concat((entry.Name ?? "sem_nome").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    var dir = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_{safeName}");
    Directory.CreateDirectory(dir);
    var report = new StringBuilder();
    report.AppendLine($"{path} #{idx} ({entry.Name}, {entry.Size} B)");
    report.AppendLine($"slots: {f.Slots.Count}; problemas do leitor: {f.Problems.Count}; igTransform visitados: {scene.TransformCount}");
    foreach (var n in scene.Notes) report.AppendLine(n);

    // Imagens indexadas de 8 bits, gravadas com a primeira linha no topo.
    var texture = new Dictionary<int, string>();
    foreach (var img in scene.Images.Values)
    {
        string pal = img.Palette.Length >= 16 ? ByteStats.Hex(img.Palette.AsSpan(0, 16)) : "-";
        int maxAlpha = 0;
        for (int i = 3; i < img.Palette.Length; i += 4) maxAlpha = Math.Max(maxAlpha, img.Palette[i]);
        report.AppendLine($"imagem #{img.Index} \"{img.Name}\" {img.Width}x{img.Height} formato {img.PixelFormat} {img.BitsPerPixel} bpp, " +
            $"{img.Pixels.Length} B; paleta formato {img.PaletteFormat} ×{img.PaletteCount} ({img.Palette.Length} B), alfa máx {maxAlpha}, início {pal}");
        if (img.PixelFormat == 14)
        {
            // dois layouts candidatos para o formato comprimido; o de PC fica como textura do OBJ até a inspeção decidir
            foreach (var (gc, suffix) in new[] { (false, "pc"), (true, "gc") })
            {
                var blocks = IgbScene.DecodeBlockCompressed(img, gc);
                if (blocks == null) { report.AppendLine("    formato 14 com tamanho inesperado"); break; }
                var name = $"img{img.Index:D4}_{suffix}.png";
                using (var fs = File.Create(Path.Combine(dir, name))) Png.WriteRgba(fs, img.Width, img.Height, blocks);
                if (!gc) texture[img.Index] = name;
            }
            continue;
        }
        var rgba = IgbScene.IndexedToRgba(img);
        if (rgba == null) { report.AppendLine("    não decodificada (só 8 bpp com paleta e formato 14 por enquanto)"); continue; }
        var file = $"img{img.Index:D4}.png";
        using (var fs = File.Create(Path.Combine(dir, file))) Png.WriteRgba(fs, img.Width, img.Height, rgba);
        texture[img.Index] = file;
    }

    // Geometria: um objeto OBJ por igGeometryAttr. UV sem inversão: a imagem já sai com o topo em cima e v = 0 é a base,
    // como no OBJ (hipótese coerente com as linhas da imagem guardadas de baixo para cima; conferir no modelo).
    // Um OBJ com tudo e, quando há mais de uma raiz de cena, um OBJ por raiz (cena_NNNN.obj) para comparar camadas.
    var mtl = new StringBuilder();
    foreach (var (index, file) in texture)
    {
        mtl.AppendLine($"newmtl img{index:D4}");
        mtl.AppendLine("Kd 1 1 1");
        mtl.AppendLine($"map_Kd {file}");
    }
    var writers = new Dictionary<int, (StringBuilder Text, int BaseVertex)>();
    void Write(int key, IgbMeshPart part)
    {
        if (!writers.TryGetValue(key, out var w))
        {
            w = (new StringBuilder(), 1);
            w.Text.AppendLine("# exportado por dw4 igb-export; amostra de inspeção, não é recurso final");
            w.Text.AppendLine("mtllib modelo.mtl");
        }
        var obj = w.Text;
        int baseVertex = w.BaseVertex;
        obj.AppendLine($"o cena{part.SceneRoot}_geom{part.GeometryIndex:D4}");
        if (texture.ContainsKey(part.ImageIndex)) obj.AppendLine($"usemtl img{part.ImageIndex:D4}");
        foreach (var (x, y, z) in part.Positions) obj.AppendLine(FormattableString.Invariant($"v {x} {y} {z}"));
        bool uv = part.TexCoords.Count == part.Positions.Count, nrm = part.Normals.Count == part.Positions.Count;
        if (uv) foreach (var (u, v) in part.TexCoords) obj.AppendLine(FormattableString.Invariant($"vt {u} {v}"));
        if (nrm) foreach (var (x, y, z) in part.Normals) obj.AppendLine(FormattableString.Invariant($"vn {x} {y} {z}"));
        string V(int i)
        {
            var s = (i + baseVertex).ToString(inv);
            return uv && nrm ? $"{s}/{s}/{s}" : uv ? $"{s}/{s}" : nrm ? $"{s}//{s}" : s;
        }
        for (int t = 0; t + 2 < part.Triangles.Count; t += 3)
            obj.AppendLine($"f {V(part.Triangles[t])} {V(part.Triangles[t + 1])} {V(part.Triangles[t + 2])}");
        writers[key] = (obj, baseVertex + part.Positions.Count);
    }
    int exported = 0;
    var roots = scene.Parts.Select(p => p.SceneRoot).Distinct().ToList();
    foreach (var part in scene.Parts)
    {
        report.AppendLine($"geometria #{part.GeometryIndex} (cena {part.SceneRoot}): primitiva {part.PrimitiveType}, {part.VertexCount} vértices, formato 0x{part.VertexFormat:X8}, " +
            $"{part.Triangles.Count / 3} triângulos, imagem {(part.ImageIndex >= 0 ? $"#{part.ImageIndex}" : "nenhuma")}");
        foreach (var n in part.Notes) report.AppendLine($"    {n}");
        if (part.Positions.Count == 0 || part.Triangles.Count == 0) continue;
        exported++;
        Write(int.MinValue, part);
        if (roots.Count > 1) Write(part.SceneRoot, part);
    }
    foreach (var (key, w) in writers)
        File.WriteAllText(Path.Combine(dir, key == int.MinValue ? "modelo.obj" : $"cena_{key:D5}.obj"), w.Text.ToString());
    File.WriteAllText(Path.Combine(dir, "modelo.mtl"), mtl.ToString());
    File.WriteAllText(Path.Combine(dir, "relatorio.txt"), report.ToString());
    Console.Write(report);
    Console.WriteLine($"{exported} de {scene.Parts.Count} geometrias e {texture.Count} imagens gravadas em {dir}");
    return 0;
}

// Decodifica todos os igEnbayaAnimationSource e confere se cada fluxo foi consumido exatamente.
// Com índice, mostra também o cabeçalho de cada fluxo e as animações (igAnimation) do arquivo.
int IgbAnim(string path, string? which)
{
    int sources = 0, exact = 0, failed = 0, files = 0;
    var rates = new Dictionary<int, int>();
    var failures = new List<string>();
    var inexact = new List<string>();
    var extraHist = new Dictionary<int, int>();
    using var afs = AfsArchive.Open(path);
    IEnumerable<AfsEntry> entries = afs.Entries;
    if (which != null)
    {
        if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
        entries = [afs.Entries[idx]];
    }
    foreach (var e in entries)
    {
        if (!IgbHeader.TryRead(afs.ReadHead(e, IgbHeader.Size), out _)) continue;
        using var ms = new MemoryStream((int)e.Size);
        afs.CopyEntry(e, ms);
        var data = ms.ToArray();
        var f = IgbFile.Read(data);
        bool le = f.Header.LittleEndian;
        var srcs = f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igEnbayaAnimationSource")).ToList();
        if (srcs.Count == 0) continue;
        files++;
        if (which != null)
        {
            foreach (var a in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igAnimation")).Take(60))
                Console.WriteLine($"#{a.Index} {a.TypeName} " + string.Join("; ", a.Fields.Select(v => $"{v.Desc.ShortName}@{v.Desc.Slot}={FormatField(f, v)}")));
        }
        foreach (var src in srcs)
        {
            sources++;
            var mf = src.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
            int r = mf?.AsInt32(le) ?? -1;
            if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb) { failed++; failures.Add($"#{e.Index} {e.Name} fonte #{src.Index}: sem bloco"); continue; }
            try
            {
                var clip = Enbaya.Decode(data.AsSpan((int)mb.FileOffset, mb.Size), le);
                rates[clip.SampleRate] = rates.GetValueOrDefault(clip.SampleRate) + 1;
                if (clip.ConsumedExactly) exact++;
                else
                {
                    extraHist[clip.ExtraSamples] = extraHist.GetValueOrDefault(clip.ExtraSamples) + 1;
                    if (inexact.Count < 15)
                        inexact.Add($"#{e.Index} {e.Name} fonte #{src.Index}: {clip.Duration.ToString("0.###", inv)} s, {clip.Poses.Count} amostras, " +
                            $"+{clip.ExtraSamples} passos possíveis nos deltas que sobraram; {string.Join(", ", clip.Consumption.Where(c => c.Split(": ")[1].Split('/') is var x && x[0] != x[1]))}");
                }
                if (which != null)
                    Console.WriteLine($"fonte #{src.Index}: bloco {mb.Size} B (cabeçalho+dados {clip.TotalLength} B), {clip.TrackCount} trilhas, {clip.SampleRate} Hz, " +
                        $"{clip.Duration.ToString("0.###", inv)} s, {clip.Poses.Count} amostras, quantização {clip.QuantizationError.ToString("G6", inv)}, " +
                        $"{(clip.ConsumedExactly ? "consumo exato" : "consumo inexato: " + string.Join(", ", clip.Consumption))}; " +
                        $"trilha 0 em t=0: {Pose(clip.Poses[0][0])}");
            }
            catch (InvalidDataException ex)
            {
                failed++;
                if (failures.Count < 15) failures.Add($"#{e.Index} {e.Name} fonte #{src.Index}: {ex.Message}; início {ByteStats.Hex(data.AsSpan((int)mb.FileOffset, Math.Min(32, mb.Size)))}");
            }
        }
    }
    Console.WriteLine($"{sources} fontes Enbaya em {files} arquivos: {exact} com consumo exato, {sources - exact - failed} inexatas, {failed} com erro");
    Console.WriteLine("taxas de amostragem: " + string.Join(", ", rates.OrderBy(k => k.Key).Select(k => $"{k.Key} Hz ×{k.Value}")));
    if (extraHist.Count > 0) Console.WriteLine("inexatas por passos a mais possíveis: " + string.Join(", ", extraHist.OrderBy(k => k.Key).Select(k => $"+{k.Key} ×{k.Value}")));
    foreach (var x in inexact) Console.WriteLine($"  inexata {x}");
    foreach (var x in failures) Console.WriteLine($"  erro {x}");
    return failed == 0 ? 0 : 3;

    string Pose(EnbayaPose p) => FormattableString.Invariant($"q=({p.Qx:0.###} {p.Qy:0.###} {p.Qz:0.###} {p.Qw:0.###}) t=({p.Tx:0.###} {p.Ty:0.###} {p.Tz:0.###})");
}

// Mostra um bloco de memória inteiro (ou o bloco de dados de um objeto que tenha um MemoryRef, como igIntList).
int IgbMem(string path, string which, string slotArg, string mode)
{
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    bool le = f.Header.LittleEndian;
    if (!int.TryParse(slotArg, NumberStyles.None, inv, out int slot) || slot >= f.Slots.Count) { Console.Error.WriteLine("slot inválido"); return 1; }
    var target = f.Slots[slot];
    if (target is IgbObject o)
    {
        var mr = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
        int r = mr?.AsInt32(le) ?? -1;
        Console.WriteLine($"#{slot} {o.TypeName}: " + string.Join("; ", o.Fields.Select(v => $"{v.Desc.ShortName}@{v.Desc.Slot}={FormatField(f, v)}")));
        if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock) { Console.WriteLine("sem bloco de memória"); return 0; }
        target = f.Slots[r];
    }
    var mb = (IgbMemoryBlock)target;
    var mem = MemoryOf(data, mb);
    Console.WriteLine($"#{mb.Index} {mb.TypeName}: {mem.Length} B ({mode})");
    int width = mode switch { "u8" or "hist" or "hex" => 1, "f32" or "i32" or "u32" => 4, _ => 1 };
    var values = new List<string>();
    var hist = new SortedDictionary<int, int>();
    for (int i = 0; i + width <= mem.Length; i += width)
    {
        switch (mode)
        {
            case "u8": values.Add(mem[i].ToString(inv)); break;
            case "hist": hist[mem[i]] = hist.GetValueOrDefault(mem[i]) + 1; break;
            case "i32": values.Add((le ? BinaryPrimitives.ReadInt32LittleEndian(mem.AsSpan(i)) : BinaryPrimitives.ReadInt32BigEndian(mem.AsSpan(i))).ToString(inv)); break;
            case "u32": values.Add((le ? BinaryPrimitives.ReadUInt32LittleEndian(mem.AsSpan(i)) : BinaryPrimitives.ReadUInt32BigEndian(mem.AsSpan(i))).ToString(inv)); break;
            case "f32": values.Add((le ? BinaryPrimitives.ReadSingleLittleEndian(mem.AsSpan(i)) : BinaryPrimitives.ReadSingleBigEndian(mem.AsSpan(i))).ToString("0.#####", inv)); break;
            default: values.Add(mem[i].ToString("X2", inv)); break;
        }
    }
    if (mode == "hist") Console.WriteLine(string.Join(" ", hist.Select(kv => $"{kv.Key}:{kv.Value}")));
    else for (int i = 0; i < values.Count; i += 16) Console.WriteLine($"{i * width,6:X}: {string.Join(" ", values.Skip(i).Take(16))}");
    return 0;
}

// Grafo de cena a partir dos igSceneInfo: classe, nome e quantas geometrias cada nó carrega e tem abaixo.
int IgbTree(string path, string which, int maxDepth)
{
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    bool le = f.Header.LittleEndian;
    IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
    IEnumerable<IgbObject> Refs(IgbObject o) => o.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>();
    IEnumerable<IgbObject> Items(IgbObject list)
    {
        var cnt = list.Fields.FirstOrDefault(v => v.Desc.ShortName == "Int");
        var mr = list.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
        if (cnt == null || mr == null || Slot(mr.AsInt32(le)) is not IgbMemoryBlock mb) yield break;
        var mem = MemoryOf(data, mb);
        int n = Math.Min(cnt.AsInt32(le), mem.Length / 4);
        for (int i = 0; i < n; i++)
            if (Slot(le ? BinaryPrimitives.ReadInt32LittleEndian(mem.AsSpan(4 * i)) : BinaryPrimitives.ReadInt32BigEndian(mem.AsSpan(4 * i))) is IgbObject o) yield return o;
    }
    IEnumerable<IgbObject> Children(IgbObject n) => Refs(n).SelectMany(r => f.Is(r, "igNodeList") || (f.Is(r, "igObjectList") && !f.Is(r, "igAttrList")) ? Items(r).Where(i => f.Is(i, "igNode")) : f.Is(r, "igNode") ? new[] { r } : Array.Empty<IgbObject>());
    int OwnGeoms(IgbObject n) => Refs(n).Where(r => f.Is(r, "igAttrList")).SelectMany(Items).Count(a => f.Is(a, "igGeometryAttr"));
    var memo = new Dictionary<int, int>();
    int Total(IgbObject n, HashSet<int> path)
    {
        if (memo.TryGetValue(n.Index, out int v)) return v;
        if (!path.Add(n.Index)) return 0;
        int t = OwnGeoms(n) + Children(n).Sum(c => Total(c, path));
        path.Remove(n.Index);
        return memo[n.Index] = t;
    }
    int lines = 0;
    void Print(IgbObject n, int depth, HashSet<int> path)
    {
        if (lines > 5000 || !path.Add(n.Index)) return;
        string name = n.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? "";
        var kids = Children(n).ToList();
        Console.WriteLine($"{new string(' ', 2 * depth)}#{n.Index} {n.TypeName} \"{name}\" geom {OwnGeoms(n)}/{Total(n, [])} filhos {kids.Count}");
        lines++;
        if (depth < maxDepth) foreach (var c in kids) Print(c, depth + 1, path);
        path.Remove(n.Index);
    }
    foreach (var si in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igSceneInfo")))
    {
        Console.WriteLine($"#{si.Index} {si.TypeName}");
        foreach (var root in Refs(si).Where(r => f.Is(r, "igNode"))) Print(root, 1, []);
    }
    var names = f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igNode"))
        .Select(o => o.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text).Where(t => !string.IsNullOrEmpty(t))
        .GroupBy(t => t!).OrderByDescending(g => g.Count()).Take(80);
    Console.WriteLine("nomes de nós mais comuns: " + string.Join(", ", names.Select(g => $"{g.Key}×{g.Count()}")));
    return 0;
}

// Exporta um personagem (esqueleto, malhas de pele, textura e animações Enbaya) para glTF 2.0.
// Opções de diagnóstico: --quat=conj usa o conjugado dos quatérnios; --trans=rest ignora as translações animadas e usa
// as do esqueleto; --anim=N exporta só as N primeiras animações.
int IgbGltf(string path, string which, string outDir, string[] options)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    bool conj = options.Contains("--quat=conj"), restTrans = options.Contains("--trans=rest"),
        gcBlocks = options.Contains("--dxt=gc"), animTrans = options.Contains("--trans=anim"), allSkins = options.Contains("--todas-peles");
    int maxAnims = int.MaxValue;
    foreach (var o in options.Where(o => o.StartsWith("--anim=", StringComparison.Ordinal))) maxAnims = int.Parse(o[7..], inv);
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    var entry = afs.Entries[idx];
    using var ms = new MemoryStream();
    afs.CopyEntry(entry, ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    var ch = IgbCharacter.Read(f, data);
    var report = new StringBuilder();
    report.AppendLine($"{path} #{idx} ({entry.Name}); opções: {string.Join(' ', options)}");
    foreach (var n in ch.Notes) report.AppendLine($"nota: {n}");
    if (ch.Bones.Count == 0) { Console.Write(report); Console.Error.WriteLine("sem esqueleto"); return 1; }

    var dir = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_gltf");
    Directory.CreateDirectory(dir);
    var g = new GltfBuilder();

    // Esqueleto: um nó por osso, pose de repouso = translações do esqueleto, sem rotação.
    report.AppendLine($"esqueleto \"{ch.SkeletonName}\": {ch.Bones.Count} ossos, {ch.InverseBind.Count} matrizes de pele");
    int rootNode = g.AddNode(new JsonObject { ["name"] = $"dw4_{idx}" });
    g.AddRootNode(rootNode);
    var boneNode = new int[ch.Bones.Count];
    foreach (var b in ch.Bones)
    {
        string name = string.IsNullOrEmpty(b.Name) ? $"osso{b.Index}" : b.Name;
        boneNode[b.Index] = g.AddNode(new JsonObject { ["name"] = name, ["translation"] = new JsonArray(b.Tx, b.Ty, b.Tz) });
        report.AppendLine($"  osso {b.Index,2} {name,-12} pai {b.Parent,2} matriz {b.BlendMatrix,2} t=({b.Tx:0.###} {b.Ty:0.###} {b.Tz:0.###})");
    }
    var rootChildren = new JsonArray();
    foreach (var b in ch.Bones)
    {
        if (b.Parent >= 0 && b.Parent < ch.Bones.Count && b.Parent != b.Index)
        {
            var parent = g.Node(boneNode[b.Parent]);
            if (parent["children"] is not JsonArray kids) parent["children"] = kids = [];
            kids.Add(boneNode[b.Index]);
        }
        else rootChildren.Add(boneNode[b.Index]);
    }

    // Inversas de bind: as do arquivo (em linhas com translação na última linha = mesma ordem de colunas do glTF);
    // ossos sem matriz de pele recebem a inversa da pose de repouso (só translação).
    var boneOfMatrix = new Dictionary<int, int>();
    foreach (var b in ch.Bones) if (b.BlendMatrix >= 0) boneOfMatrix[b.BlendMatrix] = b.Index;
    var ibm = new List<float>();
    foreach (var b in ch.Bones)
    {
        if (b.BlendMatrix >= 0 && b.BlendMatrix < ch.InverseBind.Count) { ibm.AddRange(ch.InverseBind[b.BlendMatrix]); continue; }
        float x = 0, y = 0, z = 0;
        for (var cur = b; ; cur = ch.Bones[cur.Parent])
        {
            x += cur.Tx; y += cur.Ty; z += cur.Tz;
            if (cur.Parent < 0 || cur.Parent >= ch.Bones.Count || cur.Parent == cur.Index) break;
        }
        ibm.AddRange([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, -x, -y, -z, 1]);
    }
    var joints = new JsonArray();
    foreach (var n in boneNode) joints.Add(n);
    int skin = g.AddSkin(new JsonObject { ["name"] = ch.SkeletonName, ["joints"] = joints, ["inverseBindMatrices"] = g.AddFloats(ibm, 16, "MAT4", false) });

    // Texturas
    var materialOfImage = new Dictionary<int, int>();
    int Material(int image)
    {
        if (materialOfImage.TryGetValue(image, out int m)) return m;
        string? uri = null;
        bool alpha = false;
        if (ch.Scene.Images.TryGetValue(image, out var img))
        {
            var rgba = IgbScene.ToRgba(img, gcBlocks);
            report.AppendLine($"imagem #{image} \"{img.Name}\" {img.Width}x{img.Height} formato {img.PixelFormat} {img.BitsPerPixel} bpp: {(rgba == null ? "não decodificada" : "ok")}");
            if (rgba != null)
            {
                uri = $"img{image:D4}.png";
                using var fs = File.Create(Path.Combine(dir, uri));
                Png.WriteRgba(fs, img.Width, img.Height, rgba);
                for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) { alpha = true; break; }
            }
        }
        return materialOfImage[image] = g.AddMaterial(uri ?? "sem_textura", uri, alpha);
    }
    int fallbackImage = ch.Scene.Images.Values.OrderByDescending(i => i.Width * i.Height).FirstOrDefault()?.Index ?? -1;

    // Malhas de pele: um nó por igSkin
    // Por padrão só a pele de maior detalhe ("High…", ou a com mais vértices); --todas-peles exporta todas.
    var chosen = allSkins ? ch.Skins : ch.Skins.Where(k => k.Name.StartsWith("High", StringComparison.Ordinal)).Take(1).ToList();
    if (chosen.Count == 0) chosen = ch.Skins.OrderByDescending(k => k.Parts.Sum(p => p.Positions.Count)).Take(1).ToList();
    foreach (var sm in chosen)
    {
        var prims = new JsonArray();
        report.AppendLine($"pele \"{sm.Name}\": {sm.Parts.Count} geometrias, paleta de {sm.MatrixPalette.Count}, imagem {sm.ImageIndex}");
        foreach (var part in sm.Parts)
        {
            foreach (var n in part.Notes) report.AppendLine($"    {n}");
            int nv = part.Positions.Count;
            if (nv == 0 || part.Triangles.Count == 0) continue;
            var pos = new List<float>(); foreach (var (x, y, z) in part.Positions) pos.AddRange([x, y, z]);
            var attrs = new JsonObject { ["POSITION"] = g.AddFloats(pos, 3, "VEC3", true, minMax: true) };
            if (part.Normals.Count == nv)
            {
                var nrm = new List<float>(); foreach (var (x, y, z) in part.Normals) nrm.AddRange([x, y, z]);
                attrs["NORMAL"] = g.AddFloats(nrm, 3, "VEC3", true);
            }
            if (part.TexCoords.Count == nv)
            {
                var uv = new List<float>(); foreach (var (u, v) in part.TexCoords) uv.AddRange([u, 1 - v]); // glTF: origem no topo
                attrs["TEXCOORD_0"] = g.AddFloats(uv, 2, "VEC2", true);
            }
            if (part.BlendIndices.Count == nv)
            {
                var j4 = new List<int>();
                var w4 = new List<float>();
                int bad = 0;
                foreach (var local in part.BlendIndices)
                {
                    int bone = 0;
                    if (local < sm.MatrixPalette.Count && boneOfMatrix.TryGetValue(sm.MatrixPalette[local], out int bi)) bone = bi;
                    else bad++;
                    j4.AddRange([bone, 0, 0, 0]);
                    w4.AddRange([1, 0, 0, 0]); // pele rígida: todos os pesos do DW4 conferidos valem 1,0
                }
                if (bad > 0) report.AppendLine($"    {bad} vértices com índice fora da paleta (ligados ao osso 0)");
                if (part.BlendWeights.Any(w => Math.Abs(w - 1) > 1e-4)) report.AppendLine("    há pesos diferentes de 1,0: pele não é rígida, revisar");
                attrs["JOINTS_0"] = g.AddJoints(j4);
                attrs["WEIGHTS_0"] = g.AddFloats(w4, 4, "VEC4", true);
            }
            int image = part.ImageIndex >= 0 ? part.ImageIndex : sm.ImageIndex >= 0 ? sm.ImageIndex : fallbackImage;
            var prim = new JsonObject { ["attributes"] = attrs, ["indices"] = g.AddIndices(part.Triangles), ["mode"] = 4 };
            if (image >= 0) prim["material"] = Material(image);
            prims.Add(prim);
            report.AppendLine($"    geometria #{part.GeometryIndex}: {nv} vértices, {part.Triangles.Count / 3} triângulos, imagem {image}");
        }
        if (prims.Count == 0) continue;
        int mesh = g.AddMesh(new JsonObject { ["name"] = sm.Name, ["primitives"] = prims });
        rootChildren.Add(g.AddNode(new JsonObject { ["name"] = sm.Name, ["mesh"] = mesh, ["skin"] = skin }));
    }
    g.Node(rootNode)["children"] = rootChildren;

    // Animações
    var clips = new Dictionary<int, EnbayaClip?>();
    EnbayaClip? Clip(int src)
    {
        if (clips.TryGetValue(src, out var c)) return c;
        try { c = Enbaya.Decode(ch.EnbayaBlocks[src], f.Header.LittleEndian); }
        catch (InvalidDataException ex) { report.AppendLine($"fonte #{src}: {ex.Message}"); c = null; }
        return clips[src] = c;
    }
    var boneByName = new Dictionary<string, int>();
    foreach (var b in ch.Bones) boneByName.TryAdd(string.IsNullOrEmpty(b.Name) ? "" : b.Name, b.Index);
    int exported = 0;
    foreach (var a in ch.Animations.Take(maxAnims))
    {
        var samplers = new JsonArray();
        var channels = new JsonArray();
        double clipDuration = 0;
        foreach (var t in a.Tracks)
        {
            if (!boneByName.TryGetValue(t.Bone, out int bone) || t.SourceIndex < 0 || t.TrackInSource < 0) continue;
            var c = Clip(t.SourceIndex);
            if (c == null || t.TrackInSource >= c.TrackCount) continue;
            clipDuration = Math.Max(clipDuration, c.Duration);
            int times = g.AddFloats(c.Times, 1, "SCALAR", false, minMax: true);
            var rot = new List<float>();
            var tr = new List<float>();
            foreach (var poses in c.Poses)
            {
                var p = poses[t.TrackInSource];
                if (conj) rot.AddRange([-p.Qx, -p.Qy, -p.Qz, p.Qw]); else rot.AddRange([p.Qx, p.Qy, p.Qz, p.Qw]);
                tr.AddRange([p.Tx, p.Ty, p.Tz]);
            }
            samplers.Add(new JsonObject { ["input"] = times, ["output"] = g.AddFloats(rot, 4, "VEC4", false), ["interpolation"] = "LINEAR" });
            channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = boneNode[bone], ["path"] = "rotation" } });
            // Translação: no Guilmon só a raiz (JointRoot) traz translação animada; nas outras trilhas ela vale 0 em todas as
            // amostras e o osso fica na translação do esqueleto (hipótese conferida visualmente). --trans=anim força os
            // valores da animação; --trans=rest força o esqueleto.
            var bone0 = ch.Bones[bone];
            bool zeroTrack = tr.All(v => v == 0);
            if (restTrans || (!animTrans && zeroTrack))
                for (int k = 0; k < tr.Count; k += 3) { tr[k] = bone0.Tx; tr[k + 1] = bone0.Ty; tr[k + 2] = bone0.Tz; }
            {
                samplers.Add(new JsonObject { ["input"] = times, ["output"] = g.AddFloats(tr, 3, "VEC3", false), ["interpolation"] = "LINEAR" });
                channels.Add(new JsonObject { ["sampler"] = samplers.Count - 1, ["target"] = new JsonObject { ["node"] = boneNode[bone], ["path"] = "translation" } });
            }
            if (exported == 0 && c.Poses.Count > 0)
            {
                var p0 = c.Poses[0][t.TrackInSource];
                var bb = ch.Bones[bone];
                report.AppendLine($"  [{a.Name}] {t.Bone,-12} t0 anim=({p0.Tx:0.###} {p0.Ty:0.###} {p0.Tz:0.###}) esqueleto=({bb.Tx:0.###} {bb.Ty:0.###} {bb.Tz:0.###}) q0=({p0.Qx:0.###} {p0.Qy:0.###} {p0.Qz:0.###} {p0.Qw:0.###})");
            }
        }
        if (channels.Count == 0) { report.AppendLine($"animação \"{a.Name}\": sem trilhas utilizáveis"); continue; }
        g.AddAnimation(new JsonObject { ["name"] = a.Name, ["samplers"] = samplers, ["channels"] = channels });
        report.AppendLine($"animação #{a.Index} \"{a.Name}\": {channels.Count} canais, duração da fonte {clipDuration:0.###} s, Long@9 {a.Duration:0.###} s");
        exported++;
    }

    g.Save(dir, "personagem");
    File.WriteAllText(Path.Combine(dir, "relatorio.txt"), report.ToString());
    Console.Write(report);
    Console.WriteLine($"{ch.Skins.Count} peles, {exported} animações gravadas em {dir}");
    return 0;
}

// Levantamento por IGB: quantos objetos de classes-chave (geometria, imagem, esqueleto, animação, colisão) e nomes de
// exemplo, para escolher amostras da Fase 2. Grava CSV fora do repositório (contém nomes do jogo).
int IgbSurvey(string path, string csvPath)
{
    var full = Path.GetFullPath(csvPath);
    if (InsideGitRepo(Path.GetDirectoryName(full)!)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var collisionLike = new System.Text.RegularExpressions.Regex("col|hit|ground|wall|floor|bound|bsp|terrain|navi|path|land",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    var classTotals = new Dictionary<string, (int objects, int files)>();
    using var w = new StreamWriter(full, false, new UTF8Encoding(true));
    w.WriteLine(CsvLine(["id", "categoria", "bytes", "objetos", "geometrias", "imagens", "esqueletos", "animacoes", "faixas",
        "blend_matrix", "classes_colisao", "imagens_ex", "animacoes_ex", "esqueletos_ex", "lua_ex", "classes_be"]));
    int files = 0;
    foreach (var (id, cat, data) in IgbSources(path))
    {
        IgbFile f;
        try { f = IgbFile.Read(data); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentOutOfRangeException) { continue; }
        files++;
        var objs = f.Slots.OfType<IgbObject>().ToList();
        int Count(string cls) => objs.Count(o => f.Is(o, cls));
        string Names(string cls, int max) => string.Join(" | ", objs.Where(o => f.Is(o, cls))
            .Select(o => o.Fields.FirstOrDefault(v => v.Text is { Length: > 0 })?.Text).Where(t => t != null).Distinct().Take(max));
        var perClass = objs.GroupBy(o => o.TypeName).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (k, n) in perClass)
        {
            var t = classTotals.GetValueOrDefault(k);
            classTotals[k] = (t.objects + n, t.files + 1);
        }
        var coll = perClass.Where(kv => collisionLike.IsMatch(kv.Key)).Select(kv => $"{kv.Key}:{kv.Value}");
        var be = perClass.Where(kv => kv.Key.StartsWith("be", StringComparison.Ordinal)).OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key}:{kv.Value}");
        w.WriteLine(CsvLine([id, cat, data.Length.ToString(inv), objs.Count.ToString(inv), Count("igGeometryAttr").ToString(inv),
            Count("igImage").ToString(inv), Count("igSkeleton").ToString(inv), Count("igAnimation").ToString(inv),
            Count("igAnimationTrack").ToString(inv), Count("igBlendMatrixSelect").ToString(inv), string.Join(" ", coll),
            Names("igImage", 4), Names("igAnimation", 4), Names("igSkeleton", 2), Names("beLuaData", 3), string.Join(" ", be)]));
    }
    Console.WriteLine($"{files} IGB levantados em {full}");
    Console.WriteLine("classes com nome parecido com colisão/terreno (objetos em arquivos):");
    foreach (var (k, (n, nf)) in classTotals.Where(kv => collisionLike.IsMatch(kv.Key)).OrderByDescending(kv => kv.Value.objects))
        Console.WriteLine($"  {k}: {n} objetos em {nf} arquivos");
    Console.WriteLine("classes de esqueleto, pele e animação:");
    foreach (var (k, (n, nf)) in classTotals.Where(kv => System.Text.RegularExpressions.Regex.IsMatch(kv.Key, "Skel|Skin|Blend|Anim|Actor|Bone|Enbaya|Joint", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).OrderByDescending(kv => kv.Value.objects))
        Console.WriteLine($"  {k}: {n} objetos em {nf} arquivos");
    return 0;
}

// Separa os fluxos de um vídeo Sofdec (MPEG-PS) de um AFS: vídeo em .m1v e áudio em .adx, numa pasta fora do repositório.
int SfdDemux(string path, string which, string outDir)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    var entry = afs.Entries[idx];
    using var ms = new MemoryStream();
    afs.CopyEntry(entry, ms);
    var data = ms.ToArray();
    if (Classifier.Classify(data) != ContentKind.MpegPs) { Console.Error.WriteLine("o item não começa com pacote MPEG-PS"); return 1; }

    var (streams, packs, problems) = MpegPs.Demux(data);
    Directory.CreateDirectory(root);
    var stem = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}");
    Console.WriteLine($"{path} #{idx} ({entry.Name}, {entry.Size} B): {packs} pacotes, {streams.Count} fluxos, {problems.Count} problemas");
    foreach (var p in problems.Take(10)) Console.WriteLine($"  problema: {p}");
    foreach (var s in streams.Values.OrderBy(s => s.StreamId))
    {
        var bytes = s.Payload.ToArray();
        string kind = s.StreamId switch { >= 0xE0 and <= 0xEF => "vídeo", >= 0xC0 and <= 0xDF => "áudio", 0xBD => "privado 1", _ => "?" };
        string ext = s.StreamId switch { >= 0xE0 and <= 0xEF => "m1v", >= 0xC0 and <= 0xDF => "adx", _ => "bin" };
        string detail = "";
        if (kind == "vídeo" && MpegPs.SequenceHeader(bytes) is (int w, int h, int fr))
            detail = $"; sequência {w}x{h}, código de quadros {fr}";
        if (kind == "áudio") detail = $"; reconhecido como {Classifier.Classify(bytes)}";
        var file = $"{stem}_{s.StreamId:X2}.{ext}";
        File.WriteAllBytes(file, bytes);
        Console.WriteLine($"  fluxo 0x{s.StreamId:X2} ({kind}): {s.Packets} pacotes, {bytes.Length} B{detail}; início {ByteStats.Hex(bytes.AsSpan(0, Math.Min(16, bytes.Length)))} -> {file}");
    }
    return problems.Count == 0 ? 0 : 3;
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
                    var names = namesByTarget.GetValueOrDefault(o.Index) ?? new List<string> { "sem_nome" };
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
