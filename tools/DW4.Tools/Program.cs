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
using DW4.Script;
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
        "igb-colisao" when args.Length == 5 => IgbColisao(args[1], args[2], args[3], args[4]),
        "igb-cena" when args.Length is 5 or 6 => IgbCena(args[1], args[2], args[3], args[4], args.Length == 6 && args[5].StartsWith("--pacote=", StringComparison.Ordinal) ? args[5]["--pacote=".Length..] : null),
        "igb-seq" when args.Length is 4 or 5 => IgbSeq(args[1], args[2], args[3], args.Length == 5 ? args[4] : null),
        "dtm-entradas" when args.Length is 2 or 3 => DtmEntradas(args[1], args.Length == 3 ? args[2] : null),
        "dolphin-ram" when args.Length == 3 => DolphinRam(args[1], args[2]),
        "ram-busca" when args.Length >= 4 => RamBusca(args[1], args[2], args[3], args[4..]),
        "dolphin-gdb-teste" when args.Length == 2 => DolphinGdbTeste(args[1]),
        "dolphin-gdb" when args.Length >= 4 => DolphinGdb(args[1], args[2], args[3..]),
        "igb-camera" when args.Length == 4 => IgbCamera(args[1], args[2], args[3]),
        "igb-camera-demo" when args.Length == 4 => IgbCameraDemoCmd(args[1], args[2], args[3]),
        "igb-textos" when args.Length == 4 => IgbTextos(args[1], args[2], args[3]),
        "igb-entradas" when args.Length == 4 => IgbEntradas(args[1], args[2], args[3]),
        "fase-exportar" when args.Length == 5 => FaseExportar(args[1], args[2], args[3], args[4]),
        "igb-db" when args.Length == 4 => IgbDb(args[1], args[2], args[3]),
        "fonte-exportar" when args.Length == 4 => FonteExportar(args[1], args[2], args[3]),
        "fonte-caixas" when args.Length == 4 => FonteCaixas(args[1], args[2], args[3]),
        "igb-drops" when args.Length == 4 => IgbDropsCmd(args[1], args[2], args[3]),
        "igb-disparos" when args.Length == 4 => IgbDisparos(args[1], args[2], args[3]),
        "igb-geradores" when args.Length == 4 => IgbGeradores(args[1], args[2], args[3]),
        "igb-gatilhos" when args.Length == 4 => IgbGatilhos(args[1], args[2], args[3]),
        "adx-wav" when args.Length >= 4 => AdxWav(args[1], args[2], args[3..]),
        "igb-sons" when args.Length == 4 => IgbSons(args[1], args[2], args[3]),
        "igb-hud" when args.Count(a => !a.StartsWith("--atores=", StringComparison.Ordinal)) is 4 or 6 => IgbHudCmd(args.Where(a => !a.StartsWith("--atores=", StringComparison.Ordinal)).ToArray(), args.FirstOrDefault(a => a.StartsWith("--atores=", StringComparison.Ordinal))?["--atores=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries)),
        "igb-objetos" when args.Length >= 5 => IgbObjetos(args[1], args[2], args[3], args[4..].Where(a => !a.StartsWith("--pacote=", StringComparison.Ordinal)).ToArray(),
            args.FirstOrDefault(a => a.StartsWith("--pacote=", StringComparison.Ordinal))?["--pacote=".Length..]),
        "igb-survey" when args.Length == 3 => IgbSurvey(args[1], args[2]),
        "sfd-demux" when args.Length == 4 => SfdDemux(args[1], args[2], args[3]),
        "titulo-exportar" when args.Length == 4 => TituloExportar(args[1], args[2], args[3]),
        "sfd-quadros" when args.Length >= 4 => SfdQuadros(args[1], args[2], args[3], args[4..]),
        "lua-scan" when args.Length is 2 or 3 => LuaScan(args[1], args.Length == 3 ? args[2] : null),
        "lua-extrair" when args.Length == 4 => LuaExtrair(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3]),
        "lua-trace" when args.Length >= 3 => LuaTrace(args[1], int.Parse(args[2], CultureInfo.InvariantCulture), args[3..]),
        "dol-info" when args.Length == 2 => DolInfo(args[1]),
        "dol-desmontar" when args.Length == 4 => DolDesmontar(args[1], args[2], args[3]),
        "dol-refs" when args.Length == 3 => DolRefs(args[1], args[2]),
        "dol-skilltab" when args.Length == 3 => DolSkillTab(args[1], args[2]),
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
          dw4 igb-gltf     <arquivo.afs> <indice> <pasta-de-trabalho> [--quat=as] [--trans=rest|anim] [--dxt=gc] [--todas-peles] [--anim=N] [--ator=NOME]
                           (personagem com esqueleto, pele e animações em glTF, fora do repositório)
          dw4 igb-colisao  <arquivo.afs> <indice> <no-raiz> <pasta-de-trabalho>   (malha de colisão em glTF, nó "-colonly" para a Godot)
          dw4 igb-cena     <arquivo.afs> <indice> <no-raiz|todas> <pasta-de-trabalho> [--pacote=<arquivo.afs>:<indice>]   (cenário ou objeto estático em glTF com texturas; o pacote resolve as texturas por nome, beTextureCtrlAttr)
          dw4 igb-seq      <arquivo.afs> <indice> <pasta-de-trabalho> [--ator=NOME]   (sequências beModelCtrlData em JSON e texto)
          dw4 dtm-entradas <gravacao.dtm> [saida.csv]   (linha do tempo das entradas de uma gravação do Dolphin)
          dw4 dolphin-ram  <estado.sav> <saida.bin>   (MEM1 de 24 MiB de um save state do Dolphin, fora do repositório)
          dw4 ram-busca    <a.bin> <b.bin> <quadros-entre-eles> [--y=40]   (contadores de quadro e posições candidatas)
          dw4 dolphin-gdb  <porta> <pasta> <quadro>... [--contador=0x814541D8] [--amostra=ENDERECO,TAM]   (MEM1 nos quadros pedidos, via servidor GDB do Dolphin)
          dw4 igb-camera   <arquivo.afs> <indice> <pasta-de-trabalho>   (volumes de câmera da fase em JSON, fora do repositório)
          dw4 igb-camera-demo <arquivo.afs> <indice> <pasta-de-trabalho>   (câmeras de demo do acmr_pack, amostradas a 60 Hz, em JSON, fora do repositório)
          dw4 igb-textos   <arquivo.afs> <indice> <pasta-de-trabalho>   (textos de um *_pack: índice na lista, chave e texto, em textos.json, fora do repositório)
          dw4 igb-entradas <arquivo.afs> <indice> <pasta-de-trabalho>   (pontos de entrada do jogador, bePoint01Info, em pontos_entrada.json, fora do repositório)
          dw4 fase-exportar <ndmw.afs> <area.afs> <fase> <pasta-saída>   (uma fase inteira no layout da fatia + fase.json, pelo índice do ndmw.igb; fora do repositório)
          dw4 igb-db       <arquivo.afs> <indice> <pasta-de-trabalho>   (as 12 tabelas beDataBase em CSV e JSON, fora do repositório)
          dw4 fonte-exportar <ndmw.afs> <indice> <pasta-de-trabalho>   (fontes beFontImage: textura PNG e métricas dos caracteres em fonte.json; a do jogo é o #79)
          dw4 fonte-caixas <area.afs> <indice> <pasta>   (caixas de texto do usa_pack: categoria TALKFONT…, família, retângulo 640×480, cor e posição em caixas.json)
          dw4 igb-drops    <arquivo.afs> <indice> <pasta-de-trabalho>   (listas de drop dos geradores em JSON, fora do repositório)
          dw4 igb-geradores <arquivo.afs> <indice> <pasta-de-trabalho>   (geradores de onda em geradores.json: posições absolutas por vaga, contagem por nº de jogadores)
          dw4 igb-gatilhos <arquivo.afs> <indice> <pasta-de-trabalho>   (volumes de gatilho de área beMatCtrlData em gatilhos.json)
          dw4 adx-wav      <arquivo.afs> <pasta-de-trabalho> <indice> [indice...]   (ADX → WAV com o loop num chunk smpl, fora do repositório)
          dw4 igb-sons     <arquivo.afs> <indice> <pasta-de-trabalho>   (bancos .sfp do IGB → WAV por cue A9GG_CC, e tabela de sons em JSON)
          dw4 igb-hud      <ndmw.afs> <indice-fase> [<area.afs> <indice-usa_pack>] <pasta-de-trabalho> [--atores=a,b,c]   (atores de interface i_* com nós, sequências, medidores e números em JSON + PNG)
          dw4 igb-survey   <arquivo.afs> <saida.csv>   (uma linha por IGB: contagens de classes-chave e nomes; fora do repositório)
          dw4 sfd-demux    <arquivo.afs> <indice> <pasta-de-trabalho>   (separa áudio e vídeo de um Sofdec, fora do repositório)
          dw4 titulo-exportar <area.afs> <indice-usa_pack> <pasta>   (tela de título j_start* do pacote de idioma, com sequências, para o menu inicial)
          dw4 sfd-quadros  <arquivo.afs> <indice> <pasta-de-trabalho> [quadro...]   (decodifica o vídeo MPEG-1 e grava os quadros pedidos em PNG)
          dw4 lua-scan     <arquivo.afs> [pasta-de-trabalho]   (com pasta: grava listagens fora do repositório)
          dw4 igb-objetos  <ndmw.afs> <entrada> <pasta> <ator|ator=raiz>... [--pacote=<arquivo.afs>:<indice>]   (objetos rígidos da fase para a fatia)
          dw4 lua-extrair  <ndmw.afs> <entrada> <pasta-de-trabalho>   (scripts da fase para o cliente; fora do repositório)
          dw4 lua-trace    <ndmw.afs> <entrada> [x.lua ...] [Metodo=valor ...]
                           (executa os scripts da fase na VM própria, na ordem do motor, e mostra cada chamada be:*)
          dw4 dol-info     <main.dol>   (seções, entrada e bases r13/r2; leitura estática, nada é executado)
          dw4 dol-desmontar <main.dol> <endereco-hex> <instrucoes>   (PowerPC no console, com strings e floats anotados)
          dw4 dol-refs     <main.dol> <endereco-hex | "texto">   (ponteiros em dados e código que montam o endereço)
          dw4 dol-skilltab <main.dol> <pasta-de-trabalho>   (taxas do P2ESKILLUP em JSON, fora do repositório)
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
// Os quatérnios Enbaya são gravados conjugados (conferido em capturas do Guilmon: só assim as poses ficam naturais).
// Opções de diagnóstico: --quat=as grava os quatérnios como estão no arquivo; --trans=rest ignora as translações animadas e usa
// as do esqueleto; --anim=N exporta só as N primeiras animações.
int IgbGltf(string path, string which, string outDir, string[] options)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    bool conj = !options.Contains("--quat=as"), restTrans = options.Contains("--trans=rest"),
        gcBlocks = options.Contains("--dxt=gc"), animTrans = options.Contains("--trans=anim"), allSkins = options.Contains("--todas-peles");
    int maxAnims = int.MaxValue;
    foreach (var o in options.Where(o => o.StartsWith("--anim=", StringComparison.Ordinal))) maxAnims = int.Parse(o[7..], inv);
    string? actor = options.Where(o => o.StartsWith("--ator=", StringComparison.Ordinal)).Select(o => o[7..]).LastOrDefault();
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    var entry = afs.Entries[idx];
    using var ms = new MemoryStream();
    afs.CopyEntry(entry, ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    var ch = IgbCharacter.Read(f, data, actor);
    var report = new StringBuilder();
    report.AppendLine($"{path} #{idx} ({entry.Name}); opções: {string.Join(' ', options)}");
    foreach (var n in ch.Notes) report.AppendLine($"nota: {n}");
    if (ch.Bones.Count == 0) { Console.Write(report); Console.Error.WriteLine("sem esqueleto"); return 1; }

    var dir = Path.Combine(root, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}{(actor == null ? "" : "_" + actor)}_gltf");
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
            if (part.RigidBone >= 0)
            {
                // Pele rígida por osso: vértices no espaço do osso. Para o glTF (mundo = junta · inversa de bind · v), leva v para
                // o espaço do modelo pela inversa da inversa de bind dessa junta; todos os vértices ficam na junta.
                var m = new System.Numerics.Matrix4x4(ibm[16 * part.RigidBone], ibm[16 * part.RigidBone + 1], ibm[16 * part.RigidBone + 2], ibm[16 * part.RigidBone + 3],
                    ibm[16 * part.RigidBone + 4], ibm[16 * part.RigidBone + 5], ibm[16 * part.RigidBone + 6], ibm[16 * part.RigidBone + 7],
                    ibm[16 * part.RigidBone + 8], ibm[16 * part.RigidBone + 9], ibm[16 * part.RigidBone + 10], ibm[16 * part.RigidBone + 11],
                    ibm[16 * part.RigidBone + 12], ibm[16 * part.RigidBone + 13], ibm[16 * part.RigidBone + 14], ibm[16 * part.RigidBone + 15]);
                System.Numerics.Matrix4x4.Invert(m, out var bind);
                for (int i = 0; i < nv; i++)
                {
                    var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(part.Positions[i].X, part.Positions[i].Y, part.Positions[i].Z), bind);
                    part.Positions[i] = (p.X, p.Y, p.Z);
                }
                for (int i = 0; i < part.Normals.Count; i++)
                {
                    var q = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(new System.Numerics.Vector3(part.Normals[i].X, part.Normals[i].Y, part.Normals[i].Z), bind));
                    part.Normals[i] = (q.X, q.Y, q.Z);
                }
                part.BlendIndices.Clear();
            }
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
            if (part.RigidBone >= 0)
            {
                attrs["JOINTS_0"] = g.AddJoints(Enumerable.Range(0, nv).SelectMany(_ => new[] { part.RigidBone, 0, 0, 0 }).ToList());
                attrs["WEIGHTS_0"] = g.AddFloats(Enumerable.Range(0, nv).SelectMany(_ => new[] { 1f, 0, 0, 0 }).ToList(), 4, "VEC4", true);
            }
            else if (part.BlendIndices.Count == nv * part.BlendCount)
            {
                var j4 = new List<int>();
                var w4 = new List<float>();
                int bad = 0, k = part.BlendCount;
                float minSum = float.MaxValue, maxSum = float.MinValue;
                for (int v = 0; v < nv; v++)
                {
                    var js = new int[4];
                    var ws = new float[4];
                    float sum = 0;
                    for (int c = 0; c < k; c++)
                    {
                        int local = part.BlendIndices[v * k + c];
                        if (local < sm.MatrixPalette.Count && boneOfMatrix.TryGetValue(sm.MatrixPalette[local], out int bi)) js[c] = bi;
                        else bad++;
                        // Com 1 peso por vértice, todos os pesos conferidos valem 1,0 (pele rígida). Com mais, vale o peso de cada um.
                        ws[c] = k == 1 ? 1 : part.BlendWeights[v * k + c];
                        sum += part.BlendWeights[v * k + c];
                    }
                    minSum = Math.Min(minSum, sum); maxSum = Math.Max(maxSum, sum);
                    float norm = ws.Sum();
                    if (norm > 1e-6f) for (int c = 0; c < 4; c++) ws[c] /= norm; else ws[0] = 1;
                    j4.AddRange(js);
                    w4.AddRange(ws);
                }
                if (bad > 0) report.AppendLine($"    {bad} índices fora da paleta (ligados ao osso 0)");
                report.AppendLine($"    pele com {k} peso(s) por vértice; soma dos pesos entre {minSum:0.###} e {maxSum:0.###}");
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
    Console.WriteLine($"{chosen.Count} de {ch.Skins.Count} peles, {exported} animações gravadas em {dir}");
    return 0;
}

// Exporta as geometrias de uma raiz de cena (ex.: a camada de colisão "BG01" de uma fase) como glTF só com posições e
// índices, num nó com sufixo "-colonly": a Godot importa isso como StaticBody3D com forma de colisão côncava.
int IgbColisao(string path, string which, string rootArg, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    int rootIndex = int.Parse(rootArg, inv);
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var scene = IgbScene.Read(IgbFile.Read(data), data);
    var parts = scene.Parts.Where(p => p.SceneRoot == rootIndex && p.Positions.Count > 0 && p.Triangles.Count > 0).ToList();
    if (parts.Count == 0) { Console.Error.WriteLine($"nenhuma geometria na raiz {rootIndex}; raízes: {string.Join(", ", scene.Parts.Select(p => p.SceneRoot).Distinct())}"); return 1; }
    // Partes com nome (FATO no #179): beHitLandModelInfo = [@8 igSwitch com as geometrias, @9 igNamedObjectList com os nomes,
    // na mesma ordem] → doorwall01..03, que o "HITLANDMODEL doorwallNN OFFHIT" desliga. Cada uma vai num nó próprio.
    var file = IgbFile.Read(data);
    bool le = file.Header.LittleEndian;
    IgbObject? Ref(IgbObject o, int slot) { var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef"); int i = v?.AsInt32(le) ?? -1; return i >= 0 && i < file.Slots.Count ? file.Slots[i] as IgbObject : null; }
    var nameOfGeometry = new Dictionary<int, string>();
    foreach (var hl in file.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beHitLandModelInfo"))
    {
        if (Ref(hl, 8) is not { } sw || Ref(sw, 7) is not { } kids || Ref(hl, 9) is not { } names) continue;
        var geoms = scene.ObjectList(kids);
        var labels = scene.ObjectList(names).Select(n => n.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? "").ToList();
        for (int k = 0; k < geoms.Count && k < labels.Count; k++)
            foreach (var ga in scene.AttrList(geoms[k]).Where(a => file.Is(a, "igGeometryAttr"))) nameOfGeometry[ga.Index] = labels[k];
    }
    // FATO no #179: os marcadores dos pontos de entrada (bePoint01Info @7 igSwitch → beGroupKeep → igTransform → igGeometry,
    // pirâmides de y 40 a 50 em cada vaga) ficam sob a mesma raiz da colisão. Não são chão: o jogador nascia dentro deles e
    // afundava. Ficam de fora.
    var markers = new HashSet<int>();
    void Collect(IgbObject n, int depth)
    {
        if (depth > 16) return;
        if (file.Is(n, "igGeometry"))
            foreach (var ga in scene.AttrList(n).Where(a => file.Is(a, "igGeometryAttr"))) markers.Add(ga.Index);
        if (Ref(n, 7) is { } kids && file.Is(kids, "igNodeList"))
            foreach (var c in scene.ObjectList(kids)) Collect(c, depth + 1);
    }
    foreach (var pi in file.Slots.OfType<IgbObject>().Where(o => o.TypeName == "bePoint01Info"))
        if (Ref(pi, 7) is { } sw) Collect(sw, 0);
    int before = parts.Count;
    parts = parts.Where(p => !markers.Contains(p.GeometryIndex)).ToList();
    if (parts.Count != before) Console.WriteLine($"{before - parts.Count} geometria(s) de marcador de entrada (bePoint01Info) fora da colisão");
    var dir = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_colisao_{rootIndex}");
    Directory.CreateDirectory(dir);
    var g = new GltfBuilder();
    var pos = new List<float>();
    int tri = 0;
    foreach (var grp in parts.GroupBy(pt => nameOfGeometry.GetValueOrDefault(pt.GeometryIndex, "colisao")).OrderBy(x => x.Key == "colisao" ? 0 : 1).ThenBy(x => x.Key, StringComparer.Ordinal))
    {
        var gp = new List<float>();
        var gt = new List<int>();
        foreach (var part in grp)
        {
            int b = gp.Count / 3;
            foreach (var (x, y, z) in part.Positions) gp.AddRange([x, y, z]);
            gt.AddRange(part.Triangles.Select(t => t + b));
        }
        pos.AddRange(gp);
        tri += gt.Count / 3;
        var prim = new JsonObject { ["attributes"] = new JsonObject { ["POSITION"] = g.AddFloats(gp, 3, "VEC3", true, minMax: true) }, ["indices"] = g.AddIndices(gt), ["mode"] = 4 };
        int mesh = g.AddMesh(new JsonObject { ["name"] = grp.Key, ["primitives"] = new JsonArray(prim) });
        g.AddRootNode(g.AddNode(new JsonObject { ["name"] = $"{grp.Key}-colonly", ["mesh"] = mesh }));
        if (grp.Key != "colisao") Console.WriteLine($"parte {grp.Key}: {grp.Count()} geometria(s), {gt.Count / 3} triângulos");
    }
    g.Save(dir, "colisao");
    float minY = float.MaxValue, maxY = float.MinValue;
    for (int i = 1; i < pos.Count; i += 3) { minY = Math.Min(minY, pos[i]); maxY = Math.Max(maxY, pos[i]); }
    Console.WriteLine($"{parts.Count} geometrias, {pos.Count / 3} vértices, {tri} triângulos, y de {minY:0.##} a {maxY:0.##} -> {dir}");
    return 0;
}

// Exporta as geometrias visuais de uma raiz de cena (ex.: "BG02" da dng1) como glTF estático com normais, UV e um
// material por imagem. As imagens de formato 14 usam o layout de PC (confirmado na Fase 2).
int IgbCena(string path, string which, string rootArg, string outDir, string? packSpec = null)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    bool allRoots = rootArg == "todas";
    int rootIndex = allRoots ? -1 : int.Parse(rootArg, inv);
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var scene = IgbScene.Read(IgbFile.Read(data), data);
    var parts = scene.Parts.Where(p => (allRoots || p.SceneRoot == rootIndex) && p.Positions.Count > 0 && p.Triangles.Count > 0).ToList();
    if (parts.Count == 0) { Console.Error.WriteLine($"nenhuma geometria na raiz {rootIndex}; raízes: {string.Join(", ", scene.Parts.Select(p => p.SceneRoot).Distinct())}"); return 1; }
    var dir = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_cena_{(allRoots ? "todas" : rootIndex.ToString(inv))}");
    Directory.CreateDirectory(dir);
    var g = new GltfBuilder();
    var report = new StringBuilder();
    var materialOfImage = new Dictionary<(int, bool), int>();
    // Texturas por nome (beTextureCtrlAttr → beTextureCtrlData do pacote: lh_pack/ls_pack nos campos).
    IgbScene? packScene = null;
    var packImage = new Dictionary<string, int>();
    if (packSpec != null && packSpec.LastIndexOf(':') is int colon and > 0 && packSpec[(colon + 1)..] is var packIdx && packSpec[..colon] is var packAfs)
    {
        using var pa = AfsArchive.Open(packAfs);
        using var pms = new MemoryStream();
        pa.CopyEntry(pa.Entries[int.Parse(packIdx, inv)], pms);
        var pd = pms.ToArray();
        var pf = IgbFile.Read(pd);
        packScene = IgbScene.Read(pf, pd);
        foreach (var o in pf.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beTextureCtrlData"))
        {
            var name = o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text;
            int r = o.Fields.FirstOrDefault(v => v.Desc.Slot == 3 && v.Desc.ShortName == "ObjectRef")?.AsInt32(pf.Header.LittleEndian) ?? -1;
            if (name != null && r >= 0 && pf.Slots.ElementAtOrDefault(r) is IgbObject attr && packScene.FindImage(attr) is int pi && pi >= 0) packImage.TryAdd(name, pi);
        }
    }
    var materialOfName = new Dictionary<(string, bool), int>();
    int NamedMaterial(string name, bool add)
    {
        if (materialOfName.TryGetValue((name, add), out int m)) return m;
        string? uri = null;
        bool alpha = false;
        if (packScene != null && packImage.TryGetValue(name, out int pi) && packScene.Images.TryGetValue(pi, out var img) && IgbScene.ToRgba(img) is { } rgba)
        {
            uri = $"pack_{string.Concat(Path.GetFileNameWithoutExtension(name).Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'))}.png";
            using var fs = File.Create(Path.Combine(dir, uri));
            Png.WriteRgba(fs, img.Width, img.Height, rgba);
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) { alpha = true; break; }
            report.AppendLine($"textura \"{name}\" do pacote: {img.Width}x{img.Height} formato {img.PixelFormat}{(alpha ? ", com alfa" : "")}");
        }
        else report.AppendLine($"textura \"{name}\": {(packScene == null ? "sem --pacote" : "não achada no pacote")}");
        return materialOfName[(name, add)] = g.AddMaterial(uri ?? name, uri, alpha, add);
    }
    int Material(int image, bool add)
    {
        if (materialOfImage.TryGetValue((image, add), out int m)) return m;
        string? uri = null;
        bool alpha = false;
        if (scene.Images.TryGetValue(image, out var img) && IgbScene.ToRgba(img) is { } rgba)
        {
            uri = $"img{image:D4}.png";
            using var fs = File.Create(Path.Combine(dir, uri));
            Png.WriteRgba(fs, img.Width, img.Height, rgba);
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] < 255) { alpha = true; break; }
            report.AppendLine($"imagem #{image} \"{img.Name}\" {img.Width}x{img.Height} formato {img.PixelFormat}{(alpha ? ", com alfa" : "")}");
        }
        else report.AppendLine($"imagem #{image}: {(image < 0 ? "sem textura" : "não decodificada")}");
        return materialOfImage[(image, add)] = g.AddMaterial(uri ?? "sem_textura", uri, alpha, add);
    }
    // Geometria sem textura sob um igColorAttr (FATO: o plano da stagbs06, 0,4 0,4 0,4): material da cor, em vez do branco.
    var materialOfColor = new Dictionary<string, int>();
    int ColorMaterial(float[] c)
    {
        string key = string.Join(",", c.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
        if (materialOfColor.TryGetValue(key, out int cm)) return cm;
        cm = g.AddMaterial("cor_" + key, null, false);
        g.SetBaseColor(cm, c);
        report.AppendLine($"sem textura com igColorAttr ({key})");
        return materialOfColor[key] = cm;
    }
    var prims = new JsonArray();
    foreach (var part in parts)
    {
        int nv = part.Positions.Count;
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
        if (part.Colors.Count == nv && nv > 0 && part.ImageIndex < 0 && part.TextureName == null)
            report.AppendLine($"geometria #{part.GeometryIndex} sem textura com cor de vértice: primeira 0x{part.Colors[0]:X8}");
        prims.Add(new JsonObject { ["attributes"] = attrs, ["indices"] = g.AddIndices(part.Triangles), ["mode"] = 4, ["material"] = part.ImageIndex < 0 && part.TextureName != null ? NamedMaterial(part.TextureName, part.BlendDst == 1)
            : part.ImageIndex < 0 && part.Color != null ? ColorMaterial(part.Color) : Material(part.ImageIndex, part.BlendDst == 1) });
    }
    // A Godot aceita no máximo 256 superfícies por malha (RenderingServer MAX_MESH_SURFACES; o campo stag0101 tem 331
    // geometrias): acima disso, mais malhas "cena_2", "cena_3"… num nó cada.
    const int MaxSurfaces = 256;
    var all = prims.Select(p => p!.DeepClone()).ToList();
    for (int k = 0; k * MaxSurfaces < Math.Max(1, all.Count); k++)
    {
        string name = k == 0 ? "cena" : $"cena_{k + 1}";
        int mesh = g.AddMesh(new JsonObject { ["name"] = name, ["primitives"] = new JsonArray(all.Skip(k * MaxSurfaces).Take(MaxSurfaces).ToArray()) });
        g.AddRootNode(g.AddNode(new JsonObject { ["name"] = name, ["mesh"] = mesh }));
    }
    g.Save(dir, "cena");
    var byImage = parts.GroupBy(p => p.ImageIndex).OrderByDescending(x => x.Count()).Select(x => $"#{x.Key}: {x.Count()}");
    report.AppendLine($"geometrias por imagem: {string.Join(", ", byImage)}");
    File.WriteAllText(Path.Combine(dir, "relatorio.txt"), report.ToString());
    Console.Write(report);
    Console.WriteLine($"{parts.Count} geometrias, {parts.Sum(p => p.Positions.Count)} vértices, {parts.Sum(p => p.Triangles.Count) / 3} triângulos, {materialOfImage.Count} materiais -> {dir}");
    return 0;
}

// Grava as sequências de controle de um ator (SQ_WAIT, SQ_ATCK1…) com os comandos em ordem, em JSON e em texto.
int IgbSeq(string path, string which, string outDir, string? option)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    string? actor = option != null && option.StartsWith("--ator=", StringComparison.Ordinal) ? option[7..] : null;
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var notes = new List<string>();
    var seqs = IgbSequences.Read(IgbFile.Read(data), data, actor, notes);
    foreach (var n in notes) Console.WriteLine($"nota: {n}");
    if (seqs.Count == 0) { Console.Error.WriteLine("nenhuma sequência"); return 1; }
    string Fmt(object v) => v switch
    {
        string s => $"\"{s}\"",
        float x => x.ToString("0.####", inv),
        float[] a => "(" + string.Join(", ", a.Select(x => x.ToString("0.####", inv))) + ")",
        _ => Convert.ToString(v, inv) ?? "",
    };
    JsonNode? Node(object v) => v switch
    {
        string s => JsonValue.Create(s),
        float x => JsonValue.Create(x),
        int i => JsonValue.Create(i),
        bool b => JsonValue.Create(b),
        float[] a => new JsonArray(a.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
        _ => null,
    };
    notes.Clear();
    var acts = IgbSequences.ReadActions(IgbFile.Read(data), data, actor, notes);
    foreach (var n in notes) Console.WriteLine($"nota: {n}");
    var text = new StringBuilder();
    JsonObject Section(string title, Dictionary<string, List<IgbSeqCommand>> lists)
    {
        var obj = new JsonObject();
        text.AppendLine($"=== {title} ===");
        foreach (var (name, cmds) in lists.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"{name} ({cmds.Count} comandos)");
            var arr = new JsonArray();
            foreach (var c in cmds)
            {
                text.AppendLine($"  #{c.Index,-6} {c.Kind,-10} {string.Join(" ", c.Values.Select(Fmt))}");
                arr.Add(new JsonObject { ["k"] = c.Kind, ["v"] = new JsonArray(c.Values.Select(Node).ToArray()) });
            }
            obj[name] = arr;
        }
        return obj;
    }
    var root = new JsonObject { ["sequences"] = Section("sequências (beModelCtrlData)", seqs), ["actions"] = Section("ações (beAction2Data)", acts) };
    notes.Clear();
    var maps = IgbSequences.ReadAiMaps(IgbFile.Read(data), data, actor, notes);
    foreach (var n in notes) Console.WriteLine($"nota: {n}");
    if (maps.Count > 0)
    {
        text.AppendLine("=== mapas de IA (beModelCtrlAIMap) ===");
        var arr = new JsonArray();
        foreach (var m in maps)
        {
            text.AppendLine($"#{m.Index} {m.Name} {string.Join(" ", m.Values.Select(x => Fmt(x)))} {string.Join(" ", m.Strings.Select(x => Fmt(x)))}");
            var lists = new JsonArray();
            for (int i = 0; i < m.Lists.Count; i++)
            {
                text.AppendLine($"  lista {i}: " + string.Join(" | ", m.Lists[i].Select(c => $"#{c.Index} {string.Join(" ", c.Values.Select(Fmt))}")));
                lists.Add(new JsonArray(m.Lists[i].Select(c => (JsonNode?)new JsonArray(c.Values.Select(Node).ToArray())).ToArray()));
            }
            arr.Add(new JsonObject
            {
                ["name"] = m.Name,
                ["values"] = new JsonArray(m.Values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["strings"] = new JsonArray(m.Strings.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
                ["lists"] = lists,
            });
        }
        root["aimaps"] = arr;
    }
    Directory.CreateDirectory(full);
    var stem = $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}{(actor == null ? "" : "_" + actor)}_seq";
    File.WriteAllText(Path.Combine(full, stem + ".json"), root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(full, stem + ".txt"), text.ToString());
    Console.WriteLine($"{seqs.Count} sequências ({seqs.Values.Sum(c => c.Count)} comandos), {acts.Count} ações ({acts.Values.Sum(c => c.Count)} comandos), {maps.Count} mapas de IA -> {Path.Combine(full, stem)}.json/.txt");
    return 0;
}

// Linha do tempo das entradas de uma gravação do Dolphin: trechos com o analógico/direcional parado ou na mesma direção
// e cada botão apertado, com o quadro de vídeo (2 leituras por quadro nesta gravação, conferido no cabeçalho).
int DtmEntradas(string path, string? csvPath)
{
    var dtm = DW4.Formats.Dolphin.DtmFile.Read(File.ReadAllBytes(path));
    double perFrame = dtm.FrameCount > 0 ? (double)dtm.Inputs.Count / dtm.FrameCount : 1;
    Console.WriteLine($"{dtm.GameId}: {dtm.FrameCount} quadros, {dtm.Inputs.Count} leituras ({perFrame:0.###} por quadro), save state: {dtm.FromSaveState}, atraso: {dtm.LagCount}");
    int Frame(int i) => (int)(i / perFrame);
    string Dir(DW4.Formats.Dolphin.GcPadInput p)
    {
        var (x, y) = p.Stick;
        if (p.Has(DW4.Formats.Dolphin.GcPadInput.Up)) y = 1;
        if (p.Has(DW4.Formats.Dolphin.GcPadInput.Down)) y = -1;
        if (p.Has(DW4.Formats.Dolphin.GcPadInput.Left)) x = -1;
        if (p.Has(DW4.Formats.Dolphin.GcPadInput.Right)) x = 1;
        double mag = Math.Sqrt(x * x + y * y);
        if (mag < 0.2) return "parado";
        string[] names = ["L", "NE", "N", "NO", "O", "SO", "S", "SE"];
        int sector = (int)Math.Round(Math.Atan2(y, x) / (Math.PI / 4) + 8) % 8;
        return $"{names[sector]} {Math.Min(mag, 1):0.0}";
    }
    TextWriter? csv = null;
    if (csvPath != null)
    {
        var full = Path.GetFullPath(csvPath);
        if (InsideGitRepo(Path.GetDirectoryName(full)!)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
        csv = new StreamWriter(full);
        csv.WriteLine("leitura,quadro,botoes,stick_x,stick_y,cstick_x,cstick_y,gatilho_l,gatilho_r");
    }
    string lastDir = "";
    int dirStart = 0;
    ushort lastButtons = 0;
    for (int i = 0; i <= dtm.Inputs.Count; i++)
    {
        var p = i < dtm.Inputs.Count ? dtm.Inputs[i] : default;
        string d = i < dtm.Inputs.Count ? Dir(p) : "fim";
        if (d != lastDir)
        {
            if (i > 0) Console.WriteLine($"  quadros {Frame(dirStart),6}–{Frame(i) - 1,6} ({(Frame(i) - Frame(dirStart)) / 60.0,6:0.00} s) movimento {lastDir}");
            lastDir = d;
            dirStart = i;
        }
        if (i == dtm.Inputs.Count) break;
        ushort pressed = (ushort)(p.Buttons & ~lastButtons & 0x0FFF);
        if (pressed != 0) Console.WriteLine($"  quadro {Frame(i),6}: aperta {DW4.Formats.Dolphin.GcPadInput.Names(pressed)}");
        ushort released = (ushort)(lastButtons & ~p.Buttons & 0x0FFF);
        if (released != 0) Console.WriteLine($"  quadro {Frame(i),6}: solta {DW4.Formats.Dolphin.GcPadInput.Names(released)}");
        lastButtons = p.Buttons;
        csv?.WriteLine(string.Create(inv, $"{i},{Frame(i)},{p.Buttons:X4},{p.StickX},{p.StickY},{p.CStickX},{p.CStickY},{p.TriggerL},{p.TriggerR}"));
    }
    csv?.Dispose();
    return 0;
}

// Extrai a MEM1 de um save state do Dolphin (para achar onde o jogo guarda posição, quadro etc.).
// Tabelas de balanceamento (beDBManagerInfo → beDataBase): um CSV (separador ;) por tabela e um JSON com todas.
int IgbDb(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var notes = new List<string>();
    var tables = IgbDatabase.Read(IgbFile.Read(data), data, notes);
    foreach (var n in notes) Console.WriteLine($"nota: {n}");
    if (tables.Count == 0) { Console.Error.WriteLine("nenhuma tabela beDataBase"); return 1; }
    Directory.CreateDirectory(full);
    string Cell(object v) => v switch
    {
        float x => x.ToString("R", inv),
        bool b => b ? "1" : "0",
        string t => t.Replace("\r", "").Replace("\n", " / ").Replace(";", ","),
        _ => Convert.ToString(v, inv) ?? "",
    };
    var root = new JsonObject();
    foreach (var t in tables)
    {
        var lines = new List<string> { "linha;" + string.Join(";", t.Columns.Select(c => c.Name)) };
        for (int r = 0; r < t.Rows; r++) lines.Add($"{r};" + string.Join(";", t.Columns.Select(c => Cell(c.Cells[r]))));
        File.WriteAllLines(Path.Combine(full, $"db_{t.Name}.csv"), lines, new UTF8Encoding(true));
        root[t.Name] = new JsonObject
        {
            ["rows"] = t.Rows,
            ["columns"] = new JsonArray(t.Columns.Select(c => (JsonNode?)new JsonObject
            {
                ["name"] = c.Name,
                ["kind"] = c.Kind,
                ["cells"] = new JsonArray(c.Cells.Select(v => v switch
                {
                    sbyte x => (JsonNode?)JsonValue.Create((int)x),
                    short x => JsonValue.Create((int)x),
                    int x => JsonValue.Create(x),
                    float x => JsonValue.Create(x),
                    bool x => JsonValue.Create(x),
                    string x => JsonValue.Create(x),
                    _ => null,
                }).ToArray()),
            }).ToArray()),
        };
        Console.WriteLine($"{t.Name,-10} #{t.InfoIndex} → beDataBase #{t.DataIndex}: {t.Rows} linhas, {t.Columns.Count} colunas");
    }
    File.WriteAllText(Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_db.json"),
        root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    return 0;
}

int IgbDropsCmd(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var notes = new List<string>();
    var lists = IgbDrops.Read(IgbFile.Read(data), data, notes);
    foreach (var n in notes) Console.WriteLine($"nota: {n}");
    // Fases sem lista de drop (chefes, campos sem baús): nada a gravar, não é erro.
    if (lists.Count == 0) { Console.WriteLine("nenhuma lista de beGeneraterItemDataOne (fase sem drops)"); return 0; }
    Directory.CreateDirectory(full);
    var root = new JsonObject();
    foreach (var (name, entries) in lists)
    {
        root[name] = new JsonArray(entries.Select(e => (JsonNode?)new JsonObject { ["table"] = e.Table, ["row"] = e.Row, ["rank"] = e.Rank, ["rate"] = e.Rate }).ToArray());
        Console.WriteLine($"{name,-12} {entries.Count} itens, soma {entries.Sum(e => e.Rate)}/10000: {string.Join(" ", entries.Select(e => $"{e.Table}:{e.Row}({e.Rank},{e.Rate})"))}");
    }
    // Gerador → listas por número de jogadores (índice 0 = 1 jogador) e dificuldade (0..2).
    var gens = new JsonObject();
    foreach (var (gname, perPlayers) in IgbDrops.GeneratorLists(IgbFile.Read(data), data))
        gens[gname] = new JsonArray(perPlayers.Select(p => (JsonNode?)new JsonObject
        {
            ["drop"] = new JsonArray(p.Each.Select(n => (JsonNode?)n).ToArray()),
            ["final"] = new JsonArray(p.Last.Select(n => (JsonNode?)n).ToArray()),
        }).ToArray());
    File.WriteAllText(Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_drops.json"),
        new JsonObject { ["lists"] = root , ["generators"] = gens }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

int AdxWav(string path, string outDir, string[] which)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    Directory.CreateDirectory(full);
    foreach (var w in which)
    {
        if (!int.TryParse(w, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine($"índice inválido: {w}"); return 1; }
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[idx], ms);
        var pcm = DW4.Formats.Audio.Adx.Decode(ms.ToArray(), out var h);
        var file = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}.wav");
        File.WriteAllBytes(file, DW4.Formats.Audio.Adx.ToWav(pcm, h));
        Console.WriteLine($"#{idx}: {h.Channels} canais, {h.SampleRate} Hz, {h.Samples} amostras ({h.Samples / (double)h.SampleRate:0.0} s)" +
            (h.Loop ? $", loop {h.LoopStart}..{h.LoopEnd}" : ", sem loop") + $" -> {file}");
    }
    return 0;
}

int IgbSons(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    Directory.CreateDirectory(full);
    var bankList = new JsonArray();
    foreach (var (at, size) in DW4.Formats.Audio.Sfp.Find(data))
    {
        var bytes = data.AsSpan(at, size).ToArray();
        var sfp = DW4.Formats.Audio.Sfp.Read(bytes);
        var cues = new JsonArray();
        foreach (var c in sfp.Cues)
        {
            var file = $"A9{c.Group:X2}_{c.Cue:X2}.wav";
            if (c.Sample >= 0 && c.Sample < sfp.Samples.Count)
            {
                var smp = sfp.Samples[c.Sample];
                var pcm = sfp.Decode(smp, out var ls, out var le);
                File.WriteAllBytes(Path.Combine(full, file), DW4.Formats.Audio.Wav.Write(pcm, 1, smp.Rate, ls, le));
            }
            cues.Add(new JsonObject { ["id"] = $"0xA9{c.Group:X2}{c.Cue:X2}00", ["voice"] = c.Voice, ["sample"] = c.Sample, ["volume"] = c.Volume, ["pan"] = c.Pan, ["file"] = file });
        }
        bankList.Add(new JsonObject { ["offset"] = at, ["size"] = size, ["samples"] = sfp.Samples.Count, ["cues"] = cues });
        Console.WriteLine($"banco em 0x{at:X}: {sfp.Samples.Count} amostras, {sfp.Cues.Count} cues (grupos {string.Join(",", sfp.Cues.Select(c => c.Group.ToString("X2")).Distinct())})");
    }
    var file2 = IgbFile.Read(data);
    var (sounds, banks, se, soundInfo) = IgbSounds.Read(file2, data);
    JsonNode? V(object v) => v switch { string t => JsonValue.Create(t), float x => JsonValue.Create(x), int x => JsonValue.Create(x), bool x => JsonValue.Create(x), float[] a => new JsonArray(a.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), _ => null };
    JsonObject O(IgbSoundObject o) => new() { ["index"] = o.Index, ["values"] = new JsonArray(o.Values.Select(V).ToArray()) };
    var root = new JsonObject
    {
        ["sounds"] = new JsonArray(sounds.Select(o => (JsonNode?)O(o)).ToArray()),
        ["sfpData"] = new JsonArray(banks.Select(o => (JsonNode?)O(o)).ToArray()),
        ["se"] = new JsonArray(se.Select(s => (JsonNode?)new JsonObject { ["name"] = s.Name, ["entries"] = new JsonArray(s.Entries.Select(e => (JsonNode?)O(e)).ToArray()) }).ToArray()),
        ["banks"] = bankList,
        ["soundInfo"] = new JsonArray(soundInfo.Select(i => (JsonNode?)new JsonObject { ["index"] = i.Index, ["strings"] = new JsonArray(i.Strings.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()) }).ToArray()),
    };
    File.WriteAllText(Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_sons.json"),
        root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"{sounds.Count} beSoundData, {banks.Count} beCriSfpData, {se.Count} beSeInfoData");
    return 0;
}

// Atores de interface da fase (i_dmgnum, i_pcdmgnum, i_stpnl03, i_stbase) com o grafo de nós, as sequências, os medidores
// e os números, em JSON, e as texturas em PNG; as texturas por nome vêm do pacote de idioma da fase (usa_pack).
int IgbHudCmd(string[] a, string[]? only)
{
    string path = a[1], which = a[2], outDir = a[^1];
    string? packPath = a.Length == 6 ? a[3] : null, packWhich = a.Length == 6 ? a[4] : null;
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    byte[] Entry(string afsPath, string w)
    {
        using var afs = AfsArchive.Open(afsPath);
        if (!int.TryParse(w, NumberStyles.None, inv, out int i) || i >= afs.Entries.Count) throw new InvalidDataException($"índice inválido: {w}");
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[i], ms);
        return ms.ToArray();
    }
    var data = Entry(path, which);
    byte[]? packData = packPath != null && packWhich != null ? Entry(packPath, packWhich) : null;
    // Painéis de status: i_stpnl01..05 gerados juntos em V<x,-160,0> pelos Lua (ded_g_tcounter00 e outros); i_stbase = menu de status.
    string[] actors = only ?? ["i_dmgnum", "i_pcdmgnum", "i_stpnl01", "i_stpnl02", "i_stpnl03", "i_stpnl04", "i_stpnl05", "i_stbase"];
    var (json, hud) = IgbHud.Export(IgbFile.Read(data), data, packData != null ? IgbFile.Read(packData) : null, packData, actors);
    Directory.CreateDirectory(full);
    foreach (var img in hud.Images)
    {
        using var fs = File.Create(Path.Combine(full, img.File));
        Png.WriteRgba(fs, img.Width, img.Height, img.Rgba);
    }
    json["source"] = $"{Path.GetFileName(path)} #{which}" + (packPath != null ? $" + {Path.GetFileName(packPath)} #{packWhich}" : "");
    var file = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{int.Parse(which, inv):D4}_hud.json");
    File.WriteAllText(file, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
    Console.WriteLine($"{json["actors"]!.AsArray().Count} atores, {json["nodes"]!.AsObject().Count} nós, {json["meters"]!.AsArray().Count} medidores, {json["numbers"]!.AsArray().Count} números, {hud.Images.Count} imagens -> {file}");
    foreach (var n in hud.Notes) Console.WriteLine($"nota: {n}");
    return 0;
}

// Objetos de cenário rígidos (portas, pisos, caixas…: KIND_OBJECT sem esqueleto, animados por igTransformSequence) no
// mesmo formato do igb-hud, para a fatia gerar pelos GENERATE dos scripts. Ator "nome" (raiz = igAttrSet com o nome) ou
// "nome=índice" (raiz explícita).
int IgbObjetos(string path, string which, string outDir, string[] actors, string? packSpec = null)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int i) || i >= afs.Entries.Count) throw new InvalidDataException($"índice inválido: {which}");
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[i], ms);
    var data = ms.ToArray();
    // Pacote opcional para as texturas por nome (lh_pack nos campos, usa_pack nas masmorras).
    byte[]? packData = null;
    if (packSpec != null && packSpec.LastIndexOf(':') is int colon and > 0)
    {
        using var pa = AfsArchive.Open(packSpec[..colon]);
        using var pms = new MemoryStream();
        pa.CopyEntry(pa.Entries[int.Parse(packSpec[(colon + 1)..], inv)], pms);
        packData = pms.ToArray();
    }
    var (json, hud) = IgbHud.Export(IgbFile.Read(data), data, packData != null ? IgbFile.Read(packData) : null, packData, actors);
    Directory.CreateDirectory(full);
    foreach (var img in hud.Images)
    {
        using var fs = File.Create(Path.Combine(full, img.File));
        Png.WriteRgba(fs, img.Width, img.Height, img.Rgba);
    }
    json["source"] = $"{Path.GetFileName(path)} #{which}";
    var file = Path.Combine(full, "objetos.json");
    File.WriteAllText(file, json.ToJsonString());
    Console.WriteLine($"{json["actors"]!.AsArray().Count} atores, {json["nodes"]!.AsObject().Count} nós, {hud.Images.Count} imagens -> {file}");
    foreach (var n in hud.Notes) Console.WriteLine($"nota: {n}");
    return 0;
}

// Geradores de onda de um generatorN/H/V em geradores.json, no formato que a fatia lê (Fatia.Enemies.cs).
int IgbGeradores(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var data = AfsEntryBytes(path, which);
    var gens = IgbGenerators.ReadGenerators(IgbFile.Read(data), data);
    JsonArray V(IEnumerable<float> v) => new(v.Select(x => (JsonNode?)Math.Round((double)x, 4)).ToArray());
    var arr = new JsonArray(gens.Select(g => (JsonNode?)new JsonObject
    {
        ["name"] = g.Name,
        ["positions"] = new JsonArray(g.Positions.Select(p => (JsonNode?)V([p.X, p.Y, p.Z])).ToArray()),
        ["rotYDeg"] = V(g.RotYDeg),
        ["count"] = new JsonArray(g.Count.Select(c => (JsonNode?)c).ToArray()),
        ["maxCount"] = new JsonArray(g.MaxCount.Select(c => (JsonNode?)c).ToArray()),
        ["enemyRow"] = g.EnemyRow,
        ["callLua"] = g.CallLua,
        ["global"] = g.Global,
        ["startMode"] = g.StartMode, ["startData"] = g.StartData, ["fillMode"] = g.FillMode, ["fillData"] = g.FillData,
    }).ToArray());
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, "geradores.json");
    File.WriteAllText(file, new JsonObject { ["source"] = $"{Path.GetFileName(path)} #{which}", ["generators"] = arr }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    foreach (var g in gens) Console.WriteLine($"{g.Name}: {g.Positions.Count} vagas, inimigo {g.EnemyRow}, {string.Join("/", g.Count)} no total, {string.Join("/", g.MaxCount)} ao mesmo tempo, {g.CallLua}");
    Console.WriteLine($"{gens.Count} geradores -> {file}");
    return 0;
}

// Volumes de gatilho de área (beMatCtrlData) do IGB principal da fase em gatilhos.json.
// Disparadores de ação (beActionStarterInfo): o modelo X (efeito, projétil x_*/y_*) nasce quando o modelo alvo toca uma ação.
// FATO (#744, stagbs03): beActionStarterInfo [@2 nome de X, @7 → beAsTargetModelDataList] → beAsTargetModelData [@2 modelo alvo,
// @3 → beActionStarterDataList] → beActionStarterData [@2 ação do alvo, @3 sequência de X, @4 quadro, @5 deslocamento, @6, @7].
// HIPÓTESES: X nasce no quadro @4 da ação, no alvo + deslocamento (no espaço do alvo); @6/@7 não lidos.
int IgbDisparos(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var data = AfsEntryBytes(path, which);
    var f = IgbFile.Read(data);
    var scene = IgbScene.Read(f, data);
    bool le = f.Header.LittleEndian;
    float F(byte[] m, int o) => le ? BitConverter.ToSingle(m, o) : BitConverter.ToSingle([m[o + 3], m[o + 2], m[o + 1], m[o]], 0);
    IgbFieldValue? Field(IgbObject o, string type, int slot) => o.Fields.FirstOrDefault(v => v.Desc.ShortName == type && v.Desc.Slot == slot);
    IgbObject? Ref(IgbObject o, int slot) => Field(o, "ObjectRef", slot) is { } v && v.AsInt32(le) is int i && i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
    var list = new JsonArray();
    int entries = 0;
    foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beActionStarterInfo"))
    {
        var targets = new JsonArray();
        if (Ref(info, 7) is { } tl)
            foreach (var t in scene.ObjectList(tl).Where(o => o.TypeName == "beAsTargetModelData"))
            {
                var acts = new JsonArray();
                if (Ref(t, 3) is { } dl)
                    foreach (var d in scene.ObjectList(dl).Where(o => o.TypeName == "beActionStarterData"))
                    {
                        var off = Field(d, "Vec3f", 5)?.Raw is { Length: >= 12 } r ? new JsonArray(F(r, 0), F(r, 4), F(r, 8)) : new JsonArray(0, 0, 0);
                        acts.Add(new JsonObject
                        {
                            ["acao"] = Field(d, "String", 2)?.Text ?? "", ["sequencia"] = Field(d, "String", 3)?.Text ?? "",
                            ["quadro"] = Field(d, "Int", 4)?.AsInt32(le) ?? 0, ["deslocamento"] = off,
                            ["b6"] = Field(d, "Bool", 6)?.Raw is { Length: > 0 } b6 && b6[0] != 0, ["b7"] = Field(d, "Bool", 7)?.Raw is { Length: > 0 } b7 && b7[0] != 0,
                        });
                        entries++;
                    }
                targets.Add(new JsonObject { ["modelo"] = Field(t, "String", 2)?.Text ?? "", ["acoes"] = acts });
            }
        list.Add(new JsonObject { ["nome"] = Field(info, "String", 2)?.Text ?? "", ["alvos"] = targets });
    }
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, "disparos.json");
    File.WriteAllText(file, new JsonObject { ["source"] = $"{Path.GetFileName(path)} #{which}", ["disparos"] = list }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    // Sequências dos projéteis x_* (os y_* são só efeito visual): MOVE, HITBOX 7xxxx e LUACALL "bht" (cada um em disparos/<nome>).
    foreach (var name in list.Select(n => n!["nome"]!.GetValue<string>()).Where(n => n.StartsWith("x_", StringComparison.Ordinal)).Distinct())
        IgbSeq(path, which, Path.Combine(full, "disparos", name), "--ator=" + name);
    // Modelos dos x_* (FATO: atores com malha e textura próprias, ex. x_035fblst02a com x_beem02a/b): o cliente desenha com eles.
    var shotNames = list.Select(n => n!["nome"]!.GetValue<string>()).Where(n => n.StartsWith("x_", StringComparison.Ordinal)).Distinct().ToArray();
    if (shotNames.Length > 0) IgbObjetos(path, which, Path.Combine(full, "disparos"), shotNames);
    Console.WriteLine($"{list.Count} disparadores, {entries} entradas -> {file}");
    return 0;
}

int IgbGatilhos(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var data = AfsEntryBytes(path, which);
    var vols = IgbGenerators.ReadTriggers(IgbFile.Read(data), data);
    JsonArray V((float X, float Y, float Z) p) => new((JsonNode?)Math.Round((double)p.X, 4), Math.Round((double)p.Y, 4), Math.Round((double)p.Z, 4));
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, "gatilhos.json");
    File.WriteAllText(file, new JsonObject { ["source"] = $"{Path.GetFileName(path)} #{which}", ["volumes"] = new JsonArray(vols.Select(v => (JsonNode?)new JsonObject { ["name"] = v.Name, ["min"] = V(v.Min), ["max"] = V(v.Max) }).ToArray()) }
        .ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{vols.Count} volumes ({string.Join(", ", vols.Select(v => v.Name))}) -> {file}");
    return 0;
}

byte[] AfsEntryBytes(string path, string which)
{
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) throw new InvalidDataException($"índice inválido: {which}");
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    return ms.ToArray();
}

// main.dol só como dados: desmontagem estática para achar as fórmulas (FightCom/BLT*), sem executar nada.
int DolInfo(string path)
{
    var dol = DW4.Formats.Ppc.DolFile.Read(File.ReadAllBytes(path));
    foreach (var s in dol.Sections) Console.WriteLine($"{s.Name,-6} arquivo 0x{s.Offset:X6} -> 0x{s.Address:X8}, 0x{s.Size:X} bytes");
    Console.WriteLine($"bss 0x{dol.BssAddress:X8}, 0x{dol.BssSize:X}; entrada 0x{dol.Entry:X8}");
    var (r13, r2) = dol.SmallDataBases();
    Console.WriteLine($"r13 = {(r13 is { } a ? $"0x{a:X8}" : "?")}, r2 = {(r2 is { } b ? $"0x{b:X8}" : "?")}");
    return 0;
}

static bool TryHex(string text, out uint value) =>
    uint.TryParse(text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

int DolDesmontar(string path, string addr, string count)
{
    if (!TryHex(addr, out uint a) || !int.TryParse(count, out int n) || n <= 0) { Console.Error.WriteLine("endereço ou contagem inválidos"); return 1; }
    var dol = DW4.Formats.Ppc.DolFile.Read(File.ReadAllBytes(path));
    foreach (var line in DW4.Formats.Ppc.PpcDisassembler.Listing(dol, a & ~3u, Math.Min(n, 20000))) Console.WriteLine(line);
    return 0;
}

int DolRefs(string path, string what)
{
    var dol = DW4.Formats.Ppc.DolFile.Read(File.ReadAllBytes(path));
    var (r13, r2) = dol.SmallDataBases();
    // Endereço: "0x8045…" ou 8 dígitos hexadecimais que caem numa seção; o resto é texto.
    bool isAddress = TryHex(what, out uint a) && (what.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || (what.Length == 8 && dol.SectionAt(a) != null));
    var targets = isAddress ? new List<uint> { a } : dol.FindString(what).ToList();
    if (targets.Count == 0) { Console.Error.WriteLine("não encontrado"); return 1; }
    foreach (var t in targets)
    {
        Console.WriteLine($"0x{t:X8}{(dol.CString(t, 64) is { Length: > 0 } s ? $" \"{s}\"" : "")}");
        foreach (var p in dol.FindPointers(t)) Console.WriteLine($"  ponteiro em 0x{p:X8} (vizinhos: {string.Join(" ", Enumerable.Range(-2, 5).Select(k => dol.TryWord((uint)(p + k * 4), out uint w) ? $"{w:X8}" : "--------"))})");
        foreach (var c in dol.FindCodeRefs(t, r13, r2)) Console.WriteLine($"  código em 0x{c:X8}");
    }
    return 0;
}

// Taxas de crescimento de skill do P2ESKILLUP: int16 big-endian, 4 × 21 × 3, em 0x804EAF18 (lido em 2026-10-24).
int DolSkillTab(string path, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var dol = DW4.Formats.Ppc.DolFile.Read(File.ReadAllBytes(path));
    const uint At = 0x804EAF18;
    long off = dol.OffsetOf(At);
    if (off < 0 || off + 4 * 21 * 3 * 2 > dol.Data.Length) { Console.Error.WriteLine("tabela fora das seções"); return 1; }
    var levels = new JsonArray();
    for (int i = 0; i < 4; i++)
    {
        var bands = new JsonArray();
        for (int j = 0; j < 21; j++)
        {
            var cols = new JsonArray();
            for (int k = 0; k < 3; k++)
                cols.Add((JsonNode?)JsonValue.Create((int)System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(dol.Data.AsSpan((int)off + 2 * ((i * 21 + j) * 3 + k)))));
            bands.Add(cols);
        }
        levels.Add(bands);
    }
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, "skillup.json");
    File.WriteAllText(file, new JsonObject { ["source"] = $"main.dol 0x{At:X8}", ["rates"] = levels }.ToJsonString());
    Console.WriteLine($"4 × 21 × 3 taxas -> {file}; primeira faixa: {levels[0]![0]!.ToJsonString()}, última: {levels[3]![20]!.ToJsonString()}");
    return 0;
}

int DolphinRam(string savPath, string outPath)
{
    var full = Path.GetFullPath(outPath);
    if (InsideGitRepo(Path.GetDirectoryName(full)!)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    var file = File.ReadAllBytes(savPath);
    var gameId = Encoding.ASCII.GetString(file, 0, 6);
    var state = DW4.Formats.Dolphin.SaveState.Decompress(file);
    int at = DW4.Formats.Dolphin.SaveState.FindMem1(state, gameId);
    Console.WriteLine($"{gameId}: estado descomprimido {state.Length} B; MEM1 em 0x{at:X}");
    if (at < 0) return 1;
    File.WriteAllBytes(full, state.AsSpan(at, DW4.Formats.Dolphin.SaveState.Mem1Size).ToArray());
    Console.WriteLine($"MEM1 -> {full}");
    return 0;
}

// Compara duas MEM1 separadas por N quadros: (1) u32 que avançaram N, N/2 ou 2N (contadores de quadro); (2) trincas de
// float (x, y, z) com y perto do chão dado e x, z dentro de ±1000 nas duas, mostrando quais mudaram (posições candidatas).
int RamBusca(string aPath, string bPath, string framesArg, string[] options)
{
    var a = File.ReadAllBytes(aPath);
    var b = File.ReadAllBytes(bPath);
    long frames = long.Parse(framesArg, inv);
    float floor = 40;
    foreach (var o in options.Where(o => o.StartsWith("--y=", StringComparison.Ordinal))) floor = float.Parse(o[4..], inv);
    int n = Math.Min(a.Length, b.Length);
    Console.WriteLine($"contadores (u32, alinhados) com avanço ≈ {frames}, {frames / 2} ou {frames * 2} (±3):");
    int shown = 0;
    for (int i = 0; i + 4 <= n && shown < 60; i += 4)
    {
        long d = (long)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i)) - BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(i));
        foreach (var k in new[] { frames, frames / 2, frames * 2 })
            if (Math.Abs(d - k) <= 3) { Console.WriteLine($"  0x{0x80000000 + i:X8}: {BinaryPrimitives.ReadUInt32BigEndian(a.AsSpan(i))} -> {BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(i))} (Δ {d})"); shown++; break; }
    }
    static float F(byte[] m, int i) => BinaryPrimitives.ReadSingleBigEndian(m.AsSpan(i));
    static bool Plausible(float x, float y, float z, float floor) =>
        float.IsFinite(x) && float.IsFinite(z) && Math.Abs(y - floor) < 0.5f && Math.Abs(x) < 1000 && Math.Abs(z) < 1000 && (Math.Abs(x) > 0.01f || Math.Abs(z) > 0.01f);
    Console.WriteLine($"trincas (x, y≈{floor}, z) plausíveis nas duas e que mudaram:");
    shown = 0;
    for (int i = 0; i + 12 <= n && shown < 200; i += 4)
    {
        float ax = F(a, i), ay = F(a, i + 4), az = F(a, i + 8), bx = F(b, i), by = F(b, i + 4), bz = F(b, i + 8);
        if (!Plausible(ax, ay, az, floor) || !Plausible(bx, by, bz, floor)) continue;
        if (Math.Abs(ax - bx) < 0.01f && Math.Abs(az - bz) < 0.01f) continue;
        Console.WriteLine(string.Create(inv, $"  0x{0x80000000 + i:X8}: ({ax:0.###}, {ay:0.###}, {az:0.###}) -> ({bx:0.###}, {by:0.###}, {bz:0.###})"));
        shown++;
    }
    return 0;
}

// Diagnóstico do servidor GDB do Dolphin: mostra as respostas brutas de leitura antes e depois de continuar e interromper.
int DolphinGdbTeste(string portArg)
{
    using var gdb = DW4.Formats.Dolphin.GdbClient.Connect("127.0.0.1", int.Parse(portArg, inv), TimeSpan.FromMinutes(2));
    string Show(string r) => r.Length > 80 ? r[..80] + "…" : r;
    Console.WriteLine($"?            -> {Show(gdb.Query("?"))}");
    Console.WriteLine($"qSupported   -> {Show(gdb.Query("qSupported"))}");
    foreach (var (addr, len) in new (uint, int)[] { (0x80000000, 8), (0x00000000, 8), (0x814541D8, 4), (0x014541D8, 4) })
        Console.WriteLine($"m{addr:x},{len:x} (parado)  -> \"{Show(gdb.ReadRaw(addr, len))}\"");
    Console.WriteLine($"g            -> {Show(gdb.Query("g"))}");
    gdb.Continue();
    Thread.Sleep(500);
    Console.WriteLine($"Ctrl-C       -> {Show(gdb.Interrupt())}");
    for (int i = 0; i < 5; i++)
    {
        Console.WriteLine($"m80000000,8 (depois, tentativa {i}) -> \"{Show(gdb.ReadRaw(0x80000000, 8))}\"");
        Console.WriteLine($"m814541d8,4 (depois, tentativa {i}) -> \"{Show(gdb.ReadRaw(0x814541D8, 4))}\"");
        Thread.Sleep(100);
    }
    foreach (var st in gdb.Stray) Console.WriteLine($"byte inesperado: {st}");
    try { gdb.Continue(); } catch (IOException) { }
    return 0;
}

// Acompanha a reprodução de um filme no Dolphin pelo servidor GDB: para a cada ~100 ms, lê o contador de 30 Hz do jogo
// (0x814541D8, achado comparando save states; quadro de vídeo ≈ 2 × contador + 6 nesta gravação) e grava a MEM1 inteira
// quando o quadro passa de cada alvo. Com --amostra, grava também um trecho pequeno de memória em toda parada (CSV).
int DolphinGdb(string portArg, string outDir, string[] rest)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    Directory.CreateDirectory(full);
    uint counterAddr = 0x814541D8;
    int runMs = 300, stallSeconds = 120;
    var targets = new List<long>();
    var samples = new List<(uint Addr, int Len)>();
    foreach (var r in rest)
    {
        if (r.StartsWith("--contador=", StringComparison.Ordinal)) counterAddr = Convert.ToUInt32(r[11..], 16);
        else if (r.StartsWith("--continuar=", StringComparison.Ordinal)) runMs = int.Parse(r[12..], inv);
        else if (r.StartsWith("--parado=", StringComparison.Ordinal)) stallSeconds = int.Parse(r[9..], inv);
        else if (r.StartsWith("--amostra=", StringComparison.Ordinal)) { var p = r[10..].Split(','); samples.Add((Convert.ToUInt32(p[0], 16), int.Parse(p[1], inv))); }
        else targets.Add(long.Parse(r, inv));
    }
    targets.Sort();
    using var gdb = DW4.Formats.Dolphin.GdbClient.Connect("127.0.0.1", int.Parse(portArg, inv), TimeSpan.FromMinutes(2));
    Console.WriteLine($"conectado; parada inicial: {gdb.Query("?")}");
    using var log = new StreamWriter(Path.Combine(full, "amostras.csv"));
    log.WriteLine("hora_ms,pc,contador,quadro,pilha_hash" + string.Concat(samples.Select(s => $",0x{s.Addr:X8}")));
    var clock = System.Diagnostics.Stopwatch.StartNew();
    int ti = 0;
    uint lastCounter = uint.MaxValue;
    var lastChange = clock.Elapsed;
    while (ti < targets.Count || samples.Count > 0)
    {
        gdb.Continue();
        Thread.Sleep(runMs);
        var stop = gdb.Interrupt();
        // PC na resposta de parada ("T05" + "40:XXXXXXXX;…") e um resumo da pilha, para saber se a emulação anda.
        int pcAt = stop.IndexOf("40:", StringComparison.Ordinal);
        string pc = pcAt >= 0 && pcAt + 11 <= stop.Length ? stop.Substring(pcAt + 3, 8) : "?";
        uint counter = BinaryPrimitives.ReadUInt32BigEndian(gdb.Read(counterAddr, 4));
        long frame = 2L * counter + 6;
        var stack = gdb.Read(0x80576000, 0x1000);
        uint stackHash = 2166136261;
        foreach (var bt in stack) stackHash = (stackHash ^ bt) * 16777619;
        var line = new StringBuilder(string.Create(inv, $"{clock.ElapsedMilliseconds},{pc},{counter},{frame},{stackHash:X8}"));
        foreach (var (addr, len) in samples) line.Append(',').Append(Convert.ToHexString(gdb.Read(addr, len)));
        log.WriteLine(line);
        if (counter != lastCounter) { lastCounter = counter; lastChange = clock.Elapsed; }
        else if (clock.Elapsed - lastChange > TimeSpan.FromSeconds(stallSeconds)) { Console.WriteLine($"contador parado há {stallSeconds} s: fim do filme?"); break; }
        while (ti < targets.Count && frame >= targets[ti])
        {
            var mem = new byte[DW4.Formats.Dolphin.SaveState.Mem1Size];
            for (int at = 0; at < mem.Length; at += 0x1000) gdb.Read(0x80000000u + (uint)at, 0x1000).CopyTo(mem, at);
            var file = Path.Combine(full, $"q{frame:D5}.bin");
            File.WriteAllBytes(file, mem);
            Console.WriteLine($"alvo {targets[ti]}: quadro {frame} (contador {counter}) -> {file}");
            ti++;
        }
    }
    try { gdb.Continue(); } catch (IOException) { }
    foreach (var st in gdb.Stray) Console.WriteLine($"byte inesperado: {st}");
    Console.WriteLine($"fim: {clock.Elapsed.TotalSeconds:0} s");
    return 0;
}

// Grava os volumes de câmera de uma fase (beCameraBoxData) em JSON: caixa, recuo A e pivô/inclinação B por volume.
int IgbCamera(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var cam = IgbCameraSet.Read(IgbFile.Read(data), data);
    foreach (var n in cam.Notes) Console.WriteLine($"nota: {n}");
    if (cam.Boxes.Count == 0) { Console.Error.WriteLine("nenhum volume de câmera"); return 1; }
    JsonArray Arr(float[] v) => new(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    var boxes = new JsonArray();
    foreach (var b in cam.Boxes)
    {
        boxes.Add(new JsonObject { ["index"] = b.Index, ["min"] = Arr(b.Min), ["max"] = Arr(b.Max), ["a"] = Arr(b.A), ["b"] = Arr(b.B) });
        double tilt = Math.Acos(Math.Clamp(b.B[5], -1, 1)) * 180 / Math.PI;
        Console.WriteLine(string.Create(inv, $"{b.Index,2}: ({b.Min[0]}, {b.Min[1]}, {b.Min[2]}) -> ({b.Max[0]}, {b.Max[1]}, {b.Max[2]})  d = {b.Distance}  inclinação {tilt:0.#}°  B.t = ({b.B[12]}, {b.B[13]}, {b.B[14]})"));
    }
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_camera.json");
    var root = new JsonObject { ["source"] = $"{Path.GetFileName(path)} #{idx}", ["common"] = Arr(cam.Common), ["boxes"] = boxes };
    File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{cam.Boxes.Count} volumes -> {file}");
    return 0;
}

// Exporta uma fase inteira no layout da fatia (<saída>\<fase>\cenario, colisao, lua, objetos…) e grava fase.json com as
// entradas usadas, os arquivos gerados, atores de mapa, músicas e inimigos. Os blocos saem do índice do ndmw.igb (IgbStageIndex).
int FaseExportar(string ndmwPath, string areaPath, string stage, string outRoot)
{
    var outDir = Path.GetFullPath(Path.Combine(outRoot, stage));
    if (InsideGitRepo(outDir)) { Console.Error.WriteLine($"recusado: {outDir} está dentro de um repositório Git"); return 1; }
    var igbPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ndmwPath))!, "ndmw.igb");
    var igbData = File.ReadAllBytes(igbPath);
    var index = IgbStageIndex.Read(IgbFile.Read(igbData), igbData);
    byte[] Entry(string afsPath, int i)
    {
        using var afs = AfsArchive.Open(afsPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[i], ms);
        return ms.ToArray();
    }
    string AfsPath(int afs) => afs == 1 ? areaPath : ndmwPath;
    if (index.Stub(stage) is not { } stub) { Console.Error.WriteLine($"fase {stage} não está no índice do ndmw.igb"); return 1; }
    var stubData = Entry(AfsPath(stub.Afs), stub.Index);
    var refs = IgbStageIndex.StubRefs(IgbFile.Read(stubData), stubData);
    int main = refs.FirstOrDefault(r => !r.Sound && r.Afs == 0)?.Index ?? -1;
    int sound = refs.FirstOrDefault(r => r.Sound && r.Afs == 0)?.Index ?? -1;
    if (main < 0) { Console.Error.WriteLine($"o esboço {stub.Name} (#{stub.Index}) não lista o IGB principal"); return 1; }
    var sep = index.Separate(stage).ToList();
    int Sep(string name, int afs = 0) => sep.FirstOrDefault(x => x.Afs == afs && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
    int cam = Sep("aaacamera.igb"), camFar = Sep("aaacameral.igb"), acmr = Sep("acmr_pack.igb");
    // Texturas por nome do cenário e dos objetos: lh_pack nos campos (FATO: ls_pack tem as mesmas 20 texturas, mais claras; HIPÓTESE:
    // variante de iluminação), usa_pack nas masmorras.
    int lh = Sep("lh_pack.igb");
    int genH = Sep("generatorh.igb"), genN = Sep("generatorn.igb"), genV = Sep("generatorv.igb"), usa = Sep("usa_pack.igb", 1);
    string M = main.ToString(inv);
    Directory.CreateDirectory(outDir);

    var steps = new JsonArray();
    var problems = new JsonArray();
    void Step(string what, Func<int> run)
    {
        int rc;
        string? error = null;
        try { rc = run(); }
        catch (Exception ex) { rc = -1; error = ex.Message; }
        steps.Add(new JsonObject { ["passo"] = what, ["ok"] = rc == 0, ["erro"] = error });
        if (rc != 0) problems.Add($"{what}: {(error ?? $"código {rc}")}");
    }

    // Raízes de cena pelo nome (FATO na dng1: "BG01" = colisão #1862, "BG02" = cenário #2877).
    var mainData = Entry(ndmwPath, main);
    var mainFile = IgbFile.Read(mainData);
    bool le = mainFile.Header.LittleEndian;
    var allParts = IgbScene.Read(mainFile, mainData).Parts;
    var roots = allParts.Select(p => p.SceneRoot).Distinct()
        .Select(r => (Index: r, Name: r >= 0 && r < mainFile.Slots.Count && mainFile.Slots[r] is IgbObject o ? o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "" : ""))
        .ToList();
    // FATO na dng1: há 4 raízes "BG01" (#401, #1677, #2776 e #1862); a colisão é a com mais geometrias (#1862, 28 partes).
    // FATO (stagml14): uma raiz "BG01" com 4 partes e nenhum triângulo (#766) e outra com a caixa de 24 (#1589): conta triângulos
    // primeiro e, no empate, partes.
    int Biggest(string name) => roots.Where(r => r.Name == name)
        .OrderByDescending(r => allParts.Where(p => p.SceneRoot == r.Index).Sum(p => p.Triangles.Count))
        .ThenByDescending(r => allParts.Count(p => p.SceneRoot == r.Index)).Select(r => r.Index).DefaultIfEmpty(-1).First();
    int bg01 = Biggest("BG01"), bg02 = Biggest("BG02");

    // Atores por tipo (beModelCtrlInfo [@2 nome, @7 KIND_*]).
    var actors = mainFile.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beModelCtrlInfo")
        .Select(o => (Name: o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "",
                      Kind: o.Fields.FirstOrDefault(v => v.Desc.Slot == 7 && v.Desc.ShortName == "String")?.Text ?? ""))
        .Distinct().ToList();
    // KIND_OBJECT g_/n_ e também os g_ de KIND_ENEMY (rígidos com hit_*.lua, FATO: g_b01cpole, as colunas do stagbs01).
    var objectNames = actors.Where(a => (a.Kind == "KIND_OBJECT" && (a.Name.StartsWith("g_") || a.Name.StartsWith("n_"))) || (a.Kind == "KIND_ENEMY" && a.Name.StartsWith("g_")) || (a.Kind == "KIND_OBJECT" && a.Name.StartsWith("f_"))
        // Inimigos sem esqueleto (FATO: f_luce2 do stagbs03, "sem esqueleto"): vão como objeto rígido, e o cliente usa o ator como modelo.
        || (a.Kind == "KIND_ENEMY" && (a.Name.StartsWith("e_") || a.Name.StartsWith("f_")) && IgbCharacter.Read(mainFile, mainData, a.Name).Bones.Count == 0)).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    var mapNames = actors.Where(a => a.Kind is "KIND_MAPBASE" or "KIND_MAPCURSOR").Select(a => a.Name).ToArray();
    var menuNames = actors.Where(a => a.Name.StartsWith("i_st") && !a.Name.StartsWith("i_stpnl") && !a.Name.StartsWith("i_stdownic")).Select(a => a.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    // Raiz de cada ator: o @7 do beAction2Info com o mesmo nome (FATO: n_recorderdng → igSwitch #23790 na dng1, sem igAttrSet
    // com o nome). Vai como "nome=índice" para o igb-objetos/igb-hud.
    var action2Root = mainFile.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beAction2Info")
        .Select(o => (Name: o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "",
                      Root: o.Fields.FirstOrDefault(v => v.Desc.Slot == 7 && v.Desc.ShortName == "ObjectRef")?.AsInt32(le) ?? -1))
        .Where(x => x.Root >= 0).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().Root);
    // Só quando não há igAttrSet com o nome (é ele que leva a textura; o switch do beAction2Info fica abaixo dele).
    var attrSets = mainFile.Slots.OfType<IgbObject>().Where(o => o.TypeName == "igAttrSet").Select(o => o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "").ToHashSet();
    string[] Specs(IEnumerable<string> names) => names.Select(n => !attrSets.Contains(n) && action2Root.TryGetValue(n, out int r) ? $"{n}={r}" : n).ToArray();

    string? TexturePack() => lh >= 0 ? $"{ndmwPath}:{lh}" : usa >= 0 ? $"{areaPath}:{usa}" : null;
    Step("lua", () => LuaExtrair(ndmwPath, main, Path.Combine(outDir, "lua")));
    if (bg02 >= 0) Step("cenario", () => IgbCena(ndmwPath, M, bg02.ToString(inv), Path.Combine(outDir, "cenario"), lh >= 0 ? $"{ndmwPath}:{lh}" : null));
    else problems.Add("cenario: sem raiz BG02");
    // Luz "Suave" (FATO, boot.lua: SYSgetLightMode() == 0 → LS_pack.igb, senão LH_pack.igb): as mesmas texturas do ls_pack em
    // cenario/<cena>/ls e um cena_ls.gltf que aponta para elas; o cliente escolhe pela opção de luz.
    int ls = Sep("ls_pack.igb");
    if (bg02 >= 0 && ls >= 0) Step("cenario luz suave", () =>
    {
        var tmp = Path.Combine(outDir, "cenario_ls_tmp");
        int rc = IgbCena(ndmwPath, M, bg02.ToString(inv), tmp, $"{ndmwPath}:{ls}");
        if (rc != 0 || !Directory.Exists(tmp)) return rc;
        foreach (var dir in Directory.GetDirectories(tmp))
        {
            var target = Path.Combine(outDir, "cenario", Path.GetFileName(dir));
            var gltf = Path.Combine(target, "cena.gltf");
            if (!File.Exists(gltf)) continue;
            var lsDir = Path.Combine(target, "ls");
            Directory.CreateDirectory(lsDir);
            var names = new HashSet<string>();
            foreach (var png in Directory.GetFiles(dir, "pack_*.png")) { File.Copy(png, Path.Combine(lsDir, Path.GetFileName(png)), true); names.Add(Path.GetFileName(png)); }
            var text = File.ReadAllText(gltf);
            foreach (var n in names) text = text.Replace($"\"uri\": \"{n}\"", $"\"uri\": \"ls/{n}\"");
            File.WriteAllText(Path.Combine(target, "cena_ls.gltf"), text);
        }
        Directory.Delete(tmp, true);
        return 0;
    });
    // Sem triângulo de colisão (ex.: #766 da stagml14 só tem marcadores de entrada), tenta as outras raízes "BG01".
    if (bg01 >= 0) Step("colisao", () =>
    {
        var colDir = Path.Combine(outDir, "colisao");
        int rc = 0;
        foreach (var r in roots.Where(r => r.Name == "BG01").Select(r => r.Index).OrderBy(r => r == bg01 ? 0 : 1))
        {
            rc = IgbColisao(ndmwPath, M, r.ToString(inv), colDir);
            var one = Path.Combine(colDir, $"ndmw_{main:D4}_colisao_{r}");
            var bin = Path.Combine(one, "colisao.bin");
            if (File.Exists(bin) && new FileInfo(bin).Length > 0) { bg01 = r; break; }
            if (Directory.Exists(one)) Directory.Delete(one, true);
        }
        return rc;
    });
    else problems.Add("colisao: sem raiz BG01");
    if (cam >= 0) Step("camera", () => IgbCamera(ndmwPath, cam.ToString(inv), outDir));
    if (camFar >= 0) Step("camera afastada", () => IgbCamera(ndmwPath, camFar.ToString(inv), outDir));
    if (acmr >= 0) Step("camera-demo", () => IgbCameraDemoCmd(ndmwPath, acmr.ToString(inv), Path.Combine(outDir, "camera-demo")));
    // FATO no stag0101: sem aaacamera e sem acmr_pack, as câmeras ficam no principal (#614): beCameraCtrlInfo "Scene Graph"
    // (beCameraBoxData) e 8 beCameraDemoData (Sdc0101a/b/c, camsp0401..0404, campoint00).
    bool mainBox = mainFile.Slots.OfType<IgbObject>().Any(o => o.TypeName == "beCameraBoxData");
    bool mainDemo = mainFile.Slots.OfType<IgbObject>().Any(o => o.TypeName == "beCameraDemoData");
    if (cam < 0 && mainBox) Step("camera (principal)", () => IgbCamera(ndmwPath, M, outDir));
    if (mainDemo) Step("camera-demo (principal)", () => IgbCameraDemoCmd(ndmwPath, M, Path.Combine(outDir, "camera-demo")));
    if (genN >= 0) Step("geradores", () => IgbGeradores(ndmwPath, genN.ToString(inv), outDir));
    if (genN >= 0) Step("drops", () => IgbDropsCmd(ndmwPath, genN.ToString(inv), Path.Combine(outDir, "drops")));
    Step("gatilhos", () => IgbGatilhos(ndmwPath, M, outDir));
    Step("disparos", () => IgbDisparos(ndmwPath, M, outDir));
    Step("pontos de entrada", () => IgbEntradas(ndmwPath, M, outDir));
    if (usa >= 0)
    {
        Step("textos", () => IgbTextos(areaPath, usa.ToString(inv), Path.Combine(outDir, "textos")));
        Step("db", () => IgbDb(areaPath, usa.ToString(inv), Path.Combine(outDir, "db")));
    // Tabelas do banco da própria fase (FATO: o #829 da Central Area tem shopWeapon/shopArmor/shopChip/shopJunk/shopDisk).
    if (IgbDatabase.Read(mainFile, mainData, []).Count > 0) Step("db da fase", () => IgbDb(ndmwPath, M, Path.Combine(outDir, "db")));
        Step("hud", () => IgbHudCmd(["igb-hud", ndmwPath, M, areaPath, usa.ToString(inv), Path.Combine(outDir, "hud")], null));
        if (menuNames.Length > 0)
            Step("menu", () => IgbHudCmd(["igb-hud", ndmwPath, M, areaPath, usa.ToString(inv), Path.Combine(outDir, "menu")], Specs(menuNames)));
        // Janelas 2D dos scripts (fala i_talkwn/i_messwn, teclas i_talkky/i_messky, escolha i_messcr, troca de área i_zzstchg*,
        // fade, rostos *ic…), com as sequências de cada uma (SQ_OPENL → OPENL → JOINTSET 10..26, FATO no #821).
        var windowNames = actors.Select(a => a.Name)
            .Where(n => (n.StartsWith("i_") && !n.StartsWith("i_st") && !n.StartsWith("i_dmgnum") && !n.StartsWith("i_pcdmgnum")) || (n.StartsWith("n_") && n.EndsWith("ic")))
            .Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (windowNames.Length > 0)
        {
            Step("janelas", () => IgbHudCmd(["igb-hud", ndmwPath, M, areaPath, usa.ToString(inv), Path.Combine(outDir, "janelas")], Specs(windowNames)));
            foreach (var n in windowNames) Step($"janelas/{n} seq", () => IgbSeq(ndmwPath, M, Path.Combine(outDir, "janelas", n), "--ator=" + n));
            // Fonte do jogo (ndmw #79, comum a todas as fases): uma vez, ao lado das fases.
            var fontDir = Path.Combine(outRoot, "fonte");
            if (!File.Exists(Path.Combine(fontDir, "fonte.json"))) Step("fonte", () => FonteExportar(ndmwPath, "79", fontDir));
            // Tabela das cartas (area.afs #621: RARE, KAKURITSU, NAME…; o sorteio do B. Pack usa), uma vez ao lado das fases.
            var cardDir = Path.Combine(outRoot, "cartas");
            if (!Directory.Exists(cardDir)) Step("cartas", () => IgbDb(areaPath, "621", cardDir));
        }
        // Janelas das lojas da Central Area (j_shback, j_shbase, j_shwin com as variantes SQ_AWINOPEN…SQ_JUNKOPEN; FATO: só no #825,
        // stagml12). As cartas j_shcardNN ficam de fora.
        var shopNames = actors.Select(a => a.Name).Where(n => n.StartsWith("j_sh") && !n.StartsWith("j_shcard")).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        if (shopNames.Length > 0)
        {
            Step("loja", () => IgbHudCmd(["igb-hud", ndmwPath, M, areaPath, usa.ToString(inv), Path.Combine(outDir, "loja")], Specs(shopNames)));
            foreach (var n in shopNames) Step($"loja/{n} seq", () => IgbSeq(ndmwPath, M, Path.Combine(outDir, "loja", n), "--ator=" + n));
        }
        // Tela de título (j_start* no pacote de idioma; o menu inicial do port desenha com ela).
        if (stage.Equals("stagis10", StringComparison.OrdinalIgnoreCase)) Step("titulo", () => TituloExportar(areaPath, usa.ToString(inv), Path.Combine(outDir, "titulo")));
    }
    else problems.Add("usa_pack: não encontrado no índice (textos, db, hud e menu sem exportar)");
    if (objectNames.Length > 0) Step("objetos", () => IgbObjetos(ndmwPath, M, Path.Combine(outDir, "objetos"), Specs(objectNames), TexturePack()));
    foreach (var n in objectNames) Step($"objetos/{n} seq", () => IgbSeq(ndmwPath, M, Path.Combine(outDir, "objetos", n), "--ator=" + n));
    if (mapNames.Length > 0) Step("mapa", () => IgbObjetos(ndmwPath, M, Path.Combine(outDir, "mapa"), Specs(mapNames), TexturePack()));
    foreach (var n in mapNames) Step($"mapa/{n} seq", () => IgbSeq(ndmwPath, M, Path.Combine(outDir, "mapa", n), "--ator=" + n));
    foreach (var n in menuNames) Step($"menu/{n} seq", () => IgbSeq(ndmwPath, M, Path.Combine(outDir, "menu", n), "--ator=" + n));
    if (sound >= 0) Step("sons", () => IgbSons(ndmwPath, sound.ToString(inv), Path.Combine(outDir, "sons")));
    // FATO (levantamento de áudio): o banco comum allse.sfp, que os beSoundInfo das fases citam, fica no _com ndmw #898.
    Step("sons comuns", () => IgbSons(ndmwPath, "898", Path.Combine(outDir, "sons")));

    // Músicas: beSoundData com Int6 = −1 (stream ADX); o S5 aponta para a lista de arquivos do beSoundInfo ("[0.35]").
    var music = new JsonObject();
    if (sound >= 0)
    {
        var sd = Entry(ndmwPath, sound);
        var (snd, _, _, info) = IgbSounds.Read(IgbFile.Read(sd), sd);
        var files = info.FirstOrDefault()?.Strings ?? [];
        foreach (var s in snd.Where(s => s.Values.Count > 4 && s.Values[0] is string && Convert.ToInt64(s.Values[4], inv) == -1))
        {
            int fi = Convert.ToInt32(s.Values[3], inv);
            if (fi >= 0 && fi < files.Count && System.Text.RegularExpressions.Regex.Match(files[fi], @"^\[0\.(\d+)\]$") is { Success: true } mm)
                music[(string)s.Values[0]] = int.Parse(mm.Groups[1].Value, inv);
        }
        var adx = music.Select(kv => kv.Value!.GetValue<int>().ToString(inv)).Distinct().ToArray();
        if (adx.Length > 0) Step("audio", () => AdxWav(ndmwPath, Path.Combine(outDir, "audio"), adx));
    }

    // Inimigos: linhas da tabela enemy pedidas pelos geradores (MODEL); o modelo fica no IGB principal (e_nume na dng1).
    var enemies = new JsonArray();
    var exported = new HashSet<string>();
    if (genN >= 0 && usa >= 0)
    {
        var gd = Entry(ndmwPath, genN);
        var gens = IgbGenerators.ReadGenerators(IgbFile.Read(gd), gd);
        var ud = Entry(areaPath, usa);
        var tables = IgbDatabase.Read(IgbFile.Read(ud), ud, []);
        var enemy = tables.FirstOrDefault(t => t.Name == "enemy");
        foreach (var row in gens.Select(g => g.EnemyRow).Distinct().OrderBy(r => r))
        {
            string model = enemy?.Column("MODEL") is { } col && row >= 0 && row < col.Cells.Count ? col.Cells[row]?.ToString() ?? "" : "";
            bool inMain = actors.Any(a => a.Name == model);
            enemies.Add(new JsonObject
            {
                ["linha"] = row, ["modelo"] = model, ["no_principal"] = inMain,
                ["geradores"] = new JsonArray(gens.Where(g => g.EnemyRow == row).Select(g => (JsonNode?)g.Name).ToArray()),
            });
            // e_ = inimigo comum, f_ = chefe (FATO: f_apocaly na linha 106 da enemy, stagbs01).
            if (inMain && (model.StartsWith("e_") || model.StartsWith("f_")) && exported.Add(model))
            {
                var dir = Path.Combine(outDir, "inimigos", model);
                if (!objectNames.Contains(model)) Step($"inimigos/{model}", () => IgbGltf(ndmwPath, M, dir, ["--ator=" + model]));
                Step($"inimigos/{model} seq", () => IgbSeq(ndmwPath, M, dir, "--ator=" + model));
            }
            // FATO (varredura 2026-10-25): e_arachne (stag0202) e e_torta (stag0302) ficam num IGB próprio da fase ("<modelo>.igb"
            // na lista do índice), não no principal.
            else if (!inMain && (model.StartsWith("e_") || model.StartsWith("f_")) && sep.FirstOrDefault(x => x.Afs == 0 && x.Name == model + ".igb") is { } own
                     && own.Name != null && exported.Add(model))
            {
                var dir = Path.Combine(outDir, "inimigos", model);
                string at = own.Index.ToString(CultureInfo.InvariantCulture);
                Step($"inimigos/{model} (#{at})", () => IgbGltf(ndmwPath, at, dir, ["--ator=" + model]));
                Step($"inimigos/{model} seq (#{at})", () => IgbSeq(ndmwPath, at, dir, "--ator=" + model));
            }
        }
    }

    // NPCs (n_*, sem os ícones de conversa *ic): o nó do ator é um igActor com esqueleto (FATO: n_agumon, n_leomon,
    // n_digilff, n_digilfm no stagml11), então sai como personagem; as sequências já vão em objetos\<nome>.
    foreach (var npc in objectNames.Where(n => n.StartsWith("n_") && !n.EndsWith("ic")))
    {
        var probe = IgbCharacter.Read(mainFile, mainData, npc);
        if (probe.Bones.Count == 0) continue; // objeto parado (ex.: n_recorderdng), já exportado como objeto
        Step($"npcs/{npc}", () => IgbGltf(ndmwPath, M, Path.Combine(outDir, "npcs", npc), ["--ator=" + npc]));
    }

    var generated = new JsonArray(Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories)
        .Select(p => (JsonNode?)Path.GetRelativePath(outDir, p).Replace('\\', '/')).Where(p => p!.GetValue<string>() != "fase.json")
        .OrderBy(p => p!.GetValue<string>(), StringComparer.Ordinal).ToArray());
    JsonNode? E(int i, int afs = 0) => i < 0 ? null : new JsonObject { ["afs"] = afs == 1 ? "area.afs" : "ndmw.afs", ["entrada"] = i };
    var manifest = new JsonObject
    {
        ["nome"] = stage,
        ["entradas"] = new JsonObject
        {
            ["esboco"] = E(stub.Index, stub.Afs), ["principal"] = E(main), ["sons"] = E(sound), ["camera"] = E(cam), ["camera_afastada"] = E(camFar),
            ["acmr_pack"] = E(acmr), ["gerador_h"] = E(genH), ["gerador_n"] = E(genN), ["gerador_v"] = E(genV), ["usa_pack"] = E(usa, 1),
            ["outros"] = new JsonArray(sep.Where(x => x.Afs == 0 && x.Index != cam && x.Index != camFar && x.Index != acmr && x.Index != genH && x.Index != genN && x.Index != genV)
                .Select(x => (JsonNode?)new JsonObject { ["nome"] = x.Name, ["entrada"] = x.Index }).ToArray()),
        },
        ["raizes"] = new JsonObject { ["colisao_BG01"] = bg01, ["cenario_BG02"] = bg02,
            ["todas"] = new JsonArray(roots.Select(r => (JsonNode?)new JsonObject { ["indice"] = r.Index, ["nome"] = r.Name }).ToArray()) },
        ["arquivos_camera"] = new JsonObject
        {
            ["camera"] = cam >= 0 ? $"ndmw_{cam:D4}_camera.json" : mainBox ? $"ndmw_{main:D4}_camera.json" : null,
            ["camera_afastada"] = camFar >= 0 ? $"ndmw_{camFar:D4}_camera.json" : null,
            ["demo"] = new JsonArray(new[] { acmr >= 0 ? $"camera-demo/ndmw_{acmr:D4}_<nome>.json" : null, mainDemo ? $"camera-demo/ndmw_{main:D4}_<nome>.json" : null }
                .Where(s => s != null).Select(s => (JsonNode?)s).ToArray()),
        },
        ["mapa"] = new JsonObject
        {
            ["base"] = actors.FirstOrDefault(a => a.Kind == "KIND_MAPBASE").Name, ["cursor"] = actors.FirstOrDefault(a => a.Kind == "KIND_MAPCURSOR").Name,
        },
        ["objetos"] = new JsonArray(objectNames.Select(n => (JsonNode?)n).ToArray()),
        ["atores"] = new JsonObject(actors.Where(a => a.Name.Length > 0).GroupBy(a => a.Name).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.First().Kind))),
        ["menu"] = new JsonArray(menuNames.Select(n => (JsonNode?)n).ToArray()),
        ["musicas"] = music,
        ["neblina"] = Fog(mainFile),
        ["inimigos"] = enemies,
        ["passos"] = steps,
        ["problemas"] = problems,
        ["arquivos"] = generated,
    };
    File.WriteAllText(Path.Combine(outDir, "fase.json"), manifest.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{stage}: principal #{main}, sons #{sound}, câmeras #{cam}/#{camFar}, acmr #{acmr}, geradores #{genH}/#{genN}/#{genV}, usa_pack area #{usa}; " +
        $"{steps.Count(s => s!["ok"]!.GetValue<bool>())}/{steps.Count} passos ok, {problems.Count} problemas -> {outDir}");
    return problems.Count == 0 ? 0 : 3;
}

// Pontos de entrada do jogador (bePoint01Info → beGroupKeep → igTransform): pontos_entrada.json, uma vaga por jogador.
int IgbEntradas(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var points = IgbEntryPoints.Read(IgbFile.Read(data), data);
    if (points.Count == 0) { Console.Error.WriteLine("nenhum bePoint01Info"); return 1; }
    JsonArray V((float X, float Y, float Z) v) => [v.X, v.Y, v.Z];
    var root = new JsonObject
    {
        ["source"] = $"{Path.GetFileName(path)} #{idx}",
        ["points"] = new JsonArray(points.Select(p => (JsonNode?)new JsonObject
        {
            ["index"] = p.Index, ["min"] = V(p.Min), ["max"] = V(p.Max),
            ["slots"] = new JsonArray(p.Slots.Select(s => (JsonNode?)new JsonObject
            {
                ["player"] = s.Player, ["position"] = V(s.Position), ["rotYDeg"] = s.RotYDeg,
                ["matrix"] = new JsonArray(s.Matrix.Select(x => (JsonNode?)x).ToArray()),
            }).ToArray()),
        }).ToArray()),
    };
    Directory.CreateDirectory(full);
    var file = Path.Combine(full, "pontos_entrada.json");
    File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    foreach (var p in points)
        Console.WriteLine(string.Create(inv, $"{p.Index}: {string.Join("  ", p.Slots.Select(s => $"({s.Position.X}, {s.Position.Y}, {s.Position.Z}) {s.RotYDeg}°"))}"));
    Console.WriteLine($"{points.Count} pontos -> {file}");
    return 0;
}

// Textos de um *_pack (beFontInfo → beUnicodeObjList): textos.json com índice (o k de "I<k>" nos scripts), chave e texto.
int IgbTextos(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var texts = IgbTexts.Read(IgbFile.Read(data), data);
    if (texts.Count == 0) { Console.Error.WriteLine("nenhum beUnicodeObj"); return 1; }
    Directory.CreateDirectory(full);
    var root = new JsonObject
    {
        ["source"] = $"{Path.GetFileName(path)} #{idx}",
        ["texts"] = new JsonArray(texts.Select(t => (JsonNode?)new JsonObject { ["index"] = t.Index, ["key"] = t.Key, ["text"] = t.Text }).ToArray()),
    };
    var file = Path.Combine(full, "textos.json");
    File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    }));
    Console.WriteLine($"{texts.Count} textos -> {file}");
    return 0;
}

// Câmeras de demo (beCameraCtrlInfo → beCameraDemoData) de um acmr_pack: um JSON por câmera com o caminho cru (matrizes e
// canais das sequências) e os quadros amostrados a 60 Hz (posição, frente, cima e matriz do mundo, em linhas).
int IgbCameraDemoCmd(string path, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var demos = IgbCameraDemo.Read(IgbFile.Read(data), data);
    if (demos.Count == 0) { Console.Error.WriteLine("nenhuma câmera de demo"); return 1; }
    JsonArray Arr(IEnumerable<float> v) => new(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    JsonArray Times(IEnumerable<double> v) => new(v.Select(x => (JsonNode?)JsonValue.Create(Math.Round(x, 6))).ToArray());
    Directory.CreateDirectory(full);
    foreach (var d in demos)
    {
        foreach (var n in d.Notes) Console.WriteLine($"nota ({d.Name}): {n}");
        var nodes = new JsonArray();
        foreach (var n in d.Path)
        {
            var j = new JsonObject { ["index"] = n.Index, ["name"] = n.Name, ["matrix"] = Arr(n.Matrix) };
            if (n.Sequence is { } s)
                j["sequence"] = new JsonObject
                {
                    ["mask"] = s.Mask, ["interpolation"] = new JsonArray(s.Interpolation.Select(b => (JsonNode?)(int)b).ToArray()),
                    ["duration"] = s.Duration, ["times"] = Times(s.Times), ["translation"] = Arr(s.Translation),
                    ["rotation"] = Arr(s.Rotation), ["scale"] = Arr(s.Scale), ["raw12"] = Arr(s.Raw12), ["raw13"] = Arr(s.Raw13),
                };
            nodes.Add(j);
        }
        var frames = d.Sample();
        var root = new JsonObject
        {
            ["source"] = $"{Path.GetFileName(path)} #{idx}", ["name"] = d.Name, ["duration"] = d.Duration, ["rate"] = 60,
            ["fovHorizontalDeg"] = d.Fov, ["near"] = d.Near, ["far"] = d.Far,
            ["frames"] = new JsonArray(frames.Select(fr => (JsonNode?)new JsonObject
            {
                ["t"] = Math.Round(fr.Time, 6), ["position"] = Arr(fr.Position), ["forward"] = Arr(fr.Forward), ["up"] = Arr(fr.Up),
                ["world"] = Arr(fr.World),
            }).ToArray()),
            ["path"] = nodes,
        };
        var file = Path.Combine(full, $"{Path.GetFileNameWithoutExtension(path)}_{idx:D4}_{d.Name}.json");
        File.WriteAllText(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        IgbDemoFrame a = frames[0], b = frames[^1];
        Console.WriteLine(string.Create(inv, $"{d.Name}: {d.Duration:0.###} s, {frames.Count} quadros, fov {d.Fov:0.##}° (horizontal), {d.Path.Count} nós"));
        Console.WriteLine(string.Create(inv, $"  início ({a.Position[0]:0.#}, {a.Position[1]:0.#}, {a.Position[2]:0.#}) frente ({a.Forward[0]:0.##}, {a.Forward[1]:0.##}, {a.Forward[2]:0.##})"));
        Console.WriteLine(string.Create(inv, $"  fim    ({b.Position[0]:0.#}, {b.Position[1]:0.#}, {b.Position[2]:0.#}) frente ({b.Forward[0]:0.##}, {b.Forward[1]:0.##}, {b.Forward[2]:0.##})"));
        Console.WriteLine($"  -> {file}");
    }
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
// Tela de título da stagIS10 (FATO: o first.lua gera j_startback e o TITLE2 nativo mostra o resto; os atores j_startback,
// j_startlogo, j_startmenu01 "Press START" e j_startmenu02 "Game Start / Option" ficam no pacote de idioma, area.afs #620 na
// versão americana, com a textura num igAttrSet acima do igSwitch do beAction2Info). Exporta cada um pela raiz desse igAttrSet.
int TituloExportar(string areaPath, string which, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(areaPath);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    bool le = f.Header.LittleEndian;
    IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
    string Name(IgbObject o) => o.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "";
    // Filhos diretos de um nó (lista igNodeList do próprio nó).
    IEnumerable<int> Kids(IgbObject n)
    {
        foreach (var v in n.Fields.Where(v => v.Desc.ShortName == "ObjectRef"))
        {
            if (Slot(v.AsInt32(le)) is not IgbObject list || !f.Is(list, "igNodeList")) continue;
            var cnt = list.Fields.FirstOrDefault(x => x.Desc.ShortName == "Int");
            var mr = list.Fields.FirstOrDefault(x => x.Desc.ShortName == "MemoryRef");
            if (cnt == null || mr == null || Slot(mr.AsInt32(le)) is not IgbMemoryBlock mb) continue;
            var mem = MemoryOf(data, mb);
            for (int i = 0; i < Math.Min(cnt.AsInt32(le), mem.Length / 4); i++)
                yield return le ? BinaryPrimitives.ReadInt32LittleEndian(mem.AsSpan(4 * i)) : BinaryPrimitives.ReadInt32BigEndian(mem.AsSpan(4 * i));
        }
    }
    var parent = new Dictionary<int, int>();
    foreach (var (o, i) in f.Slots.Select((s, i) => (s, i)).Where(x => x.s is IgbObject).Select(x => ((IgbObject)x.s, x.i)))
        if (f.Is(o, "igAttrSet")) foreach (int k in Kids(o)) parent.TryAdd(k, i);
    var specs = new List<string>();
    foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beAction2Info"))
    {
        string name = Name(o);
        int sw = o.Fields.FirstOrDefault(v => v.Desc.Slot == 7 && v.Desc.ShortName == "ObjectRef")?.AsInt32(le) ?? -1;
        if (!name.StartsWith("j_start", StringComparison.Ordinal) || sw < 0) continue;
        specs.Add($"{name}={(parent.TryGetValue(sw, out int p) ? p : sw)}");
    }
    if (specs.Count == 0) { Console.Error.WriteLine("nenhum ator j_start* no pacote"); return 1; }
    Console.WriteLine($"atores: {string.Join(", ", specs)}");
    int rc = IgbHudCmd(["igb-hud", areaPath, which, areaPath, which, full], specs.ToArray());
    foreach (var s in specs) rc |= IgbSeq(areaPath, which, Path.Combine(full, s[..s.IndexOf('=')]), "--ator=" + s[..s.IndexOf('=')]);
    return rc;
}

// Decodifica o vídeo inteiro (confere o decodificador) e grava em PNG os quadros pedidos (padrão: 0, 300, 900 e o último).
int SfdQuadros(string path, string which, string outDir, string[] wanted)
{
    var inv = CultureInfo.InvariantCulture;
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    if (!int.TryParse(which, NumberStyles.None, inv, out int idx) || idx >= afs.Entries.Count) { Console.Error.WriteLine("índice inválido"); return 1; }
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[idx], ms);
    var (streams, _, _) = MpegPs.Demux(ms.ToArray());
    if (!streams.TryGetValue(0xE0, out var video)) { Console.Error.WriteLine("sem fluxo de vídeo 0xE0"); return 1; }
    var want = wanted.Select(w => int.Parse(w, inv)).ToHashSet();
    var dec = new Mpeg1Video(video.Payload.ToArray());
    Directory.CreateDirectory(root);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var counts = new Dictionary<char, int>();
    byte[]? rgba = null, last = null;
    int frames = 0;
    while (dec.Next() is { } f)
    {
        frames++;
        counts[f.Type] = counts.GetValueOrDefault(f.Type) + 1;
        bool save = want.Count == 0 ? f.Index is 0 or 300 or 900 : want.Contains(f.Index);
        if (!save && want.Count > 0) continue;
        rgba ??= new byte[f.Width * f.Height * 4];
        f.ToRgba(rgba);
        if (want.Count == 0) last = (byte[])rgba.Clone();
        if (!save) continue;
        using var png = File.Create(Path.Combine(root, $"quadro_{f.Index:D4}.png"));
        Png.WriteRgba(png, f.Width, f.Height, rgba);
    }
    if (last != null)
    {
        using var png = File.Create(Path.Combine(root, $"quadro_{frames - 1:D4}.png"));
        Png.WriteRgba(png, dec.Width, dec.Height, last);
    }
    Console.WriteLine($"{dec.Width}x{dec.Height} a {dec.FrameRate:0.###} fps: {frames} quadros ({string.Join(", ", counts.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}) em {sw.Elapsed.TotalSeconds:0.0} s; {dec.Problems.Count} problemas");
    foreach (var p in dec.Problems.Take(20)) Console.WriteLine($"  problema: {p}");
    return dec.Problems.Count == 0 ? 0 : 3;
}

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

// Grava os chunks Lua de uma fase (um arquivo por nome de beLuaData) para o cliente executar; nunca dentro de um repositório.
int LuaExtrair(string afsPath, int entry, string outDir)
{
    var full = Path.GetFullPath(outDir);
    if (InsideGitRepo(full)) { Console.Error.WriteLine($"recusado: {full} está dentro de um repositório Git"); return 1; }
    Directory.CreateDirectory(full);
    using var afs = AfsArchive.Open(afsPath);
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[entry], ms);
    var data = ms.ToArray();
    var chunks = DW4.Formats.Igb.IgbLua.Chunks(IgbFile.Read(data), data);
    foreach (var (name, chunk) in chunks)
        File.WriteAllBytes(Path.Combine(full, string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '_' or '.' ? c : '_'))), chunk);
    Console.WriteLine($"{chunks.Count} scripts gravados em {full}");
    return 0;
}

// Executa os scripts de uma fase na VM Lua própria (ADR 0002), na ordem fixa do motor (func.lua, boot.lua, start.lua,
// first.lua; máquina de estados em 0x802F8A64) ou nos nomes passados, e imprime cada chamada be:* com os argumentos.
// A API ainda é simulada: cada método devolve nil, salvo os valores passados como Metodo=valor (número, texto ou nil)
// e SYSisJoinPlayer, que só confirma o jogador 0. Só imprime; não grava nada.
int LuaTrace(string afsPath, int entry, string[] rest)
{
    var scripts = rest.Where(a => a.EndsWith(".lua", StringComparison.Ordinal)).ToList();
    if (scripts.Count == 0) scripts = ["func.lua", "boot.lua", "start.lua", "first.lua"];
    var returns = new Dictionary<string, object?>(StringComparer.Ordinal);
    foreach (var a in rest.Where(a => a.Contains('=')))
    {
        var (k, v) = (a[..a.IndexOf('=')], a[(a.IndexOf('=') + 1)..]);
        returns[k] = v == "nil" ? null : double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : v;
    }

    byte[] data;
    using (var afs = AfsArchive.Open(afsPath))
    using (var ms = new MemoryStream())
    {
        afs.CopyEntry(afs.Entries[entry], ms);
        data = ms.ToArray();
    }
    var chunks = DW4.Formats.Igb.IgbLua.Chunks(IgbFile.Read(data), data);
    Console.WriteLine($"-- entrada #{entry}: {chunks.Count} scripts ({string.Join(", ", chunks.Keys.Order(StringComparer.Ordinal))})");

    var vm = new LuaVM { InstructionLimit = 5_000_000, Print = s => Console.WriteLine($"   print: {s}") };
    string Show(object? v) => v switch
    {
        null => "nil",
        string str => $"\"{str}\"",
        double d => DW4.Script.Lua.NumberToString(d),
        LuaTable => "{tabela}",
        LuaFunction fn => $"<função {fn.Name}>",
        _ => v.ToString() ?? "?",
    };
    var be = new LuaTable();
    var dmQueue = new List<string>();
    be.Missing = (_, key) => key is not string name ? null : new LuaHostFunction(name, a =>
    {
        var shown = string.Join(", ", a.Skip(1).Select(Show));
        object? ret = returns.TryGetValue(name, out var rv) ? rv
            : name == "SYSisJoinPlayer" ? (a.Length > 1 && a[1] is double p && p == 0 ? DW4.Script.Lua.True : null)
            : null;
        Console.WriteLine($"   be:{name}({shown}){(ret != null ? $" -> {Show(ret)}" : "")}");
        if (name == "DMsetLua" && a.Length > 1 && a[1] is string next) dmQueue.Add(next);
        return [ret];
    });
    vm.Globals["be"] = be;
    var undefined = new SortedSet<string>(StringComparer.Ordinal);
    vm.Globals.Missing = (_, key) => { if (key is string g) undefined.Add(g); return null; };

    int failures = 0;
    foreach (var name in scripts)
    {
        if (!chunks.TryGetValue(name, out var chunk)) { Console.WriteLine($"-- {name}: não existe nesta entrada"); continue; }
        Console.WriteLine($"-- {name}");
        try { vm.DoChunk(chunk); }
        catch (LuaException ex) { failures++; Console.WriteLine($"   ERRO: {ex.Message}"); }
    }
    if (dmQueue.Count > 0) Console.WriteLine($"-- be:DMsetLua agendou: {string.Join(", ", dmQueue)}");
    Console.WriteLine($"-- globais lidas sem definição: {string.Join(", ", undefined)}");
    Console.WriteLine($"-- {vm.Executed} instruções executadas, {failures} scripts com erro");
    return failures == 0 ? 0 : 3;
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

// Neblina da fase (igFogAttr: Enum@4 modo, Float@5 início, Float@6 fim, Float@7 densidade, Vec4f@8 cor; igFogStateAttr Bool@4).
// FATO (stag0102 #621): modo 0, 60..300, densidade 0,005, cor (0,8 0,7 0,8); um igFogStateAttr ligado e um desligado.
static JsonNode? Fog(IgbFile f)
{
    bool le = f.Header.LittleEndian;
    var fog = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "igFogAttr"));
    if (fog == null) return null;
    IgbFieldValue? F(int slot) => fog.Fields.FirstOrDefault(v => v.Desc.Slot == slot);
    float Fl(int slot) => F(slot) is { Raw.Length: >= 4 } v ? (le ? BitConverter.ToSingle(v.Raw, 0) : System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(v.Raw)) : 0;
    var c = F(8)?.Raw ?? [];
    float C(int i) => c.Length >= 4 * (i + 1) ? (le ? BitConverter.ToSingle(c, 4 * i) : System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(c.AsSpan(4 * i))) : 1;
    bool on = f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igFogStateAttr"))
        .Any(o => o.Fields.FirstOrDefault(v => v.Desc.Slot == 4 && v.Desc.ShortName == "Bool") is { Raw.Length: > 0 } b && b.Raw[0] != 0);
    return new JsonObject
    {
        ["modo"] = F(4)?.AsInt32(le) ?? 0, ["inicio"] = Fl(5), ["fim"] = Fl(6), ["densidade"] = Fl(7),
        ["cor"] = new JsonArray(C(0), C(1), C(2), C(3)), ["ligada"] = on,
    };
}

// Fontes do jogo (FATO no ndmw #79 "BecT": dois beFontImage, "MINIMUM" e "NORMAL"; @3 = altura da linha, @7 → igCharMetricsList
// com 484 caracteres de 40 bytes: código u16 + 2 de enchimento, avanço, deslocamento x e y, largura, altura, u0, v0, u1, v1 em f32; @8 → textura).
int FonteExportar(string path, string which, string outDir)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(path);
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[int.Parse(which, inv)], ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    bool le = f.Header.LittleEndian;
    var scene = IgbScene.Read(f, data);
    Directory.CreateDirectory(root);
    var pngOf = new Dictionary<int, string>();
    foreach (var img in scene.Images.Values)
    {
        var rgba = IgbScene.IndexedToRgba(img);
        if (rgba == null) continue;
        var file = $"fonte_img{img.Index:D4}.png";
        using (var fs = File.Create(Path.Combine(root, file))) Png.WriteRgba(fs, img.Width, img.Height, rgba);
        pngOf[img.Index] = file;
    }
    // Primeira igImage alcançável a partir de um objeto (a textura do igTextureBindAttr).
    int ImageUnder(int start)
    {
        var seen = new HashSet<int>();
        var todo = new Stack<int>([start]);
        while (todo.Count > 0)
        {
            int i = todo.Pop();
            if (i < 0 || i >= f.Slots.Count || !seen.Add(i) || f.Slots[i] is not IgbObject o) continue;
            if (o.TypeName == "igImage") return i;
            foreach (var v in o.Fields.Where(v => v.Desc.ShortName == "ObjectRef")) todo.Push(v.AsInt32(le));
        }
        return -1;
    }
    float F(byte[] m, int o) => le ? BitConverter.ToSingle(m, o) : BitConverter.ToSingle([m[o + 3], m[o + 2], m[o + 1], m[o]], 0);
    uint U(byte[] m, int o) => le ? BitConverter.ToUInt32(m, o) : (uint)(m[o] << 24 | m[o + 1] << 16 | m[o + 2] << 8 | m[o + 3]);
    var fonts = new JsonArray();
    foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beFontImage"))
    {
        string name = o.Fields.FirstOrDefault(v => v.Desc.Slot == 2)?.Text ?? "";
        float line = o.Fields.FirstOrDefault(v => v.Desc.Slot == 3 && v.Raw.Length >= 4) is { } lf ? F(lf.Raw, 0) : 0;
        int metrics = o.Fields.FirstOrDefault(v => v.Desc.Slot == 7)?.AsInt32(le) ?? -1;
        int bind = o.Fields.FirstOrDefault(v => v.Desc.Slot == 8)?.AsInt32(le) ?? -1;
        int img = ImageUnder(bind);
        if (img < 0 || !pngOf.ContainsKey(img)) img = pngOf.Keys.DefaultIfEmpty(-1).First();
        var glyphs = new JsonArray();
        if (metrics >= 0 && f.Slots[metrics] is IgbObject ml
            && ml.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef")?.AsInt32(le) is int mr && mr >= 0 && f.Slots[mr] is IgbMemoryBlock mb)
        {
            var mem = MemoryOf(data, mb);
            for (int at = 0; at + 40 <= mem.Length; at += 40)
                glyphs.Add(new JsonArray((JsonNode?)(U(mem, at) & 0xFFFF), // código em 16 bits; os outros 2 bytes são enchimento (CC CC no MINIMUM)
                     F(mem, at + 4), F(mem, at + 8), F(mem, at + 12), F(mem, at + 16), F(mem, at + 20),
                    F(mem, at + 24), F(mem, at + 28), F(mem, at + 32), F(mem, at + 36)));
        }
        fonts.Add(new JsonObject { ["nome"] = name, ["linha"] = line, ["textura"] = img >= 0 ? pngOf[img] : null, ["caracteres"] = glyphs });
        Console.WriteLine($"fonte {name}: linha {line}, {glyphs.Count} caracteres, textura {(img >= 0 ? pngOf[img] : "-")}");
    }
    File.WriteAllText(Path.Combine(root, "fonte.json"), new JsonObject { ["origem"] = $"{path} #{which}", ["campos"] = "código, avanço, x, y, largura, altura, u0, v0, u1, v1", ["fontes"] = fonts }.ToJsonString());
    return 0;
}

// Caixas de texto do pacote de idioma (FATO no area.afs #9: cena "FONT" → categoria TALKFONT02… → família NORMAL/MINIMUM → fontNN, um
// igAttrSet com a cor e um retângulo de geometria na tela 640 × 480; os beFontInfoData ligam categoria, fontNN (@4), família (@10) e
// os parâmetros @3, @5, @6, @7, @8). Grava caixas.json: categoria, fonte, família, retângulo, cor e parâmetros.
int FonteCaixas(string areaPath, string which, string outDir)
{
    var root = Path.GetFullPath(outDir);
    if (InsideGitRepo(root)) { Console.Error.WriteLine($"recusado: {root} está dentro de um repositório Git"); return 1; }
    using var afs = AfsArchive.Open(areaPath);
    using var ms = new MemoryStream();
    afs.CopyEntry(afs.Entries[int.Parse(which, inv)], ms);
    var data = ms.ToArray();
    var f = IgbFile.Read(data);
    bool le = f.Header.LittleEndian;
    var scene = IgbScene.Read(f, data);
    float F(byte[] m, int o) => le ? BitConverter.ToSingle(m, o) : BitConverter.ToSingle([m[o + 3], m[o + 2], m[o + 1], m[o]], 0);
    var boxes = new JsonArray();
    foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beFontInfoData"))
    {
        string Txt(int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Text ?? "";
        int Int(int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.AsInt32(le) ?? 0;
        float Flt(int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Raw.Length >= 4) is { } v ? F(v.Raw, 0) : 0;
        int set = Int(4);
        if (set < 0 || set >= f.Slots.Count || f.Slots[set] is not IgbObject attrSet) continue;
        string slotName = attrSet.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text ?? "";
        var reach = scene.Reachable(attrSet);
        var pts = scene.Parts.Where(p => reach.Contains(p.GeometryIndex)).SelectMany(p => p.Positions).ToList();
        float[]? color = null;
        foreach (var a in scene.AttrList(attrSet).Where(a => a.TypeName == "igColorAttr"))
            if (a.Fields.FirstOrDefault(v => v.Desc.Slot == 4 && v.Raw.Length >= 16) is { } c) color = [F(c.Raw, 0), F(c.Raw, 4), F(c.Raw, 8), F(c.Raw, 12)];
        var box = new JsonObject
        {
            ["categoria"] = Txt(2), ["fonte"] = slotName, ["familia"] = Txt(10),
            ["p3"] = Int(3), ["p5"] = Int(5), ["p6"] = Int(6), ["p7"] = Flt(7), ["p8"] = Flt(8),
        };
        if (pts.Count > 0) box["retangulo"] = new JsonArray(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));
        // Posição: translação do igTransform do fontNN (a geometria está no espaço dele).
        var tr = reach.Select(i => f.Slots[i]).OfType<IgbObject>().FirstOrDefault(x => x.TypeName == "igTransform");
        if (tr?.Fields.FirstOrDefault(v => v.Desc.ShortName == "Matrix44f" && v.Raw.Length >= 64) is { } mtx)
            box["posicao"] = new JsonArray(F(mtx.Raw, 48), F(mtx.Raw, 52), F(mtx.Raw, 56));
        if (color != null) box["cor"] = new JsonArray(color.Select(v => (JsonNode?)v).ToArray());
        boxes.Add(box);
    }
    Directory.CreateDirectory(root);
    File.WriteAllText(Path.Combine(root, "caixas.json"), new JsonObject { ["origem"] = $"{areaPath} #{which}", ["caixas"] = boxes }.ToJsonString());
    Console.WriteLine($"{boxes.Count} caixas de texto");
    return 0;
}
