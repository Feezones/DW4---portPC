using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using DW4.Formats.Afs;
using DW4.Formats.Analysis;
using DW4.Formats.Igb;
using DW4.Formats.Images;
using DW4.Formats.Lua;
using DW4.Formats.Sofdec;
using DW4.Gameplay;

// Verificador dos leitores de formato, sem pacotes externos.
// Sem argumentos: só testes sintéticos (dados montados aqui, nada do jogo).
// Com a pasta files\ da cópia de trabalho: também confere os resultados já medidos nos dados do jogo.
// O jogo é aberto só para leitura e nada é gravado.

int passed = 0, failed = 0;

void Test(string name, Action body)
{
    try
    {
        body();
        passed++;
        Console.WriteLine($"ok     {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FALHOU {name}: {ex.Message}");
    }
}

static void Equal<T>(T expected, T actual, string what)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"{what}: esperado {expected}, obtido {actual}");
}

static void True(bool condition, string what)
{
    if (!condition) throw new InvalidOperationException(what);
}

// ---------- sintéticos ----------

Test("AFS: tabela, nomes, datas e cópia de entrada", () =>
{
    var file = new byte[0x2000];
    "AFS\0"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 2);
    void Pair(int at, uint offset, uint size)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at), offset);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), size);
    }
    Pair(8, 0x800, 3);
    Pair(16, 0x1000, 5);
    Pair(24, 0x1800, 2 * AfsArchive.NameRecordSize);
    "abc"u8.CopyTo(file.AsSpan(0x800));
    "defgh"u8.CopyTo(file.AsSpan(0x1000));
    for (int i = 0; i < 2; i++)
    {
        var rec = file.AsSpan(0x1800 + i * AfsArchive.NameRecordSize);
        Encoding.ASCII.GetBytes(i == 0 ? "soun" : "stag").CopyTo(rec);
        ushort[] date = [2005, 3, 8, 12, 30, 15];
        for (int k = 0; k < 6; k++) BinaryPrimitives.WriteUInt16LittleEndian(rec[(32 + 2 * k)..], date[k]);
    }
    var path = Path.Combine(Path.GetTempPath(), $"dw4-teste-{Guid.NewGuid():N}.afs");
    File.WriteAllBytes(path, file);
    try
    {
        using var afs = AfsArchive.Open(path);
        Equal(2, afs.Entries.Count, "entradas");
        Equal("stag", afs.Entries[1].Name, "nome da entrada 1");
        Equal(new DateTime(2005, 3, 8, 12, 30, 15), afs.Entries[0].Timestamp, "data da entrada 0");
        Equal(0, afs.Validate().Count, "problemas de validação");
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[1], ms);
        Equal("defgh", Encoding.ASCII.GetString(ms.ToArray()), "conteúdo da entrada 1");
    }
    finally { File.Delete(path); }
});

Test("AFS: entrada desalinhada é apontada pela validação", () =>
{
    var file = new byte[0x1000];
    "AFS\0"u8.CopyTo(file);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), 0x810);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 4);
    var path = Path.Combine(Path.GetTempPath(), $"dw4-teste-{Guid.NewGuid():N}.afs");
    File.WriteAllBytes(path, file);
    try
    {
        using var afs = AfsArchive.Open(path);
        var problems = afs.Validate();
        True(problems.Any(p => p.Contains("não alinhado")), "desalinhamento não detectado");
        True(problems.Any(p => p.Contains("tabela de nomes")), "falta da tabela de nomes não detectada");
    }
    finally { File.Delete(path); }
});

Test("Classificador: MPEG-PS, IGB, ADX e desconhecido", () =>
{
    Equal(ContentKind.MpegPs, Classifier.Classify([0, 0, 1, 0xBA, 0x44]), "MPEG-PS");
    var igb = new byte[0x30];
    BinaryPrimitives.WriteUInt32LittleEndian(igb.AsSpan(0x28), Classifier.IgbMagic);
    Equal(ContentKind.Igb, Classifier.Classify(igb), "IGB");
    var adx = new byte[0x24];
    adx[0] = 0x80;
    BinaryPrimitives.WriteUInt16BigEndian(adx.AsSpan(2), 0x1C); // dados em 0x20; "(c)CRI" em 0x1A
    adx[4] = 3;
    "(c)CRI"u8.CopyTo(adx.AsSpan(0x1A));
    Equal(ContentKind.Adx, Classifier.Classify(adx), "ADX");
    Equal(ContentKind.Unknown, Classifier.Classify(new byte[64]), "zeros");
});

Test("Lua 4.0: chunk mínimo lido e cabeçalho errado recusado", () =>
{
    var chunk = new List<byte> { 0x1B, (byte)'L', (byte)'u', (byte)'a', 0x40, 1, 4, 4, 4, 32, 6, 9, 8 };
    chunk.AddRange(BitConverter.GetBytes(3.14159265358979323846E8));
    void Int(int v) => chunk.AddRange(BitConverter.GetBytes(v));
    Int(5); chunk.AddRange("@t.l\0"u8.ToArray()); // nome do fonte
    Int(0); Int(0); chunk.Add(0); Int(2);          // linha, parâmetros, vararg, pilha
    Int(0); Int(0);                                // locais, linhas
    Int(1); Int(3); chunk.AddRange("be\0"u8.ToArray()); // uma string
    Int(0); Int(0);                                // números, funções
    Int(2); Int(12 | (0 << 6)); Int(0);            // GETGLOBAL 0, END
    var f = Lua40Chunk.Read(chunk.ToArray());
    Equal("@t.l", f.Source, "fonte");
    Equal("be", f.Strings[0], "string 0");
    Equal(2, f.Code.Count, "instruções");
    Equal("GETGLOBAL", Lua40Chunk.OpNames[Lua40Chunk.Op(f.Code[0])], "opcode");
    var bad = chunk.ToArray();
    bad[10] = 7; // SIZE_OP diferente
    True(Throws(() => Lua40Chunk.Read(bad)), "SIZE_OP errado foi aceito");
});

Test("PNG: assinatura, CRC dos blocos e pixels de volta", () =>
{
    byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0, 9, 8, 7, 6];
    using var ms = new MemoryStream();
    Png.WriteRgba(ms, 2, 2, rgba);
    var png = ms.ToArray();
    True(png.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "assinatura");
    var idat = new MemoryStream();
    for (int at = 8; at < png.Length;)
    {
        int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
        var type = Encoding.ASCII.GetString(png, at + 4, 4);
        uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + len));
        Equal(Crc32(png.AsSpan(at + 4, 4 + len)), crc, $"CRC de {type}");
        if (type == "IDAT") idat.Write(png, at + 8, len);
        at += 12 + len;
    }
    idat.Position = 0;
    using var z = new ZLibStream(idat, CompressionMode.Decompress);
    using var raw = new MemoryStream();
    z.CopyTo(raw);
    var rows = raw.ToArray();
    Equal(2 * (1 + 8), rows.Length, "bytes descomprimidos");
    True(rows.AsSpan(1, 8).SequenceEqual(rgba.AsSpan(0, 8)) && rows.AsSpan(10, 8).SequenceEqual(rgba.AsSpan(8, 8)), "pixels");
});

Test("Imagem indexada: paleta RGBA e linhas de baixo para cima", () =>
{
    var palette = new byte[1024];
    palette[4 * 1] = 10; palette[4 * 1 + 3] = 255;
    palette[4 * 2] = 20; palette[4 * 2 + 3] = 128;
    var img = new IgbImage { Width = 2, Height = 2, BitsPerPixel = 8, Pixels = [1, 1, 2, 2], Palette = palette, PaletteCount = 256 };
    var rgba = IgbScene.IndexedToRgba(img)!;
    Equal((byte)20, rgba[0], "primeira linha da saída vem da última guardada");
    Equal((byte)128, rgba[3], "alfa preservado");
    Equal((byte)10, rgba[8], "última linha da saída vem da primeira guardada");
    True(IgbScene.IndexedToRgba(new IgbImage { Width = 2, Height = 2, BitsPerPixel = 4, Pixels = [0, 0], Palette = palette }) == null, "4 bpp não deveria ser aceito ainda");
});

Test("MPEG-PS: pacote MPEG-1, PES com PTS, fim e preenchimento de setor", () =>
{
    var ps = new List<byte>();
    ps.AddRange(new byte[] { 0, 0, 1, 0xBA, 0x21, 0, 1, 0, 1, 0x80, 0, 1 });        // pacote MPEG-1 (12 B)
    ps.AddRange(new byte[] { 0, 0, 1, 0xE0, 0, 9, 0xFF, 0x21, 0, 1, 0, 1, 0xAA, 0xBB, 0xCC }); // vídeo: enchimento + PTS + 3 B
    ps.AddRange(new byte[] { 0, 0, 1, 0xC0, 0, 3, 0x0F, 0x80, 0x00 });              // áudio: sem PTS + 2 B
    ps.AddRange(new byte[] { 0, 0, 1, 0xBE, 0, 2, 0xFF, 0xFF });                    // enchimento
    ps.AddRange(new byte[] { 0, 0, 1, 0xB9 });
    while (ps.Count < 0x800) ps.Add(0);
    var (streams, packs, problems) = MpegPs.Demux(ps.ToArray());
    Equal(1, packs, "pacotes");
    Equal(0, problems.Count, "problemas");
    True(streams[0xE0].Payload.ToArray().SequenceEqual(new byte[] { 0xAA, 0xBB, 0xCC }), "conteúdo do vídeo");
    True(streams[0xC0].Payload.ToArray().SequenceEqual(new byte[] { 0x80, 0x00 }), "conteúdo do áudio");
    True(!streams.ContainsKey(0xBE), "enchimento não é fluxo");
    var seq = MpegPs.SequenceHeader([0, 0, 1, 0xB3, 0x28, 0x01, 0xE0, 0xC4]);
    Equal((640, 480, 4), seq!.Value, "cabeçalho de sequência");
    True(MpegPs.Demux(ps.ToArray()[..20]).Problems.Count > 0, "PES cortado não foi apontado");
});

Test("Câmera por volumes: escolha do volume e pose com recuo e inclinação", () =>
{
    // Volume 1 com inclinação de 55° (linha Z da rotação = (0, sen, cos), como nas matrizes B do jogo) e recuo 135.
    float t = 55 * MathF.PI / 180;
    var b = Matrix4x4.CreateRotationX(-t) * Matrix4x4.CreateTranslation(0, 10, 0);
    var zones = new CameraZones(
    [
        new CameraZone(0, new Vector3(-5, 0, -5), new Vector3(5, 200, 5), Matrix4x4.CreateTranslation(0, 0, 50), Matrix4x4.Identity),
        new CameraZone(1, new Vector3(0, 0, 0), new Vector3(100, 200, 100), Matrix4x4.CreateTranslation(0, 0, 135), b),
    ], Matrix4x4.CreateTranslation(0, 1, 0));
    Equal(1, zones.Select(new Vector3(10, 40, 20)), "dentro do volume 1");
    Equal(0, zones.Select(new Vector3(-50, 40, -50)), "fora de todos, sem volume atual");
    Equal(1, zones.Select(new Vector3(-50, 40, -50), current: 1), "fora de todos, mantém o atual");
    var (pos, look) = zones.Pose(1, new Vector3(10, 40, 20));
    True(Vector3.Distance(look, new Vector3(10, 51, 20)) < 1e-3f, $"pivô {look}");
    var expected = new Vector3(10, 51 + 135 * MathF.Sin(t), 20 + 135 * MathF.Cos(t));
    True(Vector3.Distance(pos, expected) < 1e-3f, $"câmera {pos}, esperado {expected}");
});

Test("Sequência: bloco do estilo, sonda de conversa pulada, caixa no quadro certo e janela de combo", () =>
{
    SeqCommand C(string k, params object[] v) => new(k, v);
    var seqs = new Dictionary<string, List<SeqCommand>>
    {
        ["SQ_ATCK1"] =
        [
            C("LABEL", "dual"), C("ACTION", "VATCD1"), C("WCHECK", "Action"),
            C("LABEL", "slash"),
            C("HITBOX", "Set", 60000, 1f, 1f, new[] { 0f, 4, 7 }, new[] { 3f, 3, 3 }), C("ACTION", "TALKWF"), C("WCHECK", "Action"), C("MOVE", "Stop", 1f, 4f),
            C("HITBOX", "Set", 10000, 9f, 2f, new[] { 0f, 4, 14 }, new[] { 8f, 8, 8 }),
            C("ACTION", "VATCS1"), C("WCHECK", "Atck01"), C("ACTION", "KATCS1"), C("WCHECK", "Atck02"), C("JUMP", "SQ_ATCKTM"),
        ],
    };
    var r = new SequenceRunner(seqs, _ => 20f);
    const float frame = 1 / SequenceRunner.FramesPerSecond;
    True(r.Start("SQ_ATCK1", "slash"), "início");
    Equal("VATCS1", r.Action, "primeira ação (sonda de conversa pulada)");
    Equal("Stop", r.Move?.S(0), "MOVE guardado");
    for (int i = 0; i < 9; i++) r.Tick(frame, false);
    Equal(1, r.ActiveHitBoxes().Count(), "caixa ativa no quadro 9");
    Equal(new Vector3(0, 4, 14), r.ActiveHitBoxes().First().Position, "posição da caixa");
    for (int i = 0; i < 2; i++) r.Tick(frame, false);
    Equal(0, r.ActiveHitBoxes().Count(), "caixa desligada no quadro 11");
    for (int i = 0; i < 10; i++) r.Tick(frame, false);
    True(!r.Running && r.Next == null, "sem apertar, o combo termina");

    r.Start("SQ_ATCK1", "slash");
    r.Tick(frame, true);
    for (int i = 0; i < 20; i++) r.Tick(frame, false);
    Equal("KATCS1", r.Action, "com o botão na janela, segue o combo");
    r.Tick(frame, true);
    for (int i = 0; i < 20; i++) r.Tick(frame, false);
    True(!r.Running && r.Next == "SQ_ATCKTM", "fim do combo pula para SQ_ATCKTM");
    True(!r.Start("SQ_ATCK1", "gun"), "estilo inexistente");

    // Numemon: caixas do golpe depois da ACTION valem para ela.
    seqs["SQ_NUME"] =
    [
        C("HITBOX", "Set", 70000, 0f, -1f, new[] { 0f, 7, 0 }, new[] { 9.5f, 14, 9.5f }), C("MOVE", "Stop", 1f, 4f), C("ACTION", "NATK01"),
        C("HITBOX", "Set", 70004, 10f, 5f, new[] { 6.3f, 1.5f, 2.2f }, new[] { 2.5f, 3, 2.5f }), C("WCHECK", "Action"), C("JUMP", "SQ_WAIT"),
    ];
    True(r.Start("SQ_NUME", null), "início do golpe do Numemon");
    Equal(2, r.ActionHitBoxes.Count, "corpo e golpe na ação NATK01");
    for (int i = 0; i < 11; i++) r.Tick(frame, false);
    True(r.ActiveHitBoxes().Any(h => h.Type == 70004), "golpe ativo no quadro 11");
    True(!r.Invincible, "sem MUTEKI");
    seqs["SQ_SDMG"] = [C("LABEL", "common"), C("MUTEKI", "ON", -1), C("MOVE", "Back", 1.6f, 5f), C("ACTION", "SDMGC1"), C("WCHECK", "Moving"), C("JUMP", "SQ_WAIT")];
    True(r.Start("SQ_SDMG", "common") && r.Invincible && r.Move?.S(0) == "Back", "dano: invencível e recuando");
    for (int i = 0; i < 21; i++) r.Tick(frame, false);
    True(!r.Running && !r.Invincible, "fim do dano tira o MUTEKI");
});

Test("IA: faixa pela distância, sorteio acumulado em 16, troca de mapa e espera de sequência externa", () =>
{
    AiChoice A(string s, float ang, float v, int t) => new(s, ang, v, t);
    var near = new AiMap("MAP00", [36 * 36, 45 * 45, 150 * 150], 0.1f,
        [[A("SQ_CHMOD1", 0, 0, 12), A("SQ_WALK", 1.7453f, 15, 16)], [A("SQ_WAIT", 0, 21, 16)], [A("SQ_WALK", 0, 30, 16)], [A("SQ_SILENT", 0, 60, 16)]]);
    var close = new AiMap("MAP01", [22 * 22, 40 * 40, 150 * 150], 0.1f,
        [[A("SQ_ATCK1", 0, 0, 16)], [A("SQ_WALK", 0, 20, 16)], [A("SQ_CHMOD0", 0, 0, 16)], [A("SQ_SILENT", 0, 60, 16)]]);
    Equal(0, near.Band(35 * 35), "faixa 0");
    Equal(2, near.Band(100 * 100), "faixa 2");
    Equal(3, near.Band(200 * 200), "fora");
    Equal("SQ_CHMOD1", near.Pick(10, 11)?.Sequence, "sorteio 11 < 12");
    Equal("SQ_WALK", near.Pick(10, 12)?.Sequence, "sorteio 12 passa para a próxima");
    var seqs = new Dictionary<string, List<SeqCommand>>
    {
        ["SQ_CHMOD0"] = [new("AIMAP", ["MAP00"]), new("JUMP", ["SQ_WAIT"])],
        ["SQ_CHMOD1"] = [new("AIMAP", ["MAP01"]), new("JUMP", ["SQ_WAIT"])],
    };
    int roll = 0;
    var b = new EnemyBrain([near, close], seqs, () => roll);
    const float frame = 1 / EnemyBrain.FramesPerSecond;
    var self = Vector3.Zero;
    b.Tick(frame, self, new Vector3(0, 0, 10), 0);
    Equal("MAP01", b.Map.Name, "CHMOD1 troca para o MAP01");
    b.Tick(frame, self, new Vector3(0, 0, 10), 0);
    Equal("SQ_ATCK1", b.External, "a menos de 22, ataca");
    b.Tick(frame, self, new Vector3(0, 0, 100), 0);
    Equal("SQ_ATCK1", b.External, "espera a sequência externa");
    b.Release();
    b.Tick(frame, self, new Vector3(30, 0, 0), 0);
    True(b.Walking, "entre 22 e 40, anda");
    True(Math.Abs(b.Yaw - 0.1f) < 1e-4f, $"gira no máximo 0,1 rad por quadro ({b.Yaw})");
    for (int i = 0; i < 25; i++) b.Tick(frame, self, new Vector3(30, 0, 0), b.Yaw);
    True(Math.Abs(b.Yaw - MathF.PI / 2) < 1e-3f, "termina virado para o alvo");
});

Test("DTM do Dolphin: cabeçalho e leituras do controle", () =>
{
    var file = new byte[DW4.Formats.Dolphin.DtmFile.HeaderSize + 16];
    "DTM\x1A"u8.CopyTo(file);
    "GDJEB2"u8.CopyTo(file.AsSpan(4));
    file[11] = 1;
    file[12] = 1;
    BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(13), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(21), 2);
    int at = DW4.Formats.Dolphin.DtmFile.HeaderSize;
    file[at] = 1 << 1; file[at + 4] = 128; file[at + 5] = 255; // A + analógico para cima
    file[at + 8 + 1] = 1 << 3; file[at + 12] = 128; file[at + 13] = 128; // R = bit 11 = bit 3 do segundo byte
    var dtm = DW4.Formats.Dolphin.DtmFile.Read(file);
    Equal("GDJEB2", dtm.GameId, "jogo");
    True(dtm.FromSaveState, "save state");
    Equal(2, dtm.Inputs.Count, "leituras");
    True(dtm.Inputs[0].Has(DW4.Formats.Dolphin.GcPadInput.A) && dtm.Inputs[0].Stick.Y > 0.99f, "A e analógico para cima");
    True(dtm.Inputs[1].Has(DW4.Formats.Dolphin.GcPadInput.R) && !dtm.Inputs[1].Has(DW4.Formats.Dolphin.GcPadInput.A), "R");
});

Test("LZ4: bloco com literais e cópia sobreposta", () =>
{
    // "abcabcabcabcX": 3 literais "abc", cópia de 9 a partir de 3 atrás (sobreposta), depois o literal final "X"
    byte[] block = [0x35, (byte)'a', (byte)'b', (byte)'c', 3, 0, 0x10, (byte)'X'];
    Equal("abcabcabcabcX", Encoding.ASCII.GetString(DW4.Formats.Dolphin.Lz4.DecompressBlock(block, 13)), "saída");
});

Test("GDB: soma de verificação dos pacotes", () =>
{
    Equal("63", DW4.Formats.Dolphin.GdbClient.Checksum("c"), "c");
    Equal("3f", DW4.Formats.Dolphin.GdbClient.Checksum("?"), "?");
});

// ---------- dados do jogo (opcional) ----------

if (args.Length == 1)
{
    var dir = args[0];
    var areaPath = Path.Combine(dir, "area.afs");
    var ndmwPath = Path.Combine(dir, "ndmw.afs");

    Test("Jogo: AFS com 1382 e 973 entradas, estrutura consistente", () =>
    {
        using var area = AfsArchive.Open(areaPath);
        using var ndmw = AfsArchive.Open(ndmwPath);
        Equal(1382, area.Entries.Count, "area.afs");
        Equal(973, ndmw.Entries.Count, "ndmw.afs");
        Equal(0, area.Validate().Count + ndmw.Validate().Count, "problemas");
    });

    Test("Jogo: 2274 IGB lidos sem problemas e 5291 chunks Lua 4.0 sem erro", () =>
    {
        int igbs = 0, chunks = 0;
        var problems = new List<string>();
        var sources = new List<(string, byte[])> { ("ndmw.igb", File.ReadAllBytes(Path.Combine(dir, "ndmw.igb"))) };
        foreach (var p in new[] { areaPath, ndmwPath })
        {
            using var afs = AfsArchive.Open(p);
            foreach (var e in afs.Entries)
            {
                if (Classifier.Classify(afs.ReadHead(e, 0x30)) != ContentKind.Igb) continue;
                using var ms = new MemoryStream();
                afs.CopyEntry(e, ms);
                sources.Add(($"{Path.GetFileName(p)} #{e.Index}", ms.ToArray()));
            }
        }
        foreach (var (id, data) in sources)
        {
            var f = IgbFile.Read(data);
            igbs++;
            problems.AddRange(f.Problems.Select(x => $"{id}: {x}"));
            bool le = f.Header.LittleEndian;
            foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beLuaDataObject"))
                foreach (var v in o.Fields.Where(v => v.Desc.ShortName == "MemoryRef"))
                {
                    int r = v.AsInt32(le);
                    if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb || mb.Size == 0) continue;
                    var mem = data.AsSpan((int)mb.FileOffset, mb.Size).ToArray();
                    if (!Lua40Chunk.LooksLikeChunk(mem)) continue;
                    Lua40Chunk.Read(mem);
                    chunks++;
                }
        }
        Equal(2274, igbs, "IGB");
        Equal(0, problems.Count, $"problemas ({string.Join("; ", problems.Take(3))})");
        Equal(5291, chunks, "chunks Lua");
    });

    IgbScene Scene(int index)
    {
        using var afs = AfsArchive.Open(areaPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[index], ms);
        var data = ms.ToArray();
        return IgbScene.Read(IgbFile.Read(data), data);
    }

    Test("Jogo: area #598 é um quad de 4 vértices com o logotipo 128×128", () =>
    {
        var s = Scene(598);
        Equal(1, s.Parts.Count, "geometrias");
        var p = s.Parts[0];
        Equal(4, p.Positions.Count, "posições");
        Equal(4, p.TexCoords.Count, "UVs");
        Equal(6, p.Triangles.Count, "índices de triângulo");
        var img = s.Images[p.ImageIndex];
        Equal("i_bandailogo00.png", img.Name, "nome da imagem");
        Equal((128, 128), (img.Width, img.Height), "tamanho da imagem");
    });

    Test("Jogo: area #3 tem 217 geometrias, 217 transformações e 20 imagens", () =>
    {
        var s = Scene(3);
        Equal(217, s.Parts.Count, "geometrias");
        Equal(217, s.TransformCount, "igTransform");
        Equal(20, s.Images.Count, "imagens");
        Equal(0, s.Parts.Count(p => p.Positions.Count == 0), "geometrias sem posição");
    });

    Test("Jogo: Sofdec AUS_ (#2) separa em fluxos com hashes conhecidos", () =>
    {
        using var afs = AfsArchive.Open(areaPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[2], ms);
        var (streams, packs, problems) = MpegPs.Demux(ms.ToArray());
        Equal(19683, packs, "pacotes");
        Equal(0, problems.Count, "problemas");
        Equal("A927F50F4A287659A8282A22039723D1D5E6EB6E1F661ECF50FDA2C37FE3A8FC", Convert.ToHexString(SHA256.HashData(streams[0xC0].Payload.ToArray())), "SHA-256 do áudio");
        Equal("6278B0561D3A7D51EFA4BB11A103557B34A6B76A331FD1644537065C6934D62B", Convert.ToHexString(SHA256.HashData(streams[0xE0].Payload.ToArray())), "SHA-256 do vídeo");
    });

    Test("Jogo: Guilmon (#857) tem 31 ossos, 26 matrizes, pele rígida e 82 animações", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[857], ms);
        var data = ms.ToArray();
        var ch = IgbCharacter.Read(IgbFile.Read(data), data);
        Equal(31, ch.Bones.Count, "ossos");
        Equal(26, ch.InverseBind.Count, "matrizes de pele");
        Equal("JointRoot", ch.Bones[2].Name, "osso 2");
        Equal(19, ch.Bones[28].Parent, "pai de Tail01");
        Equal(82, ch.Animations.Count, "animações");
        var high = ch.Skins.Single(k => k.Name == "HighAppearance");
        Equal(29, high.MatrixPalette.Count, "paleta");
        Equal(806, high.Parts[0].BlendIndices.Count, "índices de pele");
        True(high.Parts[0].BlendWeights.All(w => w == 1f), "pesos diferentes de 1");
        var waitS = ch.Animations.Single(a => a.Name == "waitS");
        var clip = Enbaya.Decode(ch.EnbayaBlocks[waitS.Tracks.First(t => t.SourceIndex >= 0).SourceIndex]);
        Equal(30, clip.SampleRate, "taxa");
        True(Math.Abs(clip.Duration - waitS.Duration) < 0.001, $"duração {clip.Duration} × {waitS.Duration}");
    });

    Test("Jogo: as 7554 fontes Enbaya decodificam sem erro", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        int sources = 0;
        foreach (var e in afs.Entries)
        {
            if (Classifier.Classify(afs.ReadHead(e, 0x30)) != ContentKind.Igb) continue;
            using var ms = new MemoryStream();
            afs.CopyEntry(e, ms);
            var data = ms.ToArray();
            var f = IgbFile.Read(data);
            foreach (var src in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igEnbayaAnimationSource")))
            {
                var mr = src.Fields.First(v => v.Desc.ShortName == "MemoryRef");
                var mb = (IgbMemoryBlock)f.Slots[mr.AsInt32(true)];
                Enbaya.Decode(data.AsSpan((int)mb.FileOffset, mb.Size));
                sources++;
            }
        }
        Equal(7554, sources, "fontes");
    });

    Test("Jogo: colisão da fase dng1 (#179, raiz 1862) tem 36 geometrias e 458 triângulos", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[179], ms);
        var data = ms.ToArray();
        var parts = IgbScene.Read(IgbFile.Read(data), data).Parts.Where(p => p.SceneRoot == 1862).ToList();
        Equal(36, parts.Count, "geometrias");
        Equal(458, parts.Sum(p => p.Triangles.Count) / 3, "triângulos");
    });

    Test("Jogo: SQ_ATCK1 do Guilmon, bloco slash: arco de 5 caixas 10000 nos quadros 9 a 13, depois VATCS1 (animação vatcS)", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[857], ms);
        var data = ms.ToArray();
        var notes = new List<string>();
        var seqs = IgbSequences.Read(IgbFile.Read(data), data, null, notes);
        Equal(28, seqs.Count, "sequências");
        Equal(156, seqs["SQ_ATCK1"].Count, "comandos de SQ_ATCK1");
        var game = seqs.ToDictionary(k => k.Key, k => k.Value.Select(c => new SeqCommand(c.Kind, c.Values)).ToList());
        var r = new SequenceRunner(game, _ => 30f);
        True(r.Start("SQ_ATCK1", "slash"), "bloco slash");
        Equal("VATCS1", r.Action, "primeira ação");
        var boxes = r.ActionHitBoxes.Where(h => h.Type == 10000).ToList();
        Equal(5, boxes.Count, "caixas 10000");
        Equal("9,10,11,12,13", string.Join(",", boxes.Select(b => b.StartFrame)), "quadros iniciais");
        True(boxes.All(b => b.Frames == 2 && b.Size == new Vector3(8, 8, 8)), "duração 2 e tamanho 8");
        Equal(new Vector3(-9, 4, 8), boxes[0].Position, "primeira caixa à esquerda");
        var acts = IgbSequences.ReadActions(IgbFile.Read(data), data, null, notes)
            .ToDictionary(k => k.Key, k => k.Value.Select(c => new SeqCommand(c.Kind, c.Values)).ToList());
        Equal(183, acts.Count, "ações (beAction2Data)");
        Equal("vatcS", ActionAnimations.Motion(acts, "VATCS1"), "animação de VATCS1");
        Equal(11f, ActionAnimations.Length(acts, "VATCS1", m => m == "vatcS" ? 11f : null), "VATCS1 espera o fim da animação");
        Equal(60f, ActionAnimations.Length(acts, "KATCS1", _ => 7f), "KATCS1 espera 60 quadros");
        True(ActionAnimations.Loops(acts, "KATCS1") && !ActionAnimations.Loops(acts, "VATCS1"), "LP e OS");
    });

    Test("Jogo: IA do Numemon (#179, e_nume) tem MAP00 e MAP01 com 4 listas e faixas 36²/45²/150² e 22²/40²/150²", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[179], ms);
        var data = ms.ToArray();
        var notes = new List<string>();
        var maps = IgbSequences.ReadAiMaps(IgbFile.Read(data), data, "e_nume", notes);
        Equal("MAP00,MAP01", string.Join(",", maps.Select(m => m.Name).Order()), "mapas do Numemon");
        var m0 = maps.First(m => m.Name == "MAP00");
        var m1 = maps.First(m => m.Name == "MAP01");
        Equal("1296,2025,22500", string.Join(",", m0.Values.Take(3)), "faixas do MAP00");
        Equal("484,1600,22500", string.Join(",", m1.Values.Take(3)), "faixas do MAP01");
        Equal("3,4,5,1", string.Join(",", m0.Lists.Select(l => l.Count)), "itens por lista no MAP00");
        Equal("4,5,4,1", string.Join(",", m1.Lists.Select(l => l.Count)), "itens por lista no MAP01");
        Equal("SQ_ATCK1", m1.Lists[0][0].Values[0], "MAP01 ataca de perto");
        Equal(16, m1.Lists[0][^1].Values[3], "último limiar é 16");
        Equal("NEAR", m0.Strings.FirstOrDefault(), "critério do alvo");
    });

    Test("Jogo: câmera da dng1 (#180) tem 18 volumes; #181 só muda o recuo; câmera sempre acima do alvo", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        IgbCameraSet Read(int i)
        {
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[i], ms);
            var data = ms.ToArray();
            return IgbCameraSet.Read(IgbFile.Read(data), data);
        }
        var near = Read(180);
        var far = Read(181);
        Equal(18, near.Boxes.Count, "volumes #180");
        Equal(18, far.Boxes.Count, "volumes #181");
        Equal(135f, near.Boxes[0].Distance, "recuo do volume 0");
        Equal(90f, near.Boxes[1].Distance, "recuo do volume 1 (#180)");
        Equal(120f, far.Boxes[2].Distance, "recuo do volume 2 (#181)");
        for (int i = 0; i < 18; i++)
        {
            True(near.Boxes[i].Min.SequenceEqual(far.Boxes[i].Min) && near.Boxes[i].Max.SequenceEqual(far.Boxes[i].Max), $"caixa {i} difere");
            True(near.Boxes[i].B.SequenceEqual(far.Boxes[i].B), $"B {i} difere");
        }
        static Matrix4x4 M(float[] m) => new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
        var zones = new CameraZones(near.Boxes.Select(b => new CameraZone(b.Index, new Vector3(b.Min[0], b.Min[1], b.Min[2]),
            new Vector3(b.Max[0], b.Max[1], b.Max[2]), M(b.A), M(b.B))), M(near.Common));
        foreach (var z in zones.Zones)
        {
            var target = ((z.Min + z.Max) / 2) with { Y = z.Min.Y };
            int sel = zones.Select(target);
            True(sel == 0 || zones.Zones.First(x => x.Index == sel).Contains(target), $"volume {sel} escolhido não contém o centro de {z.Index}");
            var (pos, look) = zones.Pose(z.Index, target);
            True(pos.Y > look.Y, $"volume {z.Index}: câmera abaixo do alvo ({pos} olhando {look})");
        }
    });
}
else Console.WriteLine("(sem pasta do jogo: só testes sintéticos; passe a pasta files\\ da cópia de trabalho para conferir os dados)");

Console.WriteLine($"{passed} ok, {failed} falharam");
return failed == 0 ? 0 : 1;

static bool Throws(Action a)
{
    try { a(); return false; }
    catch (InvalidDataException) { return true; }
}

static uint Crc32(ReadOnlySpan<byte> data)
{
    uint crc = 0xFFFFFFFF;
    foreach (var b in data)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
    }
    return crc ^ 0xFFFFFFFF;
}
