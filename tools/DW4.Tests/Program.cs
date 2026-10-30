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
using DW4.Script;
using DW4.Script.Be;

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

Test("Port: save do personagem guarda e devolve formas, inventário, cartas e estado comum", () =>
{
    var ram = new GameRam { Difficulty = 2, CameraMode = 1 };
    var p = ram.Players[0];
    p.Forms.Start(2);
    p.Forms[2].Level = 17; p.Forms[2].Exp = 12345; p.Forms[2].Skill[2] = 420; p.Forms[2].UpCount[3] = 5;
    p.Forms.Unlock(6);
    p.Items.Bit = 999;
    p.Items.Add(new ItemRef("weapon", 25)); p.Items.Add(new ItemRef("armor1", 3));
    p.Items.Add(new ItemRef("conItem", 1)); p.Items.Add(new ItemRef("conItem", 1)); p.Items.Add(new ItemRef("impItem", 7));
    p.Cards[4] = 1;
    ram.SetFlag(409, true); ram.SetCounter(1, 6000);
    var save = DW4.Script.Save.CharacterSave.Capture(ram, 0, "Gui", "stagml11", 3);
    Equal(17, save.Level, "nível da forma atual");
    var back = DW4.Script.Save.CharacterSave.FromJson(save.ToJson())!;
    var ram2 = new GameRam();
    back.Apply(ram2, 1);
    var q = ram2.Players[1];
    Equal(2, q.Forms.Current, "forma atual");
    Equal((17, 12345L, 420, 5), (q.Forms[2].Level, q.Forms[2].Exp, q.Forms[2].Skill[2], q.Forms[2].UpCount[3]), "estado da forma");
    True(q.Forms[6].Unlocked && !q.Forms[5].Unlocked, "formas liberadas");
    Equal(999, q.Items.Bit, "BIT");
    True(q.Items.Has(new ItemRef("weapon", 25)) && q.Items.Has(new ItemRef("armor1", 3)), "DEVICEFOLDER");
    Equal(2, q.Items.Disks(1), "DISKFOLDER");
    True(q.Items.HasFlag(7), "impItem");
    Equal((byte)1, q.Cards[4], "CARDLIST");
    Equal((1, 6000, 2, 1), (ram2.FlagOn(409), ram2.Counter(1), ram2.Difficulty, ram2.CameraMode), "FLAG, COUNTER, DIFFICULTY e CAMERAMODE");
    Equal(("Gui", "stagml11", 3), (back.Name, back.Stage, back.Entry), "nome e lugar");
    Equal(null, DW4.Script.Save.CharacterSave.FromJson("{\"Version\": 99}"), "versão futura não carrega");
    Equal(null, DW4.Script.Save.CharacterSave.FromJson("lixo"), "texto qualquer não carrega");
    var fresh = DW4.Script.Save.CharacterSave.New("Novo", 2, "p_girumon", 0, "stagml11", 3);
    Equal((2, 1, "p_girumon"), (fresh.CurrentForm, fresh.Level, fresh.Model), "personagem novo: inicial no nível 1");
    True(fresh.Forms[2].Unlocked && fresh.Forms.Count(f => f.Unlocked) == 1, "só a inicial liberada");
});

Test("Port: vagas da conta (ICharacterStore local): 4 vagas, vazia = null, grava, lê e apaga por conta", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "dw4-teste-vagas-" + Guid.NewGuid().ToString("N"));
    try
    {
        DW4.Script.Save.ICharacterStore store = new DW4.Script.Save.LocalCharacterStore(root);
        True(store.List("local") is { Count: 4 } l && l.All(x => x == null), "conta nova: 4 vagas vazias");
        store.Save("local", 2, DW4.Script.Save.CharacterSave.New("Gui", 2, "p_girumon", 0, "stagml11", 3));
        var list = store.List("local");
        True(list[0] == null && list[1] == null && list[2]?.Name == "Gui" && list[3] == null, "vaga 3 ocupada");
        True(File.Exists(Path.Combine(root, "accounts", "local", "slot3.json")), "arquivo accounts/local/slot3.json");
        True(store.List("outra").All(x => x == null), "outra conta não vê as vagas");
        store.Delete("local", 2);
        True(store.Load("local", 2) == null, "apagada");
        var bad = false;
        try { store.Save("../fora", 0, DW4.Script.Save.CharacterSave.New("x", 0, "p_agumon", 0, "s", 0)); } catch (ArgumentException) { bad = true; }
        True(bad, "nome de conta com caminho é recusado");
        bad = false;
        try { store.Load("local", 4); } catch (ArgumentOutOfRangeException) { bad = true; }
        True(bad, "vaga 5 não existe");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
});


Test("Port: banco (BANKBIT e BANKDEVICE até 96) e o banco no save", () =>
{
    var inv = new Inventory { Bit = 1000 };
    True(inv.DepositBit(600) && inv.Bit == 400 && inv.BankBit == 600, "deposita BIT");
    True(!inv.DepositBit(401) && !inv.DepositBit(0), "não deposita mais do que tem nem 0");
    True(inv.WithdrawBit(100) && inv.Bit == 500 && inv.BankBit == 500, "saca");
    True(!inv.WithdrawBit(501), "não saca mais do que guardou");
    inv.Add(new ItemRef("weapon", 3));
    True(inv.DepositDevice(new ItemRef("weapon", 3)) && inv.Devices.Count == 0 && inv.BankDevices.Count == 1, "item vai para o banco");
    True(!inv.DepositDevice(new ItemRef("conItem", 1)), "disco não entra no banco de itens");
    for (int i = 0; i < 95; i++) { inv.Add(new ItemRef("abChip", i % 30)); inv.DepositDevice(new ItemRef("abChip", i % 30)); }
    Equal(96, inv.BankDevices.Count, "96 itens");
    inv.Add(new ItemRef("abChip", 1));
    True(!inv.DepositDevice(new ItemRef("abChip", 1)), "o 97º não cabe");
    True(inv.WithdrawDevice(new ItemRef("weapon", 3)) && inv.Has(new ItemRef("weapon", 3)), "saca o item");
    var ram = new GameRam();
    ram.Players[0].Items = inv;
    ram.Players[0].Forms.Start(0);
    var back = DW4.Script.Save.CharacterSave.FromJson(DW4.Script.Save.CharacterSave.Capture(ram, 0, "b", "s", 0).ToJson())!;
    var ram2 = new GameRam();
    back.Apply(ram2, 0);
    Equal((500, 95), (ram2.Players[0].Items.BankBit, ram2.Players[0].Items.BankDevices.Count), "banco gravado");
});

Test("Port: fortalecer chip com X-data (NEXTX da linha; linha +1; XDATA até 9999)", () =>
{
    var inv = new Inventory { XData = 100 };
    inv.Add(new ItemRef("weapon", 1)); inv.Add(new ItemRef("abChip", 0));
    True(!inv.UpgradeChip(0, 10), "arma não fortalece");
    True(!inv.UpgradeChip(1, -1), "NEXTX −1 não fortalece");
    True(!inv.UpgradeChip(1, 120), "X-data insuficiente");
    True(inv.UpgradeChip(1, 60) && inv.XData == 40 && inv.Devices[1] == new ItemRef("abChip", 1), "XDATA − 60 e linha 0 → 1");
    inv.XData = 12345;
    Equal(Inventory.XDataMax, inv.XData, "teto 9999");
});

Test("Port: chat (\"nome: mensagem - HH:MM\", histórico, some sem uso, canal local)", () =>
{
    var t = new DateTime(2026, 10, 25, 9, 5, 0);
    Equal("Gui: oi - 09:05", new ChatMessage("Gui", "oi", t).Format(), "formato");
    Equal("a b", ChatLog.Clean("  a\r\nb\t "), "uma linha só");
    Equal(ChatLog.MaxLength, ChatLog.Clean(new string('x', 500)).Length, "limite de letras");
    var log = new ChatLog { Capacity = 5, Shown = 3, FadeAfter = 8 };
    var ch = new LocalChatChannel { Clock = () => t };
    ch.Received += log.Add;
    ch.Send("Gui", "   ");
    Equal(0, log.All.Count, "mensagem vazia não vai");
    for (int i = 1; i <= 7; i++) ch.Send("Gui", $"m{i}");
    Equal(5, log.All.Count, "guarda até a capacidade");
    Equal("Gui: m5 - 09:05|Gui: m6 - 09:05|Gui: m7 - 09:05", string.Join("|", log.Recent), "mostra as últimas 3");
    True(log.Visible, "aparece com mensagem nova");
    log.Tick(8.1f);
    True(!log.Visible, "some depois de 8 s sem uso");
    log.Open();
    True(log.Visible && log.Typing, "abrir a caixa mostra de novo");
    log.Tick(30);
    True(log.Visible, "digitando não some");
    log.Close();
    log.Tick(7);
    True(log.Visible, "ainda visível logo depois de fechar");
});

Test("Port: personagem novo com as armas iniciais (INI_WEAPON1..3), a primeira equipada, e o equipamento no save", () =>
{
    var s = DW4.Script.Save.CharacterSave.New("Gui", 2, "p_girumon", 0, "stagml11", 3, [268, 275, 274]);
    var ram = new GameRam();
    DW4.Script.Save.CharacterSave.FromJson(s.ToJson())!.Apply(ram, 0);
    var p = ram.Players[0];
    True(p.Items.Has(new ItemRef("weapon", 268)) && p.Items.Has(new ItemRef("weapon", 275)) && p.Items.Has(new ItemRef("weapon", 274)), "as 3 na pasta");
    Equal(268, p.Equipment?.WeaponRow, "a primeira equipada");
    p.Equipment = new Equipment(275, 3) { WeaponChips = [7] };
    var back = new GameRam();
    DW4.Script.Save.CharacterSave.FromJson(DW4.Script.Save.CharacterSave.Capture(ram, 0, "Gui", "s", 0).ToJson())!.Apply(back, 0);
    Equal((275, 3, 7), (back.Players[0].Equipment!.WeaponRow, back.Players[0].Equipment!.Armor1Row, back.Players[0].Equipment!.WeaponChips[0]), "arma, armadura e chip gravados");
});

Test("Port: X-data nos atributos (custo ⌈(valor − 45)/5⌉, teto 400, mínimo 1; UPCNT 2..6, 0, 1)", () =>
{
    Equal((0, 1, 1, 2, 11, 400, 1), (XDataStatus.Cost(45), XDataStatus.Cost(46), XDataStatus.Cost(50), XDataStatus.Cost(51), XDataStatus.Cost(100), XDataStatus.Cost(9999), XDataStatus.Cost(10)), "custos");
    Equal((2, 3, 4, 5, 6, 0, 1), (XDataStatus.UpCountSlot(0), XDataStatus.UpCountSlot(1), XDataStatus.UpCountSlot(2), XDataStatus.UpCountSlot(3), XDataStatus.UpCountSlot(4), XDataStatus.UpCountSlot(5), XDataStatus.UpCountSlot(6)), "UPCNT");
    var inv = new Inventory { XData = 20 };
    var up = new int[11];
    True(XDataStatus.Raise(inv, up, 0, 100) && inv.XData == 9 && up[2] == 1, "força 100: custa 11, UPCNT[2] + 1");
    True(!XDataStatus.Raise(inv, up, 5, 200), "MHP 200 custa 31: falta X-data");
    True(XDataStatus.Raise(inv, up, 6, 50) && up[1] == 1 && inv.XData == 8, "MMP 50: custa 1, UPCNT[1]");
});
Test("Port: B. Pack (gasta o conItem 10, 3 cartas por MT % 1000 contra a soma do KAKURITSU, marca a CARDLIST)", () =>
{
    var card = new GameTables.Table(3, new() { ["KAKURITSU"] = [600, 300, 99], ["RARE"] = [0, 1, 2] });
    var inv = new Inventory();
    inv.Add(new ItemRef("conItem", CardPack.PackRow));
    var list = new byte[50];
    // r = 599 → 0; 600 → 1; 999 → soma 999 não passa → última
    var rows = CardPack.Open(inv, card, list, new SeqRandom(1599, 2600, 999));
    Equal((0, 1, 2), (rows[0], rows[1], rows[2]), "linhas sorteadas");
    Equal(0, inv.Disks(CardPack.PackRow), "pacote gasto");
    True(list[0] == 1 && list[1] == 1 && list[2] == 1 && list[3] == 0, "coleção marcada");
});
Test("Port: configurações em texto do ConfigFile (ida e volta, padrão e faixa)", () =>
{
    var c = new PortConfig { CameraMode = 1, Vibration = false, Width = 1920, Height = 1080, Fullscreen = true, VSync = false, UiScale = 1.25f, MusicVolume = 0.5f, Account = "eu" };
    var ini = c.ToIni();
    True(ini.Contains("[grafico]") && ini.Contains("largura=1920") && ini.Contains("nome=\"eu\""), "seções e chaves");
    var d = PortConfig.FromIni(ini);
    Equal((1, false, 1920, 1080, true, false, 1.25f, 0.5f, "eu"), (d.CameraMode, d.Vibration, d.Width, d.Height, d.Fullscreen, d.VSync, d.UiScale, d.MusicVolume, d.Account), "ida e volta");
    var def = PortConfig.FromIni("");
    Equal((1280, 720, true, 1f, "local"), (def.Width, def.Height, def.VSync, def.MasterVolume, def.Account), "padrão");
    var odd = PortConfig.FromIni("[som]\ngeral=7\nmusica=abc\n[conta]\nnome=\"../x\"\n[grafico]\nescala=9");
    Equal((1f, 0.8f, "local", 2f), (odd.MasterVolume, odd.MusicVolume, odd.Account, odd.UiScale), "fora da faixa ou inválido");
    Equal(-80f, PortConfig.ToDb(0), "volume 0 = silêncio");
    True(Math.Abs(PortConfig.ToDb(0.5f) + 6.02f) < 0.01f, "0,5 ≈ −6 dB");
});

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

Test("MPEG-1: tabelas de VLC sem prefixo repetido e IDCT igual à fórmula direta", () =>
{
    var problems = Mpeg1Video.CheckTables();
    True(problems.Count == 0, string.Join("; ", problems.Take(5)));
    var rng = new Random(7);
    var block = new int[64];
    for (int i = 0; i < 64; i++) block[i] = i < 10 ? rng.Next(-300, 300) : 0;
    var input = (int[])block.Clone();
    Mpeg1Video.Idct(block, new double[64]);
    for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            double s = 0;
            for (int v = 0; v < 8; v++)
                for (int u = 0; u < 8; u++)
                    s += (u == 0 ? Math.Sqrt(0.5) : 1) * (v == 0 ? Math.Sqrt(0.5) : 1) / 4 * input[v * 8 + u]
                        * Math.Cos((2 * x + 1) * u * Math.PI / 16) * Math.Cos((2 * y + 1) * v * Math.PI / 16);
            True(Math.Abs(block[y * 8 + x] - s) <= 0.5 + 1e-9, $"IDCT em ({x}, {y}): {block[y * 8 + x]} contra {s:0.###}");
        }
    var dc = new int[64];
    dc[0] = 100 * 8;
    Mpeg1Video.Idct(dc, new double[64]);
    True(dc.All(v => v == 100), "bloco só com DC vira constante");
});

Test("MPEG-1: fluxo montado aqui (I com DC, P copiando e P com meio pixel) sai na ordem certa", () =>
{
    var bits = new List<bool>();
    void B(string s) { foreach (char c in s) if (c != ' ') bits.Add(c == '1'); }
    void U(int v, int n) { for (int i = n - 1; i >= 0; i--) bits.Add(((v >> i) & 1) == 1); }
    void Align() { while (bits.Count % 8 != 0) bits.Add(false); }
    void Start(int code) { Align(); U(0, 16); U(1, 8); U(code, 8); }
    string[] luma = ["100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110"];
    string[] chroma = ["00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110"];
    void Dc(int diff, bool isLuma)
    {
        int size = diff == 0 ? 0 : 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(diff));
        B((isLuma ? luma : chroma)[size]);
        if (size > 0) U(diff > 0 ? diff : diff + (1 << size) - 1, size);
        B("10"); // fim do bloco
    }
    // Sequência 32 × 16 (dois macroblocos), 29,97 fps, matrizes padrão.
    Start(0xB3); U(32, 12); U(16, 12); U(1, 4); U(4, 4); U(0x3FFFF, 18); U(1, 1); U(20, 10); U(0, 1); U(0, 1); U(0, 1);
    // I: luma 100 no primeiro macrobloco e 200 no segundo, croma neutro.
    Start(0x00); U(0, 10); U(1, 3); U(0xFFFF, 16); U(0, 1);
    Start(0x01); U(8, 5); U(0, 1);
    B("1"); B("1"); Dc(100 - 128, true); Dc(0, true); Dc(0, true); Dc(0, true); Dc(0, false); Dc(0, false);
    B("1"); B("1"); Dc(200 - 100, true); Dc(0, true); Dc(0, true); Dc(0, true); Dc(0, false); Dc(0, false);
    // P com vetor zero nos dois (MC sem resíduo): cópia da I.
    Start(0x00); U(1, 10); U(2, 3); U(0xFFFF, 16); U(0, 1); U(1, 3); U(0, 1);
    Start(0x01); U(8, 5); U(0, 1);
    B("1"); B("001"); B("1"); B("1");
    B("1"); B("001"); B("1"); B("1");
    // P com +1/2 pixel na horizontal no primeiro: a coluna 15 mistura 100 e 200.
    Start(0x00); U(2, 10); U(2, 3); U(0xFFFF, 16); U(0, 1); U(1, 3); U(0, 1);
    Start(0x01); U(8, 5); U(0, 1);
    B("1"); B("001"); B("010"); B("1");
    B("1"); B("001"); B("1"); B("1");
    Start(0xB7);
    Align();
    var data = new byte[bits.Count / 8];
    for (int i = 0; i < bits.Count; i++) if (bits[i]) data[i >> 3] |= (byte)(0x80 >> (i & 7));

    var dec = new Mpeg1Video(data);
    Equal((32, 16), (dec.Width, dec.Height), "tamanho");
    True(Math.Abs(dec.FrameRate - 29.97) < 0.01, "quadros por segundo");
    var types = new List<char>();
    var rows = new List<byte[]>();
    while (dec.Next() is { } fr)
    {
        types.Add(fr.Type);
        rows.Add(fr.Y.AsSpan(5 * fr.Stride, 32).ToArray());
        Equal(128, (int)fr.Cb[3], "croma neutro");
    }
    Equal(0, dec.Problems.Count, string.Join("; ", dec.Problems));
    Equal("IPP", new string(types.ToArray()), "ordem de exibição");
    True(rows[0].Take(16).All(v => v == 100) && rows[0].Skip(16).All(v => v == 200), "I: " + string.Join(",", rows[0]));
    True(rows[1].SequenceEqual(rows[0]), "P com vetor zero copia a I");
    True(rows[2].Take(15).All(v => v == 100) && rows[2][15] == 150 && rows[2].Skip(16).All(v => v == 200), "meio pixel: " + string.Join(",", rows[2]));
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

Test("Objeto: caixa do HITBOX vale do quadro inicial até o fim da duração; AIMAP fica para a IA (f_bit SQ_PHASE2)", () =>
{
    SeqCommand C(string k, params object[] v) => new(k, v);
    var seqs = new Dictionary<string, List<SeqCommand>>
    {
        ["SQ_ATK01"] = [C("HITBOX", "Set", 70069, 24f, 6f, new[] { 0f, -6, -52 }, new[] { 4f, 4, 4 }), C("WAITCHECK", "Frame", 60f), C("JUMP", "SQ_WAIT")],
        ["SQ_WAIT"] = [C("WAITCHECK", "Frame", 1000f)],
        ["SQ_PHASE2"] = [C("AIMAP", "MAP01"), C("JUMP", "SQ_WAIT")],
    };
    var s = new ObjectSequencer(seqs, new Dictionary<string, List<SeqCommand>>());
    s.Start("SQ_ATK01");
    const float f = 1 / ObjectSequencer.FramesPerSecond;
    var on = new List<int>();
    for (int i = 0; i < 50; i++) { if (s.ActiveHitBoxes.Any()) on.Add(i); s.Tick(f); }
    True(on.Count > 0 && on.Min() >= 23 && on.Min() <= 25 && on.Max() <= 30, $"caixa dos quadros 24 a 30 ({string.Join(",", on)})");
    s.Start("SQ_PHASE2");
    Equal("MAP01", s.TakeAiMap(), "AIMAP do SQ_PHASE2");
    Equal(null, s.TakeAiMap(), "consumido");
    seqs["SQ_BMH"] = [C("LUACALL", "bmh", ""), C("JUMP", "SQ_WAIT")];
    s.Start("SQ_BMH");
    Equal("bmh", s.TakeLuaCall(), "LUACALL entregue (f_devl1f2)");
    Equal(null, s.TakeLuaCall(), "uma vez");
});

Test("Sequência: caixas agendadas antes de uma ação curta passam para a ação seguinte (CarryHitBoxes, SQ_MAGIC do f_apocaly)", () =>
{
    SeqCommand C(string k, params object[] v) => new(k, v);
    var seqs = new Dictionary<string, List<SeqCommand>>
    {
        ["SQ_MAGIC"] =
        [
            C("HITBOX", "Set", 70070, 24f, 12f, new[] { 0f, 0, 0 }, new[] { 20f, 30, 20 }),
            C("HITBOX", "Set", 70070, 48f, 36f, new[] { 0f, 0, 0 }, new[] { 27f, 30, 27 }),
            C("ACTION", "ACTN04"), C("WCHECK", "Action"), C("ACTION", "BLST03"), C("WCHECK", "Action"), C("JUMP", "SQ_WAIT"),
        ],
    };
    const float frame = 1 / SequenceRunner.FramesPerSecond;
    float Len(string a) => a == "ACTN04" ? 20 : 60;
    foreach (bool carry in new[] { true, false })
    {
        var r = new SequenceRunner(seqs, a => Len(a)) { CarryHitBoxes = carry };
        r.Start("SQ_MAGIC", null);
        var activeAt = new List<int>();
        for (int f = 0; f < 120 && r.Running; f++)
        {
            if (r.ActiveHitBoxes().Any()) activeAt.Add(f);
            r.Tick(frame, false);
        }
        if (carry)
        {
            True(activeAt.Count > 0 && activeAt.Min() >= 23 && activeAt.Min() <= 25, $"a 1ª caixa liga perto do quadro 24 da sequência ({(activeAt.Count > 0 ? activeAt.Min() : -1)})");
            True(activeAt.Max() >= 78, $"a 2ª caixa (48..84) vale até o fim da sequência, quadro 80 ({activeAt.Max()})");
        }
        else Equal(0, activeAt.Count, "sem carregar, a ação curta perde as caixas (comportamento do jogador)");
    }
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

    // Toques (aperta e solta): cada toque é só o golpe; não entra na pose de carregar.
    r.Start("SQ_ATCK1", "slash");
    r.Tick(frame, true, attackHeld: false);
    for (int i = 0; i < 25; i++) r.Tick(frame, i % 5 == 0, attackHeld: false);
    True(!r.Running && r.Next == null, "toques rápidos: só o golpe, sem carregar");

    // Segurando: golpe → pose de carregar (KATCS1) → continua carregando enquanto segura → ao soltar, golpe carregado.
    r.Start("SQ_ATCK1", "slash");
    for (int i = 0; i < 25; i++) r.Tick(frame, false, attackHeld: true);
    Equal("KATCS1", r.Action, "segurando depois do golpe: carrega");
    for (int i = 0; i < 60; i++) r.Tick(frame, false, attackHeld: true);
    True(r.Running && r.Action == "KATCS1", "segurando: continua carregando");
    r.Tick(frame, false, attackHeld: false);
    True(!r.Running && r.Next == "SQ_ATCKTM", "soltou: dispara o golpe carregado (SQ_ATCKTM)");

    // Soltou antes do fim do golpe: não carrega.
    r.Start("SQ_ATCK1", "slash");
    for (int i = 0; i < 25; i++) r.Tick(frame, false, attackHeld: i < 10);
    True(!r.Running && r.Next == null, "soltou durante o golpe: sem carregar");
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

Test("Técnicas: WAZALIST pelo nível e pela skill, painel com MP e recarga", () =>
{
    // Tabelas sintéticas no formato do igb-db: uma família de 5 graus (linhas 0..4) e uma bloqueada até o nível 12.
    static string Col(string name, params object[] cells) =>
        $"{{\"name\":\"{name}\",\"cells\":[{string.Join(",", cells.Select(c => c is string t ? $"\"{t}\"" : c.ToString()))}]}}";
    var pc = new[] { Col("NAME", "f1", "f2", "f3", "f4", "f5", "d1", "d2", "d3", "d4", "d5"), Col("LABEL", "fire1", "fire2", "fire3", "fire4", "fire5", "dark1", "dark2", "dark3", "dark4", "dark5"),
        Col("SKILL", 6, 6, 6, 6, 6, 6, 6, 6, 6, 6), Col("MP", 16, 64, 1, 1, 1, 20, 1, 1, 1, 1), Col("DELAY", 75, 1, 1, 1, 1, 1, 1, 1, 1, 1),
        Col("HP", -160, -1, -1, -1, -1, -1, -1, -1, -1, -1), Col("RANK", 1, 2, 3, 4, 5, 1, 2, 3, 4, 5), Col("CHIMEIRITSU", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
        Col("ZOKUSEI", "fire", "fire", "fire", "fire", "fire", "dark", "dark", "dark", "dark", "dark") };
    var sh = new List<string> { Col("TECH1", 0), Col("TECH1_LEVEL", 1), Col("TECH2", 5), Col("TECH2_LEVEL", 12) };
    int[] s1 = [1, 301, 751, 1251, 1751], s2 = [331, 631, 1071, 1471, 1971];
    for (int m = 1; m <= 5; m++) { sh.Add(Col($"TECH1_SKILL{m}", s1[m - 1])); sh.Add(Col($"TECH2_SKILL{m}", s2[m - 1])); }
    for (int n = 3; n <= 6; n++) sh.Add(Col($"TECH{n}_LEVEL", 999));
    var db = GameTables.FromJson($"{{\"pcTech\":{{\"rows\":10,\"columns\":[{string.Join(",", pc)}]}},\"shinka\":{{\"rows\":1,\"columns\":[{string.Join(",", sh)}]}}}}");
    int[] skill = [1, 1, 1, 1, 1, 1, 1, 1, 1];
    var list = Techniques.List(db, 0, 1, skill);
    Equal("fire1", string.Join(",", list.Select(t => t.Label)), "nível 1, skill 1");
    Equal(0, list[0].Stats.Element, "elemento fogo");
    skill[6] = 301;
    Equal("fire2", string.Join(",", Techniques.List(db, 0, 11, skill).Select(t => t.Label)), "skill 301, nível 11");
    Equal("fire2", string.Join(",", Techniques.List(db, 0, 12, skill).Select(t => t.Label)), "dark pede skill 331");
    skill[6] = 631;
    Equal("fire2,dark2", string.Join(",", Techniques.List(db, 0, 12, skill).Select(t => t.Label)), "skill 631, nível 12");
    var panel = new TechniquePanel();
    var fire = Techniques.List(db, 0, 1, [1, 1, 1, 1, 1, 1, 1, 1, 1])[0];
    int mp = 20;
    Equal("fire1", panel.TryUse(fire, ref mp), "usa com 20 MP");
    Equal(4, mp, "custo 16");
    Equal(1.25f, panel.Cooldown(fire), "DELAY 75 quadros");
    mp = 100;
    Equal(null, panel.TryUse(fire, ref mp), "em recarga");
    panel.Tick(1.3f);
    mp = 15;
    Equal(null, panel.TryUse(fire, ref mp), "MP insuficiente");
    Equal(15, mp, "MP intacto");
});

Test("Inventário: 36 equipamentos, discos em pilha de 9, itens importantes como bit", () =>
{
    var inv = new Inventory();
    for (int i = 0; i < Inventory.DeviceMax; i++) True(inv.Add(new ItemRef(i % 2 == 0 ? "weapon" : "armor2", i)), $"equipamento {i}");
    True(!inv.Add(new ItemRef("abChip", 0)), "37º equipamento fica no chão");
    for (int i = 0; i < Inventory.DiskMax; i++) True(inv.Add(new ItemRef("conItem", 0)), $"disco {i}");
    True(!inv.Add(new ItemRef("conItem", 0)), "10º disco igual fica no chão");
    True(inv.Add(new ItemRef("conItem", 3)), "outro disco tem pilha própria");
    True(inv.Add(new ItemRef("impItem", 2)) && inv.HasFlag(2), "item importante");
    True(!inv.Add(new ItemRef("insItem", 0)), "insItem não vai para o inventário");
    var db = GameTables.FromJson("{\"conItem\":{\"rows\":4,\"columns\":[{\"name\":\"HP\",\"cells\":[450,0,0,0]},{\"name\":\"MP\",\"cells\":[0,0,0,400]}]}}");
    Equal((450, 0), inv.UseDisk(db, 0), "HPディスクα");
    Equal(8, inv.Disks(0), "gastou um");
    Equal((0, 400), inv.UseDisk(db, 3), "disco 3");
    Equal(null, inv.UseDisk(db, 3), "sem disco");
});

Test("Drops: sorteio em 1/10000 pela ordem da lista e BIT pela faixa de nível", () =>
{
    var list = Drops.FromJson("{\"lists\":{\"x\":[{\"table\":\"armor2\",\"row\":24,\"rank\":0,\"rate\":200},{\"table\":\"weapon\",\"row\":26,\"rank\":1,\"rate\":100},{\"table\":\"insItem\",\"row\":0,\"rank\":0,\"rate\":800}]}}")["x"];
    Equal(24, Drops.Roll(list, new SeqRandom(200))?.Item.Row, "200 cai na primeira (rnd ≤ 0)");
    Equal(26, Drops.Roll(list, new SeqRandom(201))?.Item.Row, "201 cai na segunda");
    Equal(0, Drops.Roll(list, new SeqRandom(1100))?.Item.Row, "1100 cai no BIT");
    Equal(null, Drops.Roll(list, new SeqRandom(1101))?.Item.Table, "1101: nada");
    Equal(26, Drops.Roll(list, new SeqRandom(10250))?.Item.Row, "MT % 10000");
    Equal("weapon", Drops.Roll(list, new SeqRandom(5000, 250), reroll: true)?.Item.Table, "re-sorteio");
    Equal(10 + 7, Drops.Bit(1, 0, new SeqRandom(7)), "nível 1: 10..55");
    Equal((55 + 44) * 150 / 100, Drops.Bit(19, 50, new SeqRandom(44 + 45)), "nível 19 com BITRITSU 50");
    Equal(566, Drops.Bit(999, 0, new SeqRandom(0)), "última faixa");
});

Test("RECALCPLAYER: armaduras e chips somam nas colunas certas; regras de equipar", () =>
{
    static string T(int rows, params (string Name, object[] Cells)[] cols) =>
        $"{{\"rows\":{rows},\"columns\":[{string.Join(",", cols.Select(c => $"{{\"name\":\"{c.Name}\",\"cells\":[{string.Join(",", c.Cells.Select(v => v is string t ? $"\"{t}\"" : v.ToString()))}]}}"))}]}}";
    var db = GameTables.FromJson("{" + string.Join(",",
        "\"shinka\":" + T(1, ("STR_INI", [50]), ("AC_INI", [100]), ("INT_INI", [70]), ("MND_INI", [60]), ("DEX_INI", [40]), ("MHP_INI", [150]), ("MMP_INI", [80]), ("RES_FIRE", [10])),
        "\"weapon\":" + T(2, ("TSUYOSA", [20, 30]), ("DELAY", [22, 90]), ("SKILL", [2, 1]), ("SKILLPOINT", [1, 500]), ("SOUBI", [0b10, 0b10]), ("MEICHURITSU", [88, 70])),
        "\"armor1\":" + T(1, ("MAMORI", [15]), ("MAXHP", [40]), ("BOUGYORITSU", [5]), ("RES_FIRE", [100]), ("LVPOINT", [3]), ("TSUYOSA", [99])),
        "\"armor2\":" + T(1, ("TSUYOSA", [7]), ("BITRITSU", [50]), ("MAXMP", [9])),
        "\"abChip\":" + T(2, ("TSUYOSA", [3, 0]), ("CHOHATURITSU", [0, 12]), ("ZOKUSEI", ["fire", ""]), ("HPDRAIN", [4, 0]))) + "}");
    int[] sk = [1, 1, 1, 1, 1, 1, 1, 1, 1];
    var bare = db.Player(0, 1, 0, sk);
    var full = db.Player(0, 1, new Equipment(0, 0, 0) { WeaponChips = [0], ArmorChips = [1] }, sk);
    Equal(bare.Tsuyosa + 7 + 3, full.Tsuyosa, "TSUYOSA: armadura 2 + chip da arma (a armadura 1 não soma)");
    Equal(bare.Mamori + 15, full.Mamori, "MAMORI: armadura 1");
    Equal(bare.MaxHp + 40, full.MaxHp, "MHP");
    Equal(bare.MaxMp + 9, full.MaxMp, "MMP");
    Equal(5, full.Bougyoritsu, "BOUGYORITSU");
    Equal(50, full.Bitritsu, "BITRITSU");
    Equal(12, full.Kaihiritsu, "KAIHIRITSU do chip da armadura");
    Equal(0, full.AttackElement, "elemento do chip da arma");
    Equal(4, full.HpDrain, "HPDRAIN");
    Equal(110, full.Res(0), "RES fogo: shinka + armadura 1");
    Equal(22, full.AttackDelay, "DELAY");
    Equal(70, db.Player(0, 1, 1, sk).AttackDelay, "DELAY até 70");
    True(db.CanEquipWeapon(0, 1, sk) && !db.CanEquipWeapon(0, 0, sk), "SOUBI pelo bit da Digimon");
    True(!db.CanEquipWeapon(1, 1, sk), "SKILLPOINT 500");
    True(!db.CanEquipArmor("armor1", 0, 2) && db.CanEquipArmor("armor1", 0, 3), "LVPOINT");
});

Test("Evolução: cada forma guarda nível, EXP e skills; só troca para forma liberada", () =>
{
    var ev = new Evolution();
    ev.Start(2);
    ev[2].Level = 20;
    ev[2].Exp = 5000;
    ev[2].Skill[1] = 300;
    Equal(-1L, ev.ExpOf(14), "forma bloqueada");
    True(!ev.Switch(14), "não troca para bloqueada");
    True(ev.Unlock(14) && !ev.Unlock(14), "SETSHINKA só na primeira vez");
    True(ev.Switch(14), "troca");
    Equal(14, ev.Current, "forma atual");
    Equal(1, ev[14].Level, "forma nova no nível 1");
    Equal(1, ev[14].Skill[1], "skills 1");
    True(ev.Switch(2), "volta");
    Equal(20, ev[2].Level, "nível preservado");
    Equal(300, ev[2].Skill[1], "skill preservada");
    Equal(5000L, ev.ExpOf(2), "GETEXP");
});

Test("Grupo: chip de backup na morte (BIT inteiro e 20% da faixa anterior) e EXP por NINZU", () =>
{
    Equal((new BackupChip(120, 0), 0L), Party.Die(1, 0, 120), "nível 1: só o BIT");
    Equal((new BackupChip(50, 68), 432L), Party.Die(2, 500, 50), "nível 2: perde (341 − 0)/5 = 68");
    Equal((new BackupChip(0, 9), 341L), Party.Die(2, 350, 0), "não desce abaixo de NE(1) = 341");
    Equal(14, DamageFormula.ExpShare(14, 0, 1, true), "sozinho");
    Equal((11, 5), (DamageFormula.ExpShare(14, 0, 4, true), DamageFormula.ExpShare(14, 0, 4, false)), "4 jogadores");
    Equal(3, Party.PlayerDataIndex(4), "PlayerData de 4 jogadores");
});

Test("Combate (FIGHTCALC): dano do jogador e do inimigo, acerto, crítico, experiência e MT19937", () =>
{
    var a = new Fighter { Tsuyosa = 100, Kashikosa = 40, Chimeiritsu = 4, Skill = [500, 1500], WeaponSkillNo = 0, AttackElement = 0, HpDrain = 10 };
    var d = new Fighter { Mamori = 50, Seishin = 20, Resist = [200, 500, 0, 0, 0] };
    // (6·100 + 4·500)/50 = 52; 200 < 700 → +52·800/1000 = 93; crítico +55% → 144; k = 3 → 1025, m = 100 → 147
    var rng = new SeqRandom(700, 3, 100);
    var r = DamageFormula.Physical(a, d, critical: true, new StatusMods(), rng);
    Equal(147, r.Damage, "dano físico");
    True(r.Strong && r.Element == 0, "golpe forte e elemento fogo");
    Equal(14, r.HpDrained, "dreno de 10%");
    Equal(3, rng.Calls, "três sorteios");
    var none = new SeqRandom(1);
    Equal(0, DamageFormula.Physical(a, d with { Mamori = 100000 }, true, new StatusMods(), none).Damage, "dano 0");
    Equal(0, none.Calls, "dano 0 não sorteia");
    // (20·30 + 10·40 + 5·1500)/20 = 425; resistência 500 → 212; k = 2 → 1000, m = 0 → 212
    var t = DamageFormula.Technique(a, d, new TechniqueStats(Hp: -30, SkillIndex: 1, Element: 1), critical: false, new SeqRandom(2, 0));
    Equal(212, t.Damage, "dano da técnica");
    Equal(0, DamageFormula.Technique(a, d, new TechniqueStats(0, 1, 1), false, new SeqRandom(0)).Damage, "técnica sem potência");
    True(DamageFormula.PlayerCritical(a, new SeqRandom(203)), "crítico: 4 > 203 % 200");
    Equal(3, DamageFormula.Rank(10, 0, 100), "alvo morto");
    Equal(2, DamageFormula.Rank(51, 10, 100), "acima de 50%");
    Equal(1, DamageFormula.Rank(26, 10, 100), "acima de 25%");
    Equal(0, DamageFormula.Rank(25, 10, 100), "até 25%");

    var nume = new Fighter { Tsuyosa = 16, Kashikosa = 200, Chimeiritsu = 3, Subayasa = 102 };
    var guil = new Fighter { Mamori = 120, Seishin = 100, Subayasa = 60, Bougyoritsu = 10 };
    // físico: 16·100/120 = 13; (97·96 = 9312) > 5000 → sem crítico; k = 2, m = 7 → 13
    Equal(13, DamageFormula.EnemyDamage(nume, new EnemyAttack(0, 100, 70, 4, -1), guil, new StatusMods(), new SeqRandom(5000, 2, 7)).Damage, "inimigo físico");
    // mágico: 200·50/100 = 100; v = 2000 ≤ 9999 → crítico soma (5 + 100 + 90)/100 = 1 (bug do jogo) → 101; k = 0 → 950 → 95
    Equal(95, DamageFormula.EnemyDamage(nume, new EnemyAttack(1, 50, 70, 10, -1), guil, new StatusMods(), new SeqRandom(9999, 0, 0)).Damage, "inimigo mágico");
    True(!DamageFormula.EnemyHits(nume, new EnemyAttack(0, 100, 70, 4, -1), guil, new SeqRandom(5)), "bloqueio: 10 > 5");
    Equal(12, DamageFormula.ExpShare(12, 0, 1, true), "1 jogador: 100%");
    Equal(80, DamageFormula.ExpShare(100, 0, 4, true), "4 jogadores: 80% para quem matou");
    Equal(40, DamageFormula.ExpShare(100, 0, 4, false), "4 jogadores: 40% para os outros");

    // mágico com CHIMEIRITSU 0: sem sorteio de crítico (só k e m)
    var noCrit = new SeqRandom(0, 0);
    DamageFormula.EnemyDamage(nume, new EnemyAttack(1, 50, 70, 0, -1), guil, new StatusMods(), noCrit);
    Equal(2, noCrit.Calls, "c = 0 pula o crítico");
    Equal(-1, DamageFormula.EnemyDamage(nume, new EnemyAttack(3, 50, 70, 0, 1), guil, new StatusMods(), noCrit).Element, "TYPE 3 sem elemento");

    // Crescimento (RECALCPLAYER): Guilmon MHP_INI 150, GROW 90
    Equal(150, PlayerGrowth.MaxHpMp(1, 150, 90), "MHP no nível 1");
    Equal(276, PlayerGrowth.MaxHpMp(10, 150, 90), "MHP no nível 10");
    Equal(PlayerGrowth.MaxHpMp(5, 150, 90), 150 + 90 * 40 / 100, "curva contínua na fronteira 5");
    Equal(55, PlayerGrowth.Stat(1, 55, 0), "atributo no nível 1");
    Equal(341L, PlayerGrowth.NextExp(1), "NEXTEXP 1");
    Equal(968L, PlayerGrowth.NextExp(2), "NEXTEXP 2");
    Equal(17000L, PlayerGrowth.NextExp(10), "NEXTEXP 10");
    Equal(0L, PlayerGrowth.NextExp(999), "NEXTEXP 999");

    // Skill: limite por nível e +1 com taxa sintética
    Equal(1080, SkillGrowth.Limit(36), "limite 36");
    Equal(1100, SkillGrowth.Limit(37), "limite 37");
    Equal(3708, SkillGrowth.Limit(228), "limite 228");
    var rates = new short[4, 21, 3];
    rates[0, 14, 0] = 10; // nível 1–36, faixa "até 500" (primeiro j com sk ≤ T[j]), coluna normal
    var sg = new SkillGrowth(rates);
    Equal(501, sg.Raise(500, 30, technique: true, 0, false, new SeqRandom(999)), "técnica: 10·100 = 1000 > 999");
    Equal(500, sg.Raise(500, 30, technique: false, 100, false, new SeqRandom(500)), "arma: 10·100·500/1000 = 500, não sobe");
    var capped = new SeqRandom(0);
    Equal(30, sg.Raise(30, 1, true, 0, false, capped), "no limite não sobe");
    Equal(0, capped.Calls, "no limite não sorteia");

    // Estados
    Equal(260, StatusFormula.SkillCurve(40, 250, 0, 0), "curva sk/4 + b0");
    var poisoned = StatusFormula.PlayerPoison(new Fighter { Kashikosa = 70 }, 0, new TechniqueEffect(Time: 100), 0, new SeqRandom(749));
    Equal(135, poisoned, "veneno: 750 > 749, duração 70/2 + 100");
    Equal((int?)null, StatusFormula.EnemyPoison(new EnemyEffect(0, "sleep", 1, 60), 0, new SeqRandom(0)), "veneno só com KOUKA doku");
    Equal(20, StatusFormula.PlayerStatDown(-30, 0, 25), "redução (valor da técnica negativo) limitada a 80% do inimigo");

    var mt = new MersenneTwister();
    mt.SeedReference(5489);
    Equal(3499211612u, mt.Next(), "MT19937: primeiro valor de referência");
    True(new MersenneTwister().Next() == new MersenneTwister(MersenneTwister.GameSeed).Next(), "semente do jogo 5150");
});

Test("PowerPC: desmontagem de instruções conhecidas e DOL sintético (seções, r13, string e ponteiro)", () =>
{
    (uint W, uint Pc, string Text)[] cases =
    [
        (0x7C0802A6, 0, "mflr r0"), (0x9421FFF0, 0, "stwu sp, -0x10(sp)"), (0x4E800020, 0, "blr"), (0x38600001, 0, "li r3, 0x1"),
        (0x3C608045, 0, "lis r3, 0x8045"), (0x48000011, 0x80003100, "bl 0x80003110"), (0x4182000C, 0x1000, "beq 0x0000100C"),
        (0xEC21102A, 0, "fadds f1, f1, f2"), (0x7C631A14, 0, "add r3, r3, r3"), (0x5463103A, 0, "slwi r3, r3, 2"),
        (0x60000000, 0, "nop"), (0x7C7F1B78, 0, "mr r31, r3"), (0x4E800421, 0, "bctrl"),
    ];
    foreach (var (w, pc, text) in cases) Equal(text, DW4.Formats.Ppc.PpcDisassembler.Disassemble(w, pc), $"0x{w:X8}");

    var dol = new byte[0x110];
    void Put(int at, uint v) => BinaryPrimitives.WriteUInt32BigEndian(dol.AsSpan(at), v);
    Put(0x00, 0x100); Put(0x48, 0x80003100); Put(0x90, 8);          // text0
    Put(0x1C, 0x108); Put(0x64, 0x80005600); Put(0xAC, 8);          // data0
    Put(0xE0, 0x80003100);
    Put(0x100, 0x3DA08056); Put(0x104, 0x39ADF000);                 // lis r13, 0x8056; addi r13, r13, -0x1000
    "ABC\0"u8.CopyTo(dol.AsSpan(0x108)); Put(0x10C, 0x80005600);
    var d = DW4.Formats.Ppc.DolFile.Read(dol);
    Equal(2, d.Sections.Count, "seções");
    Equal(0x108L, d.OffsetOf(0x80005600), "offset de dados");
    Equal(0x8055F000u, d.SmallDataBases().R13, "r13");
    Equal("0x80005600", string.Join(",", d.FindString("ABC").Select(x => $"0x{x:X8}")), "string");
    Equal("0x80005604", string.Join(",", d.FindPointers(0x80005600).Select(x => $"0x{x:X8}")), "ponteiro");
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

// VM Lua 4.0: programas montados à mão com a codificação de lopcodes.h (OP 6 bits, U = i >> 6, S = U − MAXARG_S, B 9 bits, A a partir do bit 15).
static uint LU(int op, int u) => (uint)(op | (u << 6));
static uint LS(int op, int s) => LU(op, s + ((1 << 26) - 1 >> 1));
static uint LAB(int op, int a, int b) => (uint)(op | (b << 6) | (a << 15));
static LuaClosure LProgram(uint[] code, string?[]? strings = null, LuaProto[]? protos = null, int numParams = 0, bool vararg = false) =>
    new(new LuaProto { Code = code, Strings = strings ?? [], Protos = protos ?? [], NumParams = numParams, IsVararg = vararg, MaxStackSize = 16 }, []);

Test("VM Lua: aritmética, globais, if com JMPGE e for numérico", () =>
{
    var vm = new LuaVM();
    // x = 2 + 3 * 4; return x
    var r = vm.Call(LProgram([LS(6, 2), LS(6, 3), LS(6, 4), LU(26, 0), LU(23, 0), LU(19, 0), LU(12, 0), LU(1, 0), 0], ["x"]));
    Equal(14.0, (double)r[0]!, "2 + 3 * 4");
    // if 3 < 5 then 1 else 2
    r = vm.Call(LProgram([LS(6, 3), LS(6, 5), LS(37, 2), LS(6, 1), LS(42, 1), LS(6, 2), LU(1, 0), 0]));
    Equal(1.0, (double)r[0]!, "if 3 < 5");
    // s = 0; for i = 1, 10 do s = s + i end; return s
    r = vm.Call(LProgram([LS(6, 0), LU(19, 0), LS(6, 1), LS(6, 10), LS(6, 1), LS(44, 5), LU(12, 0), LU(11, 0), LU(23, 0), LU(19, 0), LS(45, -5), LU(12, 0), LU(1, 0), 0], ["s"]));
    Equal(55.0, (double)r[0]!, "soma 1..10");
});

Test("VM Lua: CONCAT com número (%.16g), closure com upvalue, vararg com arg.n", () =>
{
    var vm = new LuaVM();
    var concat = new LuaClosure(new LuaProto { Code = [LU(7, 0), LU(8, 0), LS(6, 2), LU(29, 3), LU(4, 1), LU(31, 0), LU(1, 0), 0], Strings = ["a"], Numbers = [1.5], MaxStackSize = 8 }, []);
    var r = vm.Call(concat);
    Equal("a1.52", (string)r[0]!, "concat");
    Equal(1.0, (double)r[1]!, "not nil = 1");
    // local up = 42; return (function() return %up end)()
    var inner = new LuaProto { Code = [LU(10, 0), LU(1, 0), 0], MaxStackSize = 2 };
    r = vm.Call(LProgram([LS(6, 42), LAB(48, 0, 1), LAB(2, 0, 255), LU(1, 0), 0], protos: [inner]));
    Equal(42.0, (double)r[0]!, "upvalue");
    // function f(...) return arg.n end; return f(7, 8, 9)
    var va = new LuaProto { Code = [LU(11, 0), LU(14, 0), LU(1, 1), 0], Strings = ["n"], IsVararg = true, MaxStackSize = 4 };
    r = vm.Call(LProgram([LAB(48, 0, 0), LU(19, 0), LU(12, 0), LS(6, 7), LS(6, 8), LS(6, 9), LAB(2, 0, 255), LU(1, 0), 0], ["f"], [va]));
    Equal(3.0, (double)r[0]!, "arg.n");
});

Test("VM Lua: be:Metodo(2, 3) pelo PUSHSELF chega ao hospedeiro com o self; SETLIST e getn", () =>
{
    var vm = new LuaVM();
    var be = new LuaTable();
    var called = new List<string>();
    be.Missing = (_, key) => new LuaHostFunction((string)key, a => { called.Add((string)key); return [ReferenceEquals(a[0], be) ? (double)a[1]! + (double)a[2]! : -1.0]; });
    vm.Globals["be"] = be;
    var r = vm.Call(LProgram([LU(12, 0), LU(16, 1), LS(6, 2), LS(6, 3), LAB(2, 0, 1), LU(1, 0), 0], ["be", "Add"]));
    Equal(5.0, (double)r[0]!, "be:Add(2, 3)");
    Equal("Add", called.Single(), "método chamado");
    // t = {10, 20, 30}; return t, getn(t)
    r = vm.Call(LProgram([LU(17, 3), LS(6, 10), LS(6, 20), LS(6, 30), LAB(21, 0, 3), LU(12, 0), LU(11, 0), LAB(2, 1, 1), LU(1, 0), 0], ["getn"]));
    Equal(3.0, (double)r[1]!, "getn");
    Equal(20.0, (double)((LuaTable)r[0]!).RawGet(2.0)!, "t[2]");
    // laço infinito para no limite
    vm.InstructionLimit = 1000;
    bool stopped = false;
    try { vm.Call(LProgram([LS(42, -1), 0])); } catch (LuaException) { stopped = true; }
    True(stopped, "laço infinito não parou");
});

Test("Tabela Lua 4.0: ordem de iteração do ltable.c (hash_s, colisão, rehash) e biblioteca", () =>
{
    // hash_s("a") = 128, "b" = 131, "c" = 130, "d" = 133 (l = 1: h = 1 ^ (32 + 0 + c)). Tamanho 4: a→0, b→3, c→2;
    // "d"→1 ocupa o último livre e força rehash para 8 (a→0, c→2, b→3, d→5). "i" (136) colide com "a" e vai para o nó 7.
    Equal(128u, LuaTable.StringHash("a"), "hash_s(a)");
    var t = new LuaTable();
    foreach (var k in new[] { "a", "b", "c" }) t.Set(k, 1.0);
    Equal("acb", string.Concat(t.Pairs().Select(p => (string)p.Key)), "ordem com 4 nós");
    t.Set("d", 1.0);
    Equal(8, t.Size, "rehash para 8");
    t.Set("i", 1.0);
    Equal("acbdi", string.Concat(t.Pairs().Select(p => (string)p.Key)), "ordem com 8 nós");
    t.Set("c", null);
    Equal("abdi", string.Concat(t.Pairs().Select(p => (string)p.Key)), "nil some da iteração");
    var vm = new LuaVM();
    // tinsert grava t.n sempre; tremove devolve o elemento e ajusta n; sort com a ordem padrão.
    var list = new LuaTable();
    var ins = vm.Globals.RawGet("tinsert");
    vm.Call(ins, list, 5.0); vm.Call(ins, list, 3.0); vm.Call(ins, list, 1.0, 9.0);
    Equal(3.0, (double)list.RawGet("n")!, "n depois de 3 tinsert");
    Equal(9.0, (double)list.RawGet(1.0)!, "tinsert na posição 1");
    vm.Call(vm.Globals.RawGet("sort"), list);
    Equal("3,5,9", string.Join(",", Enumerable.Range(1, 3).Select(i => (double)list.RawGet((double)i)!)), "sort");
    Equal(9.0, (double)vm.Call(vm.Globals.RawGet("tremove"), list)[0]!, "tremove");
    Equal(2.0, (double)list.RawGet("n")!, "n depois do tremove");
    string F(string fmt, params object?[] a) => (string)vm.Call(vm.Globals.RawGet("format"), [fmt, .. a])[0]!;
    Equal("007|  3.50|0.1|1e+20|ff|x  |abc", F("%03d|%6.2f|%g|%g|%x|%-3s|%s", 7.0, 3.5, 0.1, 1e20, 255.0, "x", "abc"), "format");
    Equal("lo", (string)vm.Call(vm.Globals.RawGet("strsub"), "hello", -2.0)[0]!, "strsub negativo");
    Equal("0.1", (string)vm.Call(vm.Globals.RawGet("tostring"), 0.1)[0]!, "tostring %.16g");
});

Test("API be:*: sd com atraso em quadros, diretor de demo com espera por condição, flags e argumentos tipados", () =>
{
    var engine = new RecordingEngine();
    var host = new BeHost(engine, new GameRam(), new BeSystem());
    var vm = new LuaVM();
    vm.Globals["be"] = host.Table;
    object?[] Be(string name, params object?[] args) => vm.Call(host.Table.Get(name), [host.Table, .. args]);
    Be("sd", "SOUND", null, -1.0, "PLAY", "AM_DUNGE");
    Be("sd", "PLAYER", null, -1.0, "SQ_DEMOWALK", 2.0);
    Equal("SOUND - -1 PLAY \"AM_DUNGE\"", engine.Log.Single(), "sd sem atraso sai na hora");
    host.Tick();
    Equal(1, engine.Log.Count, "atraso de 2 quadros ainda não venceu");
    host.Tick();
    Equal("PLAYER - -1 SQ_DEMOWALK", engine.Log[1], "entregue no 2º quadro");
    Be("DMsetSD", "MODELCTRL", "i_stpnl00", 0.0, "SQ_OPEN");
    Be("DMchkSD", "MODELCTRL", "i_stpnl00", 0.0, "ISSEQNAME", "SQ_OPEN", "!=0");
    Be("DMsetSD", "SOUND", null, -1.0, "BGMSTOP");
    Equal(2, engine.Log.Count, "DMsetSD só enfileira");
    Equal(3.0, (double)Be("DMgetCount")[0]!, "DMgetCount");
    Be("DMrun");
    engine.Answer = 1;
    host.Tick();
    Equal("MODELCTRL i_stpnl00 0 SQ_OPEN", engine.Log[2], "set do DM entregue");
    Equal(3, engine.Log.Count, "chk \"!=0\" segura a fila enquanto vale 1");
    engine.Answer = 0;
    host.Tick();
    Equal("SOUND - -1 BGMSTOP", engine.Log[3], "fila segue quando a condição deixa de valer");
    Be("FLGset", 107.0, 1.0);
    Equal(1.0, (double)Be("FLGisOn", 107.0)[0]!, "FLGisOn devolve 1");
    Equal(0.0, (double)Be("FLGisOn", 108.0)[0]!, "FLGisOn devolve 0, não nil");
    Be("COUNTERset", 2.0, 101.0);
    Equal(101.0, (double)Be("COUNTERget", 2.0)[0]!, "COUNTER");
    var args = BeArg.Parse("V<220,40,-200>/V<0,-90,0>/ALL");
    Equal('V', args[1].Kind, "token tipado");
    Equal(-90.0, args[1].Numbers[1], "rotação");
    True(args[2].IsText && args[2].Text == "ALL", "texto");
});

Test("Objetos: ciclo do piso de dano (rótulos, espera, HITBOX, JUMP)", () =>
{
    static SeqCommand C(string k, params object[] v) => new(k, v);
    // Forma do g_ddamagefloor (igb-seq #179), com os mesmos comandos e valores: as esperas ficam dentro das ações.
    var seqs = new Dictionary<string, List<SeqCommand>>
    {
        ["SQ_WAIT"] =
        [
            C("LABEL", "OFF"), C("ACTION", "WAIT01"), C("WCHECK", "Action"), C("JUMP", "SQ_INTRPT"),
            C("LABEL", "ON"), C("ACTION", "WIT2ON"), C("WCHECK", "Action"),
            C("HITBOX", "Set", 70112, 0, 120, new[] { 0f, 0f, 0f }, new[] { 17f, 1f, 17f }),
            C("ACTION", "ONSW01"), C("WCHECK", "Action"), C("JUMP", "SQ_INTVL"),
        ],
        ["SQ_INTRPT"] = [C("ACTION", "WAIT01"), C("WCHECK", "Action"), C("JUMP", "SQ_WAIT")],
        ["SQ_INTVL"] = [C("ACTION", "WAIT02"), C("WCHECK", "Action"), C("JUMP", "SQ_WAIT")],
        ["SQ_SWOF"] = [C("LABELCTRL", "OFF", "SQ_WAIT"), C("JUMP", "SQ_WAIT")],
    };
    var actions = new Dictionary<string, List<SeqCommand>>
    {
        ["WAIT01"] = [C("JOINTSET", "", 1, 1, "OS"), C("WAITCHECK", "Joint", 0)],
        ["WIT2ON"] = [C("JOINTSET", "", 40, 70, "LP"), C("WAITCHECK", "Frame", 30)],
        ["ONSW01"] = [C("JOINTSET", "", 80, 140, "LP"), C("WAITCHECK", "Frame", 120)],
        ["WAIT02"] = [C("JOINTSET", "", 1, 1, "OS"), C("WAITCHECK", "Frame", 180)],
    };
    var q = new ObjectSequencer(seqs, actions);
    q.Start("SQ_WAIT");
    Equal("WAIT01", q.Action, "sem rótulo: primeiro bloco");
    Equal(false, q.Running, "OFF termina no fim do bloco");
    q.SetLabel("SQ_WAIT", "ON");
    q.Start("SQ_WAIT");
    Equal("WIT2ON", q.Action, "ON");
    Equal(0, q.ActiveHitBoxes.Count(), "caixa só depois da espera");
    for (int i = 0; i < 30; i++) q.Tick(1 / 60f);
    Equal("ONSW01", q.Action, "depois de 30 quadros");
    Equal(1, q.ActiveHitBoxes.Count(), "HITBOX ligada");
    Equal(70112, q.ActiveHitBoxes.First().Type, "tipo da caixa");
    int serial = q.BoxSerial;
    for (int i = 0; i < 120; i++) q.Tick(1 / 60f);
    Equal("WAIT02", q.Action, "JUMP para SQ_INTVL");
    Equal(0, q.ActiveHitBoxes.Count(), "caixa dura 120 quadros");
    for (int i = 0; i < 180; i++) q.Tick(1 / 60f);
    Equal("WIT2ON", q.Action, "volta a SQ_WAIT no rótulo ON");
    for (int i = 0; i < 30; i++) q.Tick(1 / 60f);
    True(q.BoxSerial > serial, "caixa nova a cada ciclo");
    q.Start("SQ_SWOF");
    Equal("OFF", q.Label("SQ_WAIT"), "LABELCTRL troca o rótulo de SQ_WAIT");
});

Test("Câmera de demo: leitura do JSON e amostragem", () =>
{
    var json = """
        {"name":"entry","duration":0.05,"rate":60,"fovHorizontalDeg":54.43,
         "frames":[{"t":0,"position":[0,0,0],"forward":[0,0,1],"up":[0,1,0]},
                   {"t":0.05,"position":[10,0,0],"forward":[0,0,1],"up":[0,1,0]}]}
        """;
    var d = CameraDemo.FromJson(json);
    Equal("entry", d.Name, "nome");
    Equal(2, d.Frames.Count, "quadros");
    True(MathF.Abs(d.Sample(0.025f).Position.X - 5) < 1e-3f, "meio do caminho");
    Equal(10f, d.Sample(1f).Position.X, "presa no fim");
    Equal(0f, d.Sample(-1f).Position.X, "presa no início");
});

Test("be: DMswtSD/DMcase com padrão (−1) e DMswtEnd", () =>
{
    foreach (var (answer, expected) in new[] { (0.0, "A,FIM"), (1.0, "B,FIM"), (7.0, "PADRAO,FIM") })
    {
        var log = new List<string>();
        var dm = new DemoManager(new BeMessenger(m => log.Add(m.Command)), _ => answer);
        BeMessage M(string c) => new("X", null, -1, c, [], null);
        dm.Add(new(DemoManager.Kind.Switch, M("GETSELECT"), 0));
        dm.Add(new(DemoManager.Kind.Case, null, 0, 0)); dm.Add(new(DemoManager.Kind.Set, M("A"), 0));
        dm.Add(new(DemoManager.Kind.Case, null, 0, 1)); dm.Add(new(DemoManager.Kind.Set, M("B"), 0));
        dm.Add(new(DemoManager.Kind.Case, null, 0, -1)); dm.Add(new(DemoManager.Kind.Set, M("PADRAO"), 0));
        dm.Add(new(DemoManager.Kind.SwitchEnd, null, 0)); dm.Add(new(DemoManager.Kind.Set, M("FIM"), 0));
        dm.Run();
        dm.Tick();
        Equal(expected, string.Join(",", log), $"GETSELECT = {answer}");
    }
});

Test("be: DMsetSD SETDEMOCAMERA com atraso segura o chk ISDEMOCAMERA até a demo chegar (tutorial, gene00.lua)", () =>
{
    var log = new List<string>();
    var messenger = new BeMessenger(m => log.Add(m.Command));
    var dm = new DemoManager(messenger, _ => null); // sem demo exportada: a consulta não responde
    BeMessage C(string c, string? arg = null) => new("CAMERA", null, -1, c, [], arg);
    dm.Add(new(DemoManager.Kind.Set, C("SETDEMOCAMERA", "stageend"), 220));
    dm.Add(new(DemoManager.Kind.CheckZero, C("ISDEMOCAMERA", "==0"), 0));
    dm.Add(new(DemoManager.Kind.Set, C("SETBOXCAMERA"), 0));
    dm.Run();
    for (int i = 0; i < 100; i++) { dm.Tick(); messenger.Tick(); }
    True(!log.Contains("SETBOXCAMERA"), "a fila espera a demo: " + string.Join(",", log));
    for (int i = 0; i < 200; i++) { dm.Tick(); messenger.Tick(); }
    Equal("SETDEMOCAMERA,SETBOXCAMERA", string.Join(",", log), "a demo chega antes do SETBOXCAMERA");
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

    Test("Jogo: VM Lua executa o corpo principal dos 2902 chunks distintos com be:* simulado, sem erro interno da VM", () =>
    {
        var seen = new HashSet<string>();
        int run = 0, ok = 0;
        var errors = new Dictionary<string, int>();
        var methods = new Dictionary<string, int>();
        var missing = new Dictionary<string, int>();
        void Count(Dictionary<string, int> d, string k) => d[k] = d.GetValueOrDefault(k) + 1;
        using var afs = AfsArchive.Open(ndmwPath);
        foreach (var e in afs.Entries)
        {
            if (Classifier.Classify(afs.ReadHead(e, 0x30)) != ContentKind.Igb) continue;
            using var ms = new MemoryStream();
            afs.CopyEntry(e, ms);
            var data = ms.ToArray();
            var f = IgbFile.Read(data);
            bool le = f.Header.LittleEndian;
            foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beLuaDataObject"))
                foreach (var v in o.Fields.Where(v => v.Desc.ShortName == "MemoryRef"))
                {
                    int r = v.AsInt32(le);
                    if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb || mb.Size == 0) continue;
                    var mem = data.AsSpan((int)mb.FileOffset, mb.Size).ToArray();
                    if (!Lua40Chunk.LooksLikeChunk(mem) || !seen.Add(Convert.ToHexString(SHA256.HashData(mem)))) continue;
                    var vm = new LuaVM { InstructionLimit = 2_000_000 };
                    var be = new LuaTable();
                    be.Missing = (_, key) => key is string name
                        ? new LuaHostFunction(name, _ => { Count(methods, name); return []; })
                        : null;
                    vm.Globals["be"] = be;
                    vm.Globals.Missing = (_, key) => { if (key is string gname) Count(missing, gname); return null; };
                    run++;
                    try { vm.DoChunk(mem); ok++; }
                    catch (LuaException ex) { Count(errors, ex.Message); }
                }
        }
        Console.WriteLine($"       {run} chunks executados, {ok} sem erro; {methods.Count} métodos be:* chamados no corpo principal");
        foreach (var (msg, n) in errors.OrderByDescending(x => x.Value).Take(8)) Console.WriteLine($"       erro {n}×: {msg}");
        Console.WriteLine($"       globais lidas sem definição: {string.Join(", ", missing.OrderByDescending(x => x.Value).Take(15).Select(x => $"{x.Key} ({x.Value})"))}");
        Equal(2902, run, "chunks distintos");
    });

    Test("Jogo: voltar à dng1 com a onda GENE-03 vencida (FLG 102) reabre a porta 0 sem animação e tira a parede", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[179], ms);
        var data = ms.ToArray();
        var engine = new RecordingEngine();
        var ram = new GameRam();
        ram.SetFlag(102, true); // o gene03.lua faz FLGset(102, 1) ao vencer; a GameRam sobrevive à troca de fase no cliente
        // FATO (start.lua #179): se COUNTER 2 != 101, a área mudou: COUNTERset(2, 101) e FLGset(100..299, 0). A dng101b2 não mexe
        // no COUNTER 2, então na volta ele ainda é 101 (da primeira entrada) e as FLGs da área ficam.
        ram.SetCounter(2, 101);
        var host = new BeHost(engine, ram, new BeSystem { FolderName = "dng101b1" });
        var stage = new StageScripts(IgbLua.Chunks(IgbFile.Read(data), data), host);
        stage.Boot();
        stage.Start();
        for (int i = 0; i < 300; i++) stage.Tick();
        True(engine.Log.Contains("MODELCTRL g_d1door 0 SQ_SILENTON"), "porta 0 aberta sem animação: " + string.Join(" | ", engine.Log.Where(l => l.Contains("g_d1door") || l.Contains("doorwall"))));
        True(engine.Log.Contains("HITLANDMODEL doorwall01 0 OFFHIT"), "parede da porta 0 sem colisão");
        True(!engine.Log.Contains("MODELCTRL g_d1door 1 SQ_SILENTON"), "porta 1 continua fechada (FLG 106 desligada)");
    });

    Test("Jogo: txt_status.lua dá os índices de texto do menu de status por fase (dng1 = 11..28; stag0101 diferente)", () =>
    {
        List<int> Indices(int entry, int callNo)
        {
            using var afs = AfsArchive.Open(ndmwPath);
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[entry], ms);
            var data = ms.ToArray();
            var stage = new StageScripts(IgbLua.Chunks(IgbFile.Read(data), data), new BeHost(new RecordingEngine(), new GameRam(), new BeSystem()));
            return stage.RunText("txt_status.lua", callNo)
                .Select(m => m.Args.LastOrDefault() is { Kind: 'I', Numbers.Length: > 0 } k ? (int)k.Numbers[0] : -1).ToList();
        }
        Equal("11,12,13", string.Join(",", Indices(179, 0)), "dng1 ajuda (callNo 0)");
        Equal(string.Join(",", Enumerable.Range(14, 12)), string.Join(",", Indices(179, 3)), "dng1 atributos (callNo 3)");
        Equal("26,27,28", string.Join(",", Indices(179, 15)), "dng1 menu (callNo 15)");
        True(string.Join(",", Indices(614, 3)) != string.Join(",", Indices(179, 3)), "stag0101 tem outra ordem");
    });

    Test("Jogo: scripts da dng1 (#179) na ordem do motor: lista de arquivos, área 101, objetos gerados e músicas da entrada", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[179], ms);
        var data = ms.ToArray();
        var engine = new RecordingEngine();
        var sys = new BeSystem { FolderName = "dng101b1" };
        sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
        var host = new BeHost(engine, new GameRam(), sys);
        var unimpl = new List<string>();
        host.Unimplemented = (n, _) => unimpl.Add(n);
        var stage = new StageScripts(IgbLua.Chunks(IgbFile.Read(data), data), host);
        var errors = new List<string>();
        stage.Error = (n, m) => errors.Add($"{n}: {m}");
        var files = stage.Boot();
        True(files.Contains("dng101b1/separate/generatorN.igb"), "generatorN na lista");
        True(files.Contains("_common/p_girumon.igb"), "modelo do jogador na lista");
        stage.Start();
        for (int i = 0; i < 600; i++) stage.Tick();
        Equal(0, errors.Count, $"erros ({string.Join("; ", errors)})");
        Equal(101, host.Ram.Counter(2), "COUNTER 2 = área 101");
        True(engine.Log.Contains("PLAYER p_girumon 0 GENERATE"), "jogador gerado");
        Equal(3, engine.Log.Count(l => l.StartsWith("MODELCTRL g_d1door ", StringComparison.Ordinal) && l.Contains("GENERATE")), "3 portas");
        True(engine.Log.Any(l => l.StartsWith("SOUND ", StringComparison.Ordinal) && l.Contains("PLAY \"AM_DUNGE\"")), "AM_DUNGE");
        True(engine.Log.Any(l => l.Contains("PLAY \"DG_CAVE1\"")), "DG_CAVE1");
        Console.WriteLine($"       {engine.Log.Count} comandos; métodos ainda não implementados: {string.Join(", ", unimpl.Distinct())}");
    });

    Test("Jogo: lobby stagml11 (#821): func/boot/start/first, quest01 e callshop rodam sem erro e sem os 17 métodos faltando", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[821], ms);
        var data = ms.ToArray();
        var engine = new RecordingEngine();
        var sys = new BeSystem { FolderName = "stagml11" };
        sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
        var ram = new GameRam();
        ram.Players[0].Forms.Start(0);
        ram.Players[0].Level = () => 20;
        var host = new BeHost(engine, ram, sys);
        var unimpl = new List<string>();
        host.Unimplemented = (n, _) => unimpl.Add(n);
        var stage = new StageScripts(IgbLua.Chunks(IgbFile.Read(data), data), host);
        var errors = new List<string>();
        stage.Error = (n, m) => errors.Add($"{n}: {m}");
        stage.Boot();
        stage.Start();
        for (int i = 0; i < 600; i++) stage.Tick();
        foreach (var s in new[] { "quest01.lua", "callshop.lua" }) if (stage.Has(s)) stage.Run(s); else errors.Add($"{s} não existe");
        for (int i = 0; i < 600; i++) stage.Tick();
        Equal(0, errors.Count, $"erros ({string.Join("; ", errors)})");
        string[] lobby = ["MATgetPlayerMatStat", "MDLgetPosStr", "MDLgetRotStr", "MDLisCallName", "MDLsetWorldPosTarget", "MsgBox", "SYSctlBit",
            "SYSctlCardFolder", "SYSctlDeviceFolder", "SYSctlFlagFolder", "SYSctlShinka", "SYSgetArea", "SYSgetGateStageName", "SYSgetGateStageNo",
            "SYSgetPlayerLevel", "SYSisGetShinkaDigimon", "SYSsetGateStage"];
        Equal("", string.Join(", ", unimpl.Where(u => lobby.Any(l => u.StartsWith(l, StringComparison.Ordinal)))), "nenhum dos 17 sem resposta");
        // FATO (start.lua): sem portão gravado, SYSsetGateStage("GateOff", −1).
        Equal(("GateOff", -1), (ram.GateStageName, ram.GateStageNo), "portão desligado no começo");
        // FATO (start.lua): nível ≥ 16 liga a FLG 908; 46 e 76 ligam 909 e 910.
        Equal((1, 0, 0), (ram.FlagOn(908), ram.FlagOn(909), ram.FlagOn(910)), "FLG 908..910 pelo nível 20");
        True(engine.Log.Any(l => l.StartsWith("MODELCTRL g_cyheal 0 GENERATE", StringComparison.Ordinal)), "g_cyheal gerado");
        Console.WriteLine($"       {engine.Log.Count} comandos; ainda sem resposta: {string.Join(", ", unimpl.Distinct())}");

        // Chamadas diretas (FATO pelos nativos lidos).
        object?[] Be(string m, params object?[] args) => ((LuaHostFunction)host.Table.Get(m)!).Body([host.Table, .. args]);
        Equal(1.0, Be("SYSgetArea")[0], "SYSgetArea = 1");
        Equal(1.0, Be("SYSctlCardFolder", "SET", 3.0)[0], "carta 3 marcada");
        Equal(0.0, Be("SYSctlCardFolder", "SET", 3.0)[0], "já tinha");
        Equal(1.0, Be("SYSctlCardFolder", "KAZU", 0.0)[0], "1 carta");
        Equal(-1.0, Be("SYSctlShinka", "GETEXP", 0.0, 5.0)[0], "forma 5 não obtida: −1");
        Equal(1.0, Be("SYSctlShinka", "SETSHINKA", 0.0, 5.0)[0], "libera a forma 5");
        Equal(1.0, Be("SYSisGetShinkaDigimon", 0.0, 5.0)[0], "forma 5 obtida");
        Equal(0.0, Be("SYSctlShinka", "SETSHINKA", 0.0, 5.0)[0], "já liberada");
        int bit = ram.Players[0].Items.Bit;
        Be("SYSctlBit", "ADD", 0.0, 2000.0);
        Equal(bit + 2000, ram.Players[0].Items.Bit, "BIT +2000");
        Equal(1.0, Be("SYSctlDeviceFolder", "SET", 0.0, "weapon", 35.0, 0.0, 0.0)[0], "arma guardada");
        Equal(1.0, Be("SYSctlDeviceFolder", "IS", 0.0, "weapon", 35.0, 0.0, 0.0)[0], "tem a arma");
        Equal(1.0, Be("SYSctlFlagFolder", "SET", 11.0)[0], "impItem 11");
        Equal(1.0, Be("SYSctlFlagFolder", "CLR", 11.0)[0], "impItem 11 tirado");
        Equal(0.0, Be("SYSctlFlagFolder", "IS", 11.0)[0], "sem o impItem 11");
        var npc = new TestModel { Position = (10, 0, 20), Yaw = Math.PI / 2 };
        host.Model = npc;
        Equal("V<15.000000,2.000000,20.000000>", Be("MDLgetPosStr", 0.0, 2.0, 5.0)[0], "5 à frente de quem olha para +X");
        Equal("V<0.000000,90.000000,0.000000>", Be("MDLgetRotStr", 0.0, 0.0, 0.0)[0], "giro 90°");
        host.Model = null;

        // Cura (mate03.lua → funcHealGate do func.lua): entrou no volume com HP 100/150 → GAMERAM ADDHP até o máximo.
        engine.Answers["GETHP"] = 100; engine.Answers["GETMHP"] = 150; engine.Answers["GETMP"] = 80; engine.Answers["GETMMP"] = 80;
        engine.Log.Clear();
        True(stage.RunMat("mate03.lua", new BeMatEvent(true, true, 0, _ => { })), "mate03.lua rodou");
        for (int i = 0; i < 120; i++) stage.Tick();
        True(engine.Log.Any(l => l.StartsWith("GAMERAM - 0 ADDHP \"I<0>/I<150>", StringComparison.Ordinal)), "ADDHP: " + string.Join(" | ", engine.Log.Take(12)));
        True(engine.Log.Contains("PLAYER - 0 ONCTRL"), "devolve o controle");

        // Teleporte (mate00.lua): os destinos crescem com o enredo (FLG 2 → 1 campo, 4 → 2, 5 → 3, 6/86 → 4; 409 → destino da
        // missão). Com a FLG 4: stag0101 e stag0201; escolher o 2º leva ao stag0201.
        int t = 0;
        engine.Responder = m => (m.Name, m.Command) switch
        {
            ("i_zzstchgcur", "ISSELECTNOW") => t < 20 ? 1 : 0,
            ("i_zzstchgcur", "ISEND") => t < 20 ? 0 : 1,
            ("i_zzstchgcur", "GETSELECT") => 1,
            _ => null,
        };
        // FATO (mate00.lua): com a FLG 2 o teleporte só oferece o primeiro campo; com a 409 (ligada pelo quest01 acima), só o
        // destino da missão (stag0401, entrada 3).
        ram.SetFlag(2, false);
        ram.SetFlag(409, false);
        ram.SetFlag(4, true);
        engine.Log.Clear();
        True(stage.RunMat("mate00.lua", new BeMatEvent(true, true, 0, _ => { })), "mate00.lua rodou");
        for (; t < 600; t++) stage.Tick();
        True(engine.Log.Any(l => l == "SELECTCTRL i_zzstchgcur 0 GENERATE \"I<2>\""), "menu com 2 opções: "  + string.Join(" | ", engine.Log.Where(l => l.Contains("zzstchgcur"))));
        Equal(2, engine.Log.Count(l => l.StartsWith("TEXTCTL - -1 TEXTSET \"STCHGFONT01/font0", StringComparison.Ordinal)), "2 textos de destino");
        True(engine.Log.Contains("STAGECTL - -1 NEXTSTAGE \"stag0201\""), "2ª opção → stag0201: " + string.Join(" | ", engine.Log.TakeLast(15)));

        // Desistir (DMcase 4): nada de troca de fase, o controle volta.
        t = 0;
        engine.Responder = m => (m.Name, m.Command) switch
        {
            ("i_zzstchgcur", "ISSELECTNOW") => t < 20 ? 1 : 0,
            ("i_zzstchgcur", "ISEND") => t < 20 ? 0 : 1,
            ("i_zzstchgcur", "GETSELECT") => 4,
            _ => null,
        };
        engine.Log.Clear();
        stage.RunMat("mate00.lua", new BeMatEvent(true, true, 0, _ => { }));
        for (; t < 600; t++) stage.Tick();
        True(!engine.Log.Any(l => l.Contains("NEXTSTAGE")), "desistiu: sem troca");
        True(engine.Log.Any(l => l.Contains("ONCTRL")), "desistiu: controle volta");

        // Portão de volta (mate02.lua): com o portão gravado na dng101b2 (entrada 2), a janela sim/não leva até lá.
        Be("SYSsetGateStage", "dng101b2", 2.0);
        t = 0;
        engine.Responder = m => (m.Name, m.Command) switch
        {
            ("i_zzstchgwn", "ISSELECTNOW") => t < 20 ? 1 : 0,
            (_, "ISEND") => t < 20 ? 0 : 1,
            ("i_zzstchgwn", "GETSELECT") => 0,
            ("i_zzstchgwb", "ISRUN") => t < 40 ? 1 : 0, // como o cliente: aberta até a escolha
            _ => null,
        };
        engine.Log.Clear();
        True(stage.RunMat("mate02.lua", new BeMatEvent(true, true, 0, _ => { })), "mate02.lua rodou");
        for (; t < 600; t++) stage.Tick();
        True(engine.Log.Contains("TEXTCTL - -1 TEXTSET \"STCHGFONT00/font00/I<60>\""), "nome da dng101b2 na janela");
        True(engine.Log.Contains("STAGECTL - -1 NEXTSTAGE \"dng101b2\"") && engine.Log.Contains("PLAYERPOINT - -1 SETGENERATEPOS \"I<2>\""),
            "volta à dng101b2, entrada 2: " + string.Join(" | ", engine.Log.Take(40)));
        engine.Responder = null;

        // Métodos da varredura (FATO pelos nativos).
        host.Mat = new BeMatEvent(true, true, 0, _ => { });
        Equal((1.0, 0.0), ((double)Be("MATisRidePlayer", 0.0)[0]!, (double)Be("MATisRideOffPlayer", 0.0)[0]!), "entrou: Ride 1, RideOff 0");
        host.Mat = null;
        host.Model = new TestModel { Position = (3, 0, 4) };
        Equal(25.0, Be("MDLgetKyoriSquare", 0.0, 0.0, 0.0)[0], "distância ao quadrado");
        host.Model = null;
        Equal(1.0, Be("SYSctlDiskFolder", "SET", 0.0, 11.0, 2.0)[0], "2 discos 11");
        Equal(2, ram.Players[0].Items.Disks(11), "contagem");
        Equal(1.0, Be("SYSctlDiskFolder", "CLR", 0.0, 11.0, 9.0)[0], "tira os discos 11");
        Equal(0, ram.Players[0].Items.Disks(11), "sem disco 11");
        host.CallName = "WPos";
        Equal(1.0, Be("MDLisCallName", "WPos")[0], "chamada WPos");
        Be("MDLsetWorldPosTarget", "V<1,2,3>");
        Equal((1.0, 2.0, 3.0), host.WorldPosTarget, "alvo da mira");
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

    Test("Jogo: abertura AUS_ (#2) decodifica os 1920 quadros do MPEG-1 sem erro", () =>
    {
        using var afs = AfsArchive.Open(areaPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[2], ms);
        var (streams, _, _) = MpegPs.Demux(ms.ToArray());
        var dec = new Mpeg1Video(streams[0xE0].Payload.ToArray());
        var counts = new Dictionary<char, int>();
        string hash300 = "";
        int frames = 0;
        while (dec.Next() is { } fr)
        {
            Equal(frames++, fr.Index, "índice de exibição");
            counts[fr.Type] = counts.GetValueOrDefault(fr.Type) + 1;
            if (fr.Index == 300) hash300 = Convert.ToHexString(SHA256.HashData(fr.Y));
        }
        Equal(0, dec.Problems.Count, string.Join("; ", dec.Problems.Take(3)));
        Equal(1920, frames, "quadros (64,06 s a 29,97 fps, a duração do ADX)");
        Equal((139, 524, 1257), (counts['I'], counts['P'], counts['B']), "imagens I, P e B");
        Equal("3B79BFC3847222CE65EF25CC4326454EA4D9FF0D08686BCBB0162DEBBEB0B7BE", hash300, "SHA-256 da luma do quadro 300 (conferido a olho em 2026-10-30)");
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

    Test("Jogo: SQ_JUMP do Guilmon (slash): JUMPS1 sobe até virar, JUMPS2 cai até pousar, JUMPS3 e volta ao SQ_WAIT", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[857], ms);
        var data = ms.ToArray();
        var seqs = IgbSequences.Read(IgbFile.Read(data), data, null, new List<string>());
        var game = seqs.ToDictionary(k => k.Key, k => k.Value.Select(c => new SeqCommand(c.Kind, c.Values)).ToList());
        var jump = new JumpMotion();
        float y = 0, peak = 0;
        var r = new SequenceRunner(game, _ => 6f) { Condition = w => w switch { "TurnY" => jump.VelocityY <= 0, "Landing" => y <= 0 && jump.VelocityY <= 0, _ => true }, MoveStarted = m => jump.Apply(m, jump.VelocityY) };
        True(r.Start("SQ_JUMP", "slash"), "bloco slash");
        Equal("Jump", r.Move?.S(0), "MOVE Jump");
        Equal((1.44f, 6.3f), (r.Move!.F(1), r.Move.F(2)), "argumentos do Jump");
        True(Math.Abs(JumpMotion.PeakHeight(r.Move) - 14.29f) < 0.01f, "pico ≈ 14,3 (passa o degrau de 10 do pedestal)");
        const float frame = 1 / SequenceRunner.FramesPerSecond;
        var actions = new List<string>();
        for (int i = 0; i < 300 && r.Running; i++)
        {
            if (r.Action is { } a && (actions.Count == 0 || actions[^1] != a)) actions.Add(a);
            y = Math.Max(0, y + jump.Step(frame) * frame);
            peak = Math.Max(peak, y);
            r.Tick(frame, false);
        }
        Equal("JUMPS1,JUMPS2,JUMPS3", string.Join(",", actions), "ações");
        True(!r.Running && r.Next == "SQ_WAIT", "termina no SQ_WAIT");
        True(!jump.Active, "MOVE Stop no pouso desliga o pulo");
        True(peak > 10 && peak < 14.5f, $"altura alcançada {peak:0.0}");
        y = 20; // no ar: o SQ_FALL para no "Landing"
        True(r.Start("SQ_FALL", "slash") && r.Move?.S(0) == "Fall" && jump.Active, "SQ_FALL começa pelo Fall e espera o pouso");
    });

    Test("Jogo: MOVEEX/MOVE pelos canais do executor (0x8030A1F4/0x8030A2D8): texto → canal e modo, acelera, freia, troca de sequência", () =>
    {
        Equal((1, 0), MotionChannels.ParseEx("TYPACC")!.Value, "TYPACC = Y, acelera +");
        Equal((0, 1), MotionChannels.ParseEx("TXMACC")!.Value, "TXMACC = X, acelera −");
        Equal((2, 5), MotionChannels.ParseEx("TZMTGD")!.Value, "TZMTGD = Z, começa em −máx e freia");
        Equal((3, 0), MotionChannels.ParseEx("RYPACC")!.Value, "RYPACC = giro Y");
        var m = new MotionChannels();
        // f_luce SQ_ATCKON: MOVEEX "TYPACC" 1.8 6 120 True 1 4 → 0,1/quadro² até 1,8; aos 120 quadros freia com 1/(0,5·16).
        True(m.Apply(new SeqCommand("MOVEEX", ["TYPACC", 1.8f, 6, 120, true, 1, 4])), "aplica");
        float y = 0;
        for (int i = 0; i < 18; i++) y += m.Step(1).Local.Y;
        True(Math.Abs(m[1].Velocity - 1.8f) < 1e-4f, $"chega a 1,8 em 18 quadros ({m[1].Velocity})");
        for (int i = 18; i < 120; i++) y += m.Step(1).Local.Y;
        // FATO do código: o tempo só vence quando fica abaixo de 0; caindo em 0 exato ele vira "sem fim" (120 − 120 passos de 1).
        y += m.Step(1).Local.Y;
        True(m[1].Timer == 0 && m[1].Mode == 0, "tempo zerado exato não troca de fase");
        m.SequenceChanged();
        Equal(2, m[1].Mode, "o bool: a troca de sequência passa ao freio (modo 2)");
        True(Math.Abs(m[1].Accel - 0.125f) < 1e-5f, "freio 1/(0,5·4²)");
        for (int i = 0; i < 20; i++) y += m.Step(1).Local.Y;
        True(!m[1].Active && m[1].Velocity == 0, "parou no 0");
        True(y > 200 && y < 230, $"subiu {y:0.0}");
        m.Apply(new SeqCommand("MOVEEX", ["TXMACC", 1f, 6, 10, false, 1, 4]));
        for (int i = 0; i < 4; i++) m.Step(3);
        Equal(3, m[0].Mode, "tempo 10 passa de 0 no 4º passo de 3: o modo 1 vai ao 3");
        m.Stop();
        // O bool: troca de sequência antecipa o freio; sem ele, segue. O giro sempre para.
        m.Apply(new SeqCommand("MOVEEX", ["TXPACC", 0.7f, 5, 0, true, 5, 0.1f]));
        m.Apply(new SeqCommand("MOVEEX", ["TZPACC", 0.3f, 6, 0, false, 5, 1]));
        m.Apply(new SeqCommand("MOVEEX", ["RYPACC", 2f, 6, 0, false, 1, 4]));
        m.Step(1);
        m.SequenceChanged();
        True(m[0].Active && m[0].Mode == 2, "X com o bool passa ao freio");
        True(m[2].Active && m[2].Mode == 0, "Z sem o bool continua acelerando");
        True(!m[MotionChannels.RotateY].Active, "giro para");
        // MOVE "Push" 1,5 25 (projétil): começa em 1,5 e freia até parar.
        var p = new MotionChannels();
        p.Apply(new SeqCommand("MOVE", ["Push", 1.5f, 25]));
        float z = 0;
        for (int i = 0; i < 400 && p.AnyActive; i++) z += p.Step(1).Local.Z;
        True(!p.AnyActive && z > 230 && z < 240, $"Push percorre ≈ v·n²/4 = 234 ({z:0.0})");
        // MOVERL prende (os canais param) e o MOVE seguinte solta (0x8030CD30, 0x8030A71C).
        var q = new MotionChannels();
        q.Apply(new SeqCommand("MOVEEX", ["TXPACC", 1f, 6, 0, false, 1, 4]));
        True(q.Apply(new SeqCommand("MOVERL", ["ACTOR", "f_luce", "ball"])) && q.Attached?.S(2) == "ball" && !q.AnyActive, "preso à junta ball do f_luce");
        Equal(0f, q.Step(1).Local.X, "preso não anda");
        q.Apply(new SeqCommand("MOVE", ["Stop", 1f, 4]));
        True(q.Attached == null && q.AnyActive, "MOVE solta e o canal X volta a andar");
        // Todos os MOVEEX dos chefes caem num canal válido.
        foreach (var f in Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases"), "*_seq.json", SearchOption.AllDirectories).Where(f => f.Contains("stagbs")))
            foreach (var c in SeqCommand.LoadAll(File.ReadAllText(f)).Values.SelectMany(s => s).Where(c => c.Kind == "MOVEEX"))
                True(new MotionChannels().Apply(c), $"{Path.GetFileName(f)}: {c.S(0)}");
    });

    Test("Jogo: ADX #35 (DG_CAVE1) decodifica com o loop do cabeçalho; #6 (AM_DUNGE) faz loop inteiro", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        foreach (var (idx, ls) in new[] { (35, 2020224), (6, 0) })
        {
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[idx], ms);
            var pcm = DW4.Formats.Audio.Adx.Decode(ms.ToArray(), out var h);
            Equal((2, 48000), (h.Channels, h.SampleRate), $"#{idx} formato");
            True(h.Loop, $"#{idx} tem loop");
            Equal(ls, h.LoopStart, $"#{idx} início do loop");
            if (idx == 35) Equal(6627896, h.LoopEnd, "#35 fim do loop");
            Equal(h.Samples * 2, pcm.Length, $"#{idx} amostras");
            True(pcm.Any(v => v != 0), $"#{idx} não é silêncio");
        }
    });

    Test("Jogo: bancos .sfp: e_nume (#178) com 7 cues e amostra 6 − CC; allse (#898) com 4 grupos", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[178], ms);
        var data = ms.ToArray();
        var banks = DW4.Formats.Audio.Sfp.Find(data).Select(b => DW4.Formats.Audio.Sfp.Read(data.AsSpan(b.Offset, b.Size).ToArray())).ToList();
        var nume = banks.FirstOrDefault(b => b.Cues.Any(c => c.Group == 0x3A)) ?? throw new InvalidOperationException("sem banco com o grupo 3A");
        Equal(7, nume.Cues.Count, "cues");
        Equal("6,5,4,3,2,1,0", string.Join(",", nume.Cues.Select(c => c.Sample)), "amostras por cue");
        Equal(0x20, nume.Samples[0].DataOffset, "primeira amostra em PCMD + 0x20");
        var pcm = nume.Decode(nume.Samples[5], out _, out _);
        True(pcm.Length > 1000 && pcm.Max(v => Math.Abs((int)v)) > 1000, "nuDEAD01 decodifica com sinal");
        using var ms2 = new MemoryStream();
        afs.CopyEntry(afs.Entries[898], ms2);
        var d2 = ms2.ToArray();
        var allse = DW4.Formats.Audio.Sfp.Find(d2).Select(b => DW4.Formats.Audio.Sfp.Read(d2.AsSpan(b.Offset, b.Size).ToArray())).First(b => b.Cues.Any(c => c.Group == 0));
        Equal("19,20,12,13", string.Join(",", Enumerable.Range(0, 4).Select(g => allse.Cues.Count(c => c.Group == g))), "allse: cues por grupo A900..A903");
    });

    Test("Jogo: geradores da dng1 (#184) com posições absolutas por vaga e gatilhos de área do #179", () =>
    {
        byte[] Read(int index)
        {
            using var afs = AfsArchive.Open(ndmwPath);
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[index], ms);
            return ms.ToArray();
        }
        var gd = Read(184);
        var gens = IgbGenerators.ReadGenerators(IgbFile.Read(gd), gd);
        Equal(12, gens.Count, "geradores (GENE-01..07 e GBOX-01..05)");
        var g3 = gens.Single(g => g.Name == "GENE-03");
        Equal("4,6,6,8 / 4,4,4,4", $"{string.Join(",", g3.Count)} / {string.Join(",", g3.MaxCount)}", "GENE-03 por nº de jogadores");
        // Ponto (−180, 40, 60) + deslocamento (−20, 0, −55).
        Equal((-200f, 40f, 5f), g3.Positions[0], "GENE-03 vaga 0 absoluta");
        Equal(13, g3.EnemyRow, "Numemon (linha 13 da enemy)");
        Equal("gene03.lua", g3.CallLua, "_callLua");
        var g5 = gens.Single(g => g.Name == "GENE-05");
        Equal(-90f, MathF.Round(g5.RotYDeg[1]), "GENE-05 virado −90°");
        var box = gens.Single(g => g.Name == "GBOX-01");
        Equal((-180f, 40f, 360f), box.Positions[0], "GBOX-01 global");
        Equal(370, box.EnemyRow, "g_box (linha 370)");
        var sd = Read(179);
        var vols = IgbGenerators.ReadTriggers(IgbFile.Read(sd), sd);
        Equal(11, vols.Count, "volumes");
        var m3 = vols.Single(v => v.Name == "matg03");
        static (float, float, float) R((float X, float Y, float Z) v) => (MathF.Round(v.X), MathF.Round(v.Y), MathF.Round(v.Z));
        Equal(((-235f, 39f, -15f), (-125f, 140f, 135f)), (R(m3.Min), R(m3.Max)), "caixa do matg03");
    });

    Test("Jogo: índice do ndmw.igb acha os blocos das fases (dng101b1, dng101b2, stag0101)", () =>
    {
        var igb = File.ReadAllBytes(Path.Combine(dir, "ndmw.igb"));
        var index = IgbStageIndex.Read(IgbFile.Read(igb), igb);
        Equal(2080, index.Files.Count, "igAdxAfsFile");
        var stub = index.Stub("dng101b1")!;
        Equal("0 80", $"{stub.Afs} {stub.Index}", "esboço dng101b1.igb");
        using var ndmw = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        ndmw.CopyEntry(ndmw.Entries[stub.Index], ms);
        var refs = IgbStageIndex.StubRefs(IgbFile.Read(ms.ToArray()), ms.ToArray());
        Equal("178s 179", string.Join(" ", refs.Select(r => $"{r.Index}{(r.Sound ? "s" : "")}")), "principal e som");
        var sep = index.Separate("dng101b1").ToList();
        Equal(182, sep.Single(x => x.Name == "acmr_pack.igb").Index, "acmr_pack");
        Equal("1 9", $"{sep.Single(x => x.Name == "usa_pack.igb").Afs} {sep.Single(x => x.Name == "usa_pack.igb").Index}", "usa_pack no area.afs");
        Equal(192, index.Separate("dng101b2").Single(x => x.Name == "generatorn.igb").Index, "generatorn da dng101b2");
        Equal(0, index.Separate("stag0101").Count(x => x.Name == "aaacamera.igb"), "campo sem câmera por volumes");
    });

    Test("Jogo: pontos de entrada da dng1 (#179): 2 grupos de 4 vagas junto do matz01 e do matz02", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[179], ms);
        var data = ms.ToArray();
        var p = IgbEntryPoints.Read(IgbFile.Read(data), data);
        Equal(2, p.Count, "grupos");
        Equal("4,4", $"{p[0].Slots.Count},{p[1].Slots.Count}", "vagas");
        Equal((-300f, 40f, 431f), p[0].Slots[0].Position, "grupo 0, vaga 0");
        Equal(180f, p[0].Slots[0].RotYDeg, "grupo 0 virado 180°");
        Equal((345f, 16f, -440f), p[1].Slots[3].Position, "grupo 1, vaga 3");
        Equal(-90f, p[1].Slots[0].RotYDeg, "grupo 1 virado −90°");
    });

    Test("Jogo: textos do usa_pack da dng1 (area #9): I<9> = mswk65000 \"Open Map\"", () =>
    {
        using var afs = AfsArchive.Open(areaPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[9], ms);
        var data = ms.ToArray();
        var texts = IgbTexts.Read(IgbFile.Read(data), data);
        Equal(113, texts.Count, "beUnicodeObj na lista");
        Equal("mswk65000", texts[9].Key, "chave do item 9");
        Equal("Open Map\r\nHumid Cave B1", texts[9].Text, "texto do item 9");
        Equal("mswk62009", texts[0].Key, "item 0");
    });

    Test("Jogo: câmera de demo \"entry\" da dng1 (#182): órbita de 181 quadros em volta do pivô", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[182], ms);
        var data = ms.ToArray();
        var d = IgbCameraDemo.Read(IgbFile.Read(data), data).Single();
        Equal("entry", d.Name, "nome");
        Equal(5, d.Path.Count, "transformações do caminho");
        Equal(54.43f, MathF.Round(d.Fov, 2), "ângulo de visão");
        var f = d.Sample();
        Equal(182, f.Count, "quadros (0..181)");
        static (float, float, float) R(float[] v) => (MathF.Round(v[0]), MathF.Round(v[1]), MathF.Round(v[2]));
        // Pivô (−300, 40, 325) virado 180°, inclinação −30° e recuo 120; no fim pivô (−300, 40, 410), 0°, −55° e recuo 160.
        Equal((-300f, 100f, 221f), R(f[0].Position), "posição inicial");
        Equal((-300f, 171f, 502f), R(f[^1].Position), "posição final");
        Equal((0f, -0.5f, 0.87f), (MathF.Round(f[0].Forward[0], 2), MathF.Round(f[0].Forward[1], 2), MathF.Round(f[0].Forward[2], 2)), "frente inicial");
    });

    Test("Jogo: HUD da dng1 (#179 + usa_pack area #9): painel, medidores, números e texturas por nome", () =>
    {
        byte[] Read(string path, int index)
        {
            using var afs = AfsArchive.Open(path);
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[index], ms);
            return ms.ToArray();
        }
        static double D(System.Text.Json.Nodes.JsonNode? n) => Convert.ToDouble(n!.GetValue<object>(), System.Globalization.CultureInfo.InvariantCulture);
        var stage = Read(ndmwPath, 179);
        var pack = Read(areaPath, 9);
        var (json, hud) = IgbHud.Export(IgbFile.Read(stage), stage, IgbFile.Read(pack), pack, ["i_dmgnum", "i_stpnl03", "i_stpnl04"]);
        Equal(0, hud.Notes.Count, $"notas: {string.Join("; ", hud.Notes)}");
        var nodes = json["nodes"]!.AsObject();
        Equal("i_stpnl03", (string?)nodes["31271"]!["name"], "raiz do i_stpnl03");
        Equal("pack_stpnlwn01.png", (string?)nodes["31271"]!["texture"], "textura do painel vem do usa_pack");
        True(hud.Images.Any(i => i.File == "pack_stpnlwn01.png" && i.Width == 32 && i.Height == 32), "stpnlwn01.png 32×32");
        // Projeção ortográfica 640 × 480 do igProjectionMatrixAttr.
        var proj = nodes["31271"]!["attrs"]!.AsArray().First(a => (string?)a!["type"] == "igProjectionMatrixAttr")!["matrix"]!.AsArray();
        Equal("0.003125", D(proj[0]).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture), "2/640");
        // Gomo 1 do MP: escala 0 → 1 entre 1 s e 1,0167 s, máscara T+R+S.
        var seq = nodes["31181"]!["sequence"]!;
        Equal(11, (int)seq["mask"]!, "máscara do mpmetr01");
        Equal("0,1,1.0167,3", string.Join(",", seq["times"]!.AsArray().Select(t => D(t).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture))), "tempos do mpmetr01");
        var meters = json["meters"]!.AsArray();
        Equal("hpmt:4,mpmt:4", string.Join(",", meters.Select(m => $"{m!["name"]}:{m["data"]!.AsArray().Count}")), "medidores");
        var dmg = json["numbers"]!.AsArray().First(n => (string?)n!["name"] == "dmgnm0")!["rams"]!.AsArray();
        Equal(11, dmg.Count, "11 números de dano em uso ao mesmo tempo");
        Equal(0.09375, Math.Round(D(dmg[0]!["offsetNext"]), 5), "passo de UV por dígito");
        // O dígito 0 (unidade) é o quad mais à direita: vértices 0..5.
        var d0 = dmg[0]!["digits"]![0]!["vertices"]!.AsArray().Select(v => (int)v![0]!).ToArray();
        Equal("0,1,2,3,4,5", string.Join(",", d0), "vértices do dígito 0");
        var geom = nodes[((int)dmg[0]!["geometry"]!).ToString()]!["meshes"]![0]!["positions"]!.AsArray().Select(p => D(p)).ToArray();
        True(geom[0] >= 0 && geom[3 * 6] < 0, "dígito 0 à direita do dígito 1");
    });

    Test("Jogo: 12 tabelas beDataBase na dng1 (area #3); w_sword01 com TSUYOSA 60..199; Guilmon e Numemon nas tabelas", () =>
    {
        using var afs = AfsArchive.Open(areaPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[3], ms);
        var data = ms.ToArray();
        var notes = new List<string>();
        var tables = IgbDatabase.Read(IgbFile.Read(data), data, notes);
        Equal(0, notes.Count, $"notas: {string.Join("; ", notes)}");
        Equal("abChip,armor1,armor2,conItem,eneatk,enemy,impItem,insItem,pcTech,shinka,wazaPanel,weapon",
            string.Join(",", tables.Select(t => t.Name).Order(StringComparer.Ordinal)), "tabelas");
        var weapon = tables.First(t => t.Name == "weapon");
        Equal(277, weapon.Rows, "linhas de weapon");
        var sword = weapon.RowsWhere("MODEL", "w_sword01").ToList();
        Equal(5, sword.Count, "variantes da w_sword01");
        Equal("60,81,109,148,199", string.Join(",", sword.Select(r => Convert.ToInt32(weapon.Column("TSUYOSA")!.Cells[r]))), "TSUYOSA");
        Equal(649, tables.First(t => t.Name == "enemy").Rows, "linhas de enemy");
        Equal(22, tables.First(t => t.Name == "enemy").RowsWhere("MODEL", "e_nume").Count(), "linhas do e_nume");
        var shinka = tables.First(t => t.Name == "shinka");
        Equal(16, shinka.Rows, "linhas de shinka");
        True(shinka.RowsWhere("MODEL_NAME", "p_girumon").Any(), "Guilmon na shinka");
        // Técnicas do Guilmon (FATO, RECALC 0x80370994): famílias 0/85/40/15/80/35 e nível mínimo 1/12/22/12/23/23.
        int g = shinka.RowsWhere("MODEL_NAME", "p_girumon").First();
        string Cells(params string[] cols) => string.Join(",", cols.Select(c => Convert.ToInt32(shinka.Column(c)!.Cells[g])));
        Equal("0,85,40,15,80,35", Cells("TECH1", "TECH2", "TECH3", "TECH4", "TECH5", "TECH6"), "TECHn");
        Equal("1,12,22,12,23,23", Cells("TECH1_LEVEL", "TECH2_LEVEL", "TECH3_LEVEL", "TECH4_LEVEL", "TECH5_LEVEL", "TECH6_LEVEL"), "TECHn_LEVEL");
        Equal("1,301,751,1251,1751", Cells("TECH1_SKILL1", "TECH1_SKILL2", "TECH1_SKILL3", "TECH1_SKILL4", "TECH1_SKILL5"), "TECH1_SKILLm");
        Equal("80,120", Cells("MMP_INI", "MMP_GROW"), "MMP do Guilmon");
        var tech = tables.First(t => t.Name == "pcTech");
        Equal(100, tech.Rows, "linhas da pcTech");
        Equal("fire1,16,-160,75,6", string.Join(",", new[] { "LABEL", "MP", "HP", "DELAY", "SKILL" }.Select(c => tech.Column(c)!.Cells[0]?.ToString())), "プチファイア");
    });

    Test("Jogo: geradores da dng102b1 (#200) apontam para as listas de drop por jogadores e dificuldade (GENE-01 → nZ1scum1/hZ1scum1/vZ1scum1)", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[200], ms);
        var data = ms.ToArray();
        var gens = IgbDrops.GeneratorLists(IgbFile.Read(data), data);
        True(gens.ContainsKey("GENE-01"), "GENE-01");
        Equal(4, gens["GENE-01"].Count, "uma entrada por número de jogadores");
        Equal("nZ1scum1,hZ1scum1,vZ1scum1", string.Join(",", gens["GENE-01"][0].Each), "por dificuldade");
        var lists = IgbDrops.Read(IgbFile.Read(data), data, []);
        True(gens.Values.SelectMany(p => p).SelectMany(p => p.Each.Concat(p.Last)).All(n => n != null && lists.ContainsKey(n)), "toda lista apontada existe");
    });

    Test("Jogo: estoque das lojas da Central Area (#829): linha por nível, forma e grupo; 36 posições por linha; compra e venda", () =>
    {
        using var afs = AfsArchive.Open(ndmwPath);
        using var ms = new MemoryStream();
        afs.CopyEntry(afs.Entries[829], ms);
        var data = ms.ToArray();
        var tables = IgbDatabase.Read(IgbFile.Read(data), data, []);
        foreach (var n in new[] { "shopWeapon", "shopArmor", "shopChip", "shopJunk" }) True(tables.Any(t => t.Name == n), n);
        int Cell(string table, int row, string col) => Convert.ToInt32(tables.First(t => t.Name == table).Columns.First(c => c.Name == col).Cells[row]);
        // Faixas do nível (FATO 0x80351A2C..): 1 → 0, 16 → 1, 17 → 2, 147 → 23, 148 → 24; armadura/chip vão até 29.
        Equal(0, ShopStock.Row(ShopStock.Weapon, 1, 0, 1, 0), "nv 1, forma 0");
        Equal(1, ShopStock.Row(ShopStock.Weapon, 16, 0, 1, 0), "nv 16");
        Equal(2, ShopStock.Row(ShopStock.Weapon, 17, 0, 1, 0), "nv 17");
        Equal(25 + 23, ShopStock.Row(ShopStock.Weapon, 147, 1, 1, 0), "forma 1 = base 25, nv 147");
        Equal(150 + 24, ShopStock.Row(ShopStock.Weapon, 200, 3, 2, 0), "grupo: base 150, última faixa = linha 174 (a tabela tem 175)");
        Equal(29, ShopStock.Row(ShopStock.Armor, 250, 0, 1, 0), "armadura: última faixa 29 (a tabela tem 30)");
        Equal(-1, ShopStock.Row(ShopStock.Junk, 1, 0, 1, 999), "sucata: abaixo de 1000, nada");
        Equal(8, ShopStock.Row(ShopStock.Junk, 1, 0, 1, 3960000), "sucata: última linha 8 (a tabela tem 9)");
        var items = ShopStock.Items(ShopStock.Weapon, col => Cell("shopWeapon", 0, col));
        Equal(36, items.Count, "36 posições");
        Equal(new ItemRef("weapon", 25), items[6], "coluna 06 da linha 0 = weapon 25 (Long Sword α)");
        Equal(null, items[35], "coluna 35 vazia");
        var junk = ShopStock.Items(ShopStock.Junk, col => Cell("shopJunk", 1, col));
        Equal(new ItemRef("armor2", 42), junk[0], "sucata linha 1, coluna 00: tipo 2 = armor2, linha 42");
        var inv = new Inventory { Bit = 500 };
        True(!ShopTrade.Buy(inv, new ItemRef("weapon", 25), 600), "sem BIT não compra");
        True(ShopTrade.Buy(inv, new ItemRef("weapon", 25), 196) && inv.Bit == 304 && inv.Has(new ItemRef("weapon", 25)), "comprou: BIT − HANBAI e item na pasta");
        True(!ShopTrade.Sell(inv, new ItemRef("weapon", 25), 49, equipped: true), "equipado não vende");
        True(ShopTrade.Sell(inv, new ItemRef("weapon", 25), 49, equipped: false) && inv.Bit == 353 && !inv.Has(new ItemRef("weapon", 25)), "vendeu: BIT + KAITORI");
        inv.Bit = ShopTrade.BitMax - 10; inv.Add(new ItemRef("weapon", 0));
        ShopTrade.Sell(inv, new ItemRef("weapon", 0), 49, false);
        Equal(ShopTrade.BitMax, inv.Bit, "teto de 9 999 999");
        // Discos (HIPÓTESE da shopDisk, ver ShopStock.Table): 1 linha com conItem; a DISKFOLDER segura 9 por tipo.
        var disks = ShopStock.Items(ShopStock.Disk, col => Cell(ShopStock.Table(ShopStock.Disk)!, ShopStock.Row(ShopStock.Disk, 1, 2, 1, 0), col));
        Equal("conItem 2,1,0,5,4,3,6,7,8,9", "conItem " + string.Join(",", disks.Where(d => d != null).Select(d => d!.Value.Row)), "shopDisk linha 0");
        True(disks.All(d => d == null || d.Value.Table == "conItem"), "tudo conItem");
        var dinv = new Inventory { Bit = 100000 };
        for (int i = 0; i < 9; i++) True(ShopTrade.Buy(dinv, new ItemRef("conItem", 2), 10), $"disco {i + 1}");
        True(!ShopTrade.Buy(dinv, new ItemRef("conItem", 2), 10) && dinv.Disks(2) == 9, "o 10º não cabe");
        True(ShopTrade.Sell(dinv, new ItemRef("conItem", 2), 5, false) && dinv.Disks(2) == 8, "vende um disco");
        // X-data (0x803A4C94): na abChip real, a linha 0 (Wisdom Chip α) custa NEXTX 60 e a seguinte é α+30.
        var chipCsv = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagml12", "db", "db_abChip.csv");
        if (File.Exists(chipCsv))
        {
            var rows = File.ReadAllLines(chipCsv).Select(l => l.Split(';')).ToList();
            int nx = Array.IndexOf(rows[0], "NEXTX"), nm = Array.IndexOf(rows[0], "NAME");
            Equal("60", rows[1][nx], "NEXTX da linha 0");
            True(rows[2][nm].EndsWith("+30", StringComparison.Ordinal), $"linha 1 = {rows[2][nm]}");
        }
    });

    Test("Jogo: gameStart.lua da stagIS10 zera as flags de trabalho e copia as permanentes; hostLoad dá a dificuldade máxima", () =>
    {
        var file = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagis10", "lua", "gameStart.lua");
        True(File.Exists(file), $"exportada: {file}");
        var ram = new GameRam();
        for (int f = 700; f < 900; f++) ram.SetFlag(f, true); // todo o progresso permanente ligado
        ram.SetFlag(409, true); ram.SetCounter(2, 5);
        var host = new BeHost(new RecordingEngine(), ram, new BeSystem { FolderName = "stagis10" });
        var errors = new List<string>();
        var gs = new StageScripts(new Dictionary<string, byte[]> { ["gameStart.lua"] = File.ReadAllBytes(file) }, host) { Error = (n, m) => errors.Add($"{n}: {m}") };
        True(gs.Run("gameStart.lua") && errors.Count == 0, "roda sem erro: " + string.Join("; ", errors));
        Equal(0, ram.Counter(2), "COUNTER 2 (missão ativa) zerado");
        int copied = Enumerable.Range(1, 600).Count(f => ram.FlagOn(f) == 1);
        True(copied > 10, $"flags de trabalho copiadas das permanentes: {copied}");
        Equal(1, ram.FlagOn(49), "FLG 49 ← FLG 803");
        var fresh = new GameRam();
        True(new StageScripts(new Dictionary<string, byte[]> { ["gameStart.lua"] = File.ReadAllBytes(file) }, new BeHost(new RecordingEngine(), fresh, new BeSystem())).Run("gameStart.lua"), "personagem novo");
        Equal(0, Enumerable.Range(1, 1000).Count(f => fresh.FlagOn(f) == 1 && f != 1000), "personagem novo: nenhuma flag");
        Equal(0, fresh.DifficultyMax, "novo: só Normal");
        fresh.SetFlag(711, true); Equal(1, fresh.DifficultyMax, "FLG 711: até Hard");
        fresh.SetFlag(77, true); Equal(2, fresh.DifficultyMax, "FLG 77: até Very Hard");
    });

    Test("Jogo: NPC da stagML02 (n_digilfm): SQ_SHOK espera o movimento (WAITCHECK \"Motion\"); o andar continua no DELAY01", () =>
    {
        var file = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagml02", "objetos", "n_digilfm", "ndmw_0817_n_digilfm_seq.json");
        True(File.Exists(file), $"exportada: {file}");
        var json = File.ReadAllText(file);
        var seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)) { ReturnToWaitAtEnd = true, MotionLength = m => m == "shok01" ? 31f : 81f };
        True(seq.Start("SQ_SHOK"), "SQ_SHOK");
        const float dt = 1 / ObjectSequencer.FramesPerSecond;
        for (int i = 0; i < 20; i++) seq.Tick(dt);
        Equal("SQ_SHOK", seq.Sequence, "ainda no SQ_SHOK aos 20 quadros (o script espera ver ISSEQNAME SQ_SHOK)");
        Equal("shok01", seq.CurrentMotion, "movimento shok01");
        for (int i = 0; i < 20; i++) seq.Tick(dt);
        Equal("SQ_WAIT", seq.Sequence, "acabou o shok01 (31 quadros): JUMP SQ_WAIT");
        True(seq.Start("SQ_WALK04"), "SQ_WALK04");
        Equal("DELAY01", seq.Action, "a ação já é a DELAY01");
        Equal("walk01", seq.CurrentMotion, "mas o movimento continua walk01");
        True(seq.Motion[2].Active, "MOVE \"Move\" 0,14 no canal Z");
    });

    Test("Jogo: golpe no jogador pelo hit_p_girumon.lua (dano → MDLaddHp, faixa → SETLABEL/SQ_MDMG, atordoar; dano 0 → só o sono)", () =>
    {
        var dirLua = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "dng101b1", "lua");
        True(File.Exists(Path.Combine(dirLua, "hit_p_girumon.lua")), $"exportado: {dirLua}");
        var chunks = Directory.GetFiles(dirLua, "*.lua").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        var engine = new FightEngine();
        var host = new BeHost(engine, new GameRam(), new BeSystem { FolderName = "dng101b1" });
        var stage = new StageScripts(chunks, host);
        var errors = new List<string>();
        stage.Error = (n, e) => errors.Add($"{n}: {e}");
        string? seq = null;
        engine.Answers["BLPisHitCheck"] = 1; engine.Answers["BLPgetDamageValue"] = 30; engine.Answers["BLPgetDamageRank"] = 1;
        engine.Answers["BLPgetStunTime"] = 20;
        var p = new TestModel { Name = "p_girumon", RamNo = 0, Hp = 100, BulletKind = 70004, OnSequence = s => seq = s };
        True(stage.RunAs("hit_p_girumon.lua", p), "rodou");
        True(errors.Count == 0, string.Join(" | ", errors));
        Equal(70, p.Hp, "HP − 30");
        Equal("SQ_MDMG", seq, "faixa 1 → SQ_MDMG");
        True(engine.Log.Any(l => l.Contains("SETLABEL") && l.Contains("SQ_MDMG/common")), "SETLABEL: " + string.Join(" | ", engine.Log));
        True(engine.Log.Any(l => l.Contains("SETSTUN") && l.Contains("I<20>")), "atordoou 20 quadros");
        True(engine.Calls.Contains("BLPgetPoisonTime:TIME"), "pergunta o veneno pelo campo TIME");
        // Dano 0: só estados, na ordem atordoar (0, 0), sono…; o sono pega.
        engine.Log.Clear(); engine.Calls.Clear(); seq = null;
        engine.Answers["BLPgetDamageValue"] = 0; engine.Answers["BLPgetStunTime"] = 0; engine.Answers["BLPgetSleepTime"] = 90;
        var q = new TestModel { Name = "p_girumon", RamNo = 0, Hp = 100, BulletKind = 70004, OnSequence = s => seq = s };
        True(stage.RunAs("hit_p_girumon.lua", q), "rodou (dano 0)");
        Equal(100, q.Hp, "sem dano");
        True(engine.Log.Any(l => l.Contains("SETSLEEP") && l.Contains("I<90>")), "dormiu: " + string.Join(" | ", engine.Log));
        var st = new PlayerStatus();
        True(st.Apply("SETPOISON", [600, 7, 3]) && st.PoisonSeconds == 11 && st.Gedoku == 3, "veneno: 600/60 + 1 = 11 s");
        Equal(7, st.Tick(60), "1 tique por segundo");
        True(st.Apply("SETSTUN", [30]) && !st.CanAct, "atordoado não age");
        st.Tick(30);
        True(st.CanAct, "passou");
        st.Apply("SETPOWERDOWN", [120, 30, 500]);
        var low = st.Lowered(new Fighter { Tsuyosa = 100, Mamori = 200 });
        Equal((70, 1), (low.Tsuyosa, low.Mamori), "enfraquecer: TSUYOSA − 30, MAMORI no mínimo 1");
        st.Tick(120);
        Equal(100, st.Lowered(new Fighter { Tsuyosa = 100 }).Tsuyosa, "acabou: volta");
    });
    Test("Jogo: NPC do lobby (stagml11 n_digilfm) chega a um MOTIONSET pela SQ_WAIT (animação de espera)", () =>
    {
        var file = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagml11", "objetos", "n_digilfm", "ndmw_0821_n_digilfm_seq.json");
        True(File.Exists(file), file);
        var json = File.ReadAllText(file);
        var seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
        foreach (var lbl in new[] { "DEF", "WLK", "STP" })
        {
            seq.Start("SQ_WAIT");
            seq.SetLabel("SQ_WAIT", lbl);
            for (int i = 0; i < 30; i++) seq.Tick(1 / 60f);
            Console.WriteLine($"   rótulo {lbl}: sequência {seq.Sequence}, movimento {seq.CurrentMotion ?? "(nenhum)"}");
        }
        seq.Start("SQ_WAIT");
        for (int i = 0; i < 30; i++) seq.Tick(1 / 60f);
        True(seq.CurrentMotion != null, $"SQ_WAIT sem rótulo: movimento {seq.CurrentMotion ?? "(nenhum)"}, sequência {seq.Sequence}");
    });
    Test("Jogo: conversa com NPC da Central Area (stagml12 n_digilfm 1): SQ_TALK durante a fala, SQ_WAIT no fim, fila sem travar", () =>
    {
        var dirLua = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagml12", "lua");
        True(Directory.Exists(dirLua), $"exportada: {dirLua}");
        var chunks = Directory.GetFiles(dirLua, "*.lua").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        var engine = new RecordingEngine();
        var npcSeq = new Dictionary<string, string>();
        bool ended = false;
        int tick = 0, confirmAt = -1;
        engine.Responder = m => (m.Class, m.Command) switch
        {
            ("MODELCTRL", "ISSEQNAME") when m.Name is { } nm && nm.StartsWith("i_", StringComparison.Ordinal) => null, // janelas: o cliente não responde
            ("MODELCTRL", "ISSEQNAME") when m.Name != null => npcSeq.GetValueOrDefault($"{m.Name}/{m.Index}", "SQ_WAIT") == m.Text(0) ? 1 : 0,
            ("MODELCTRL", "ISRUN") => null, // como o cliente: janelas sem resposta
            ("SELECTCTRL", "ISEND") => ended ? 1 : 0, // como o cliente: zera em cada GENERATE e liga quando o jogador confirma
            ("SELECTCTRL", "ISSELECTNOW") => 0,
            ("SELECTCTRL", "GETSELECT") => 0,
            _ => null,
        };
        var ram = new GameRam();
        var host = new BeHost(engine, ram, new BeSystem { FolderName = "stagml12" });
        var stage = new StageScripts(chunks, host);
        var errors = new List<string>();
        stage.Error = (n, e) => errors.Add($"{n}: {e}");
        stage.Boot();
        stage.Start();
        // O cliente aplica os MODELCTRL: guarda a sequência pedida de cada modelo (é o que o ISSEQNAME pergunta).
        int seen = 0;
        void Track()
        {
            for (; seen < engine.Log.Count; seen++)
            {
                var p = engine.Log[seen].Split(' ');
                if (p.Length >= 4 && p[0] == "SELECTCTRL" && p[3] == "GENERATE") { ended = false; confirmAt = tick + 5; }
                if (p.Length >= 4 && p[0] == "MODELCTRL" && p[3].StartsWith("SQ_", StringComparison.Ordinal)) npcSeq[$"{p[1]}/{p[2]}"] = p[3];
                else if (p.Length >= 5 && p[0] == "MODELCTRL" && p[3] == "GENERATE" && p[4].Trim('"').StartsWith("SQ_", StringComparison.Ordinal)) npcSeq[$"{p[1]}/{p[2]}"] = p[4].Trim('"');
            }
        }
        for (int i = 0; i < 300; i++) { stage.Tick(); Track(); }
        True(host.Demo.Idle, $"fila livre antes de falar (count {host.Demo.Count}, parada em {host.Demo.Blocking?.Message}) | " + string.Join(" | ", engine.Log.TakeLast(12)));
        var npc = new TestModel { Name = "n_digilfm", RamNo = 1, OnSequence = s => npcSeq["n_digilfm/1"] = s };
        engine.Log.Clear();
        seen = 0;
        True(stage.RunAs("hit_n_digilfm.lua", npc), "hit_n_digilfm.lua rodou");
        bool talkedBeforeWait = false;
        for (int i = 0; i < 3000; i++)
        {
            tick++;
            if (confirmAt >= 0 && tick >= confirmAt) { ended = true; confirmAt = -1; }
            stage.Tick();
            if (engine.Log.Any(l => l.Contains("TALKFONT02")) && npcSeq.GetValueOrDefault("n_digilfm/1") == "SQ_TALK") talkedBeforeWait = true;
            Track();
        }
        True(errors.Count == 0, "sem erro de Lua: " + string.Join("; ", errors));
        True(talkedBeforeWait, $"a fala apareceu com a NPC em SQ_TALK (parada em {host.Demo.Blocking?.Kind} {host.Demo.Blocking?.Message}, NPC {npcSeq.GetValueOrDefault("n_digilfm/1")}): " + string.Join(" | ", engine.Log.Take(40)));
        Equal("SQ_WAIT", npcSeq.GetValueOrDefault("n_digilfm/1"), $"a conversa devolve a NPC ao SQ_WAIT (parada em {host.Demo.Blocking?.Kind} {host.Demo.Blocking?.Message}) " + string.Join(" | ", engine.Log.TakeLast(14)));
        True(host.Demo.Idle, "a fila do diretor de demo terminou: " + host.Demo.Blocking?.Message);
    });

    Test("Jogo: passeio das NPCs (stagml12): aim_n_vmon.lua com \"WPos\" dá um ponto; MAP01 anda longe e para/segue perto", () =>
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!;
        var dirLua = Path.Combine(root, "fases", "stagml12", "lua");
        var chunks = Directory.GetFiles(dirLua, "*.lua").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        var host = new BeHost(new RecordingEngine(), new GameRam(), new BeSystem { FolderName = "stagml12" });
        var stage = new StageScripts(chunks, host);
        var errors = new List<string>();
        stage.Error = (n, e) => errors.Add($"{n}: {e}");
        var targets = new HashSet<string>();
        for (int k = 0; k < 20; k++)
        {
            host.CallName = "WPos";
            host.WorldPosTarget = null;
            True(stage.RunAs("aim_n_vmon.lua", new TestModel { Name = "n_vmon", RamNo = 1, Position = (0, 10, 0) }), "aim rodou: " + string.Join("; ", errors));
            True(host.WorldPosTarget is not null, "devolveu um ponto: " + string.Join("; ", errors));
            targets.Add(host.WorldPosTarget!.Value.ToString());
        }
        True(errors.Count == 0, string.Join("; ", errors));
        True(targets.Count > 1, $"pontos sorteados variam ({targets.Count}): {string.Join(" ", targets.Take(4))}");
        var json = File.ReadAllText(Path.Combine(root, "fases", "stagml12", "objetos", "n_vmon", Directory.GetFiles(Path.Combine(root, "fases", "stagml12", "objetos", "n_vmon"), "*_seq.json").Select(Path.GetFileName).First()!));
        var maps = AiMap.LoadAll(json);
        var map = maps.First(m => m.Name == "MAP01");
        Equal((25f, 10000f, 40000f), (map.RangesSquared[0], map.RangesSquared[1], map.RangesSquared[2]), "raios² do MAP01");
        var brain = new EnemyBrain(maps, SeqCommand.LoadAll(json), () => 0);
        brain.SetMap("MAP01");
        brain.Tick(1 / 30f, new Vector3(0, 0, 0), new Vector3(50, 0, 0), 0);
        True(brain.Walking, "longe do ponto: SQ_WALK");
        True(brain.Yaw > 0, $"vira para o ponto ({brain.Yaw:0.00})");
        brain.Release();
        brain.Tick(1 / 30f, new Vector3(0, 0, 0), new Vector3(2, 0, 0), 0);
        True(!brain.Walking && (brain.External == "SQ_NEXT" || brain.Current?.Sequence == "SQ_WAIT"), $"perto: SQ_NEXT ou SQ_WAIT ({brain.External ?? brain.Current?.Sequence})");
    });

    Test("Jogo: estilo por arma (beWeaponSeqData): w_sword01 tudo slash; w_axe01 axe só no SQ_ATCK1/2", () =>
    {
        var armas = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fatia", "armas");
        True(Directory.Exists(armas), $"exportadas: {armas}");
        var sword = WeaponStyles.FromJson(File.ReadAllText(Path.Combine(armas, "w_sword01", "estilos.json")));
        Equal(21, sword.Map.Count, "21 sequências");
        True(sword.Map.Values.All(v => v == "slash"), "espada: slash");
        var axe = WeaponStyles.FromJson(File.ReadAllText(Path.Combine(armas, "w_axe01", "estilos.json")));
        Equal(("axe", "axe", "slash", "slash"), (axe.Of("SQ_ATCK1"), axe.Of("SQ_ATCK2"), axe.Of("SQ_ATCKTM"), axe.Of("SQ_WAIT")), "machado");
        Equal("slash", axe.Of("SQ_QUALQUER"), "fora da lista: o bloco do SQ_WAIT");
        var styles = Directory.GetDirectories(armas).Select(d => WeaponStyles.FromJson(File.ReadAllText(Path.Combine(d, "estilos.json"))).Of("SQ_ATCK1")).Distinct().OrderBy(x => x).ToList();
        Equal("arm,axe,dagger,dual,gun,maul,saber,slash", string.Join(",", styles), "estilos das 74 armas");
    });

    Test("Jogo: IA do Belphemon (stagbs02, MAP07): longe anda sem parar (escolha de valor 0), perto ataca", () =>
    {
        var file = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "stagbs02", "inimigos", "f_belvam"), "*_seq.json").First();
        var json = File.ReadAllText(file);
        var brain = new EnemyBrain(AiMap.LoadAll(json), SeqCommand.LoadAll(json), () => 0);
        brain.SetMap("MAP07");
        int walking = 0;
        for (int i = 0; i < 60; i++) { brain.Tick(1 / 30f, new Vector3(0, 0, 0), new Vector3(300, 0, 0), brain.Yaw); if (brain.Walking) walking++; }
        Equal(60, walking, "longe (> 120): anda em todos os quadros");
        brain.Release();
        brain.Tick(1 / 30f, new Vector3(0, 0, 0), new Vector3(10, 0, 0), 0);
        Equal("SQ_ATCK1", brain.External, "perto (< 28): SQ_ATCK1");
    });

    Test("Jogo: missão 1 com os scripts reais: quest01 (lobby) → cápsula (end_g_000bcaps, dng101b1) → treasure01 (recompensa)", () =>
    {
        var fases = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases");
        var ram = new GameRam();
        ram.Players[0].Forms.Start(2);
        var missing = new SortedSet<string>();
        // Roda um script da fase com um motor que confirma as falas e responde às janelas como o cliente.
        List<string> Run(string stageName, string script, IBeModel? model = null)
        {
            var chunks = Directory.GetFiles(Path.Combine(fases, stageName, "lua"), "*.lua").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
            var engine = new RecordingEngine();
            bool ended = false; int tick = 0, confirmAt = -1, seen = 0;
            engine.Responder = m => (m.Class, m.Command) switch
            {
                ("SELECTCTRL", "ISEND") => ended ? 1 : 0,
                ("SELECTCTRL", "ISSELECTNOW") => 0,
                ("SELECTCTRL", "GETSELECT") => 0,
                ("MODELCTRL", "ISRUN") => null,
                ("MODELCTRL", "ISSEQNAME") => null,
                ("SOUND", "ISREADY" or "ISALLREADY") => 1,
                _ => null,
            };
            var sys = new BeSystem { FolderName = stageName };
            sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" }); // como o cliente
            var host = new BeHost(engine, ram, sys) { Unimplemented = (n, _) => missing.Add(n) };
            var stage = new StageScripts(chunks, host);
            var errors = new List<string>();
            stage.Error = (n, e) => errors.Add($"{n}: {e}");
            stage.Boot(); // func.lua (funcOpenMessage…)
            // Globais que o hit_n_*.lua da NPC deixa antes da conversa (NPC 0, jogador 0).
            stage.Vm.Globals["g_npcRamNo"] = 0.0;
            stage.Vm.Globals["g_messPlayerNo"] = 0.0;
            stage.Vm.Globals["g_talk"] = "talk"; // a quest01 roda no meio da conversa: fecha a janela que a fala abriu (funcOpenMessage)
            bool ok = model != null ? stage.RunAs(script, model) : stage.Run(script);
            True(ok, $"{stageName}/{script} rodou: " + string.Join("; ", errors));
            for (int i = 0; i < 3000; i++)
            {
                tick++;
                if (confirmAt >= 0 && tick >= confirmAt) { ended = true; confirmAt = -1; }
                stage.Tick();
                for (; seen < engine.Log.Count; seen++)
                    if (engine.Log[seen].StartsWith("SELECTCTRL", StringComparison.Ordinal) && engine.Log[seen].Contains(" GENERATE")) { ended = false; confirmAt = tick + 5; }
            }
            True(errors.Count == 0, $"{script} sem erro de Lua: " + string.Join("; ", errors));
            return engine.Log;
        }
        Run("stagml11", "quest01.lua");
        Equal((1, 1), (ram.FlagOn(409), ram.FlagOn(424)), "quest01 liga a missão ativa (409) e a 424");
        int counter2 = ram.Counter(2);
        var capsule = Run("dng101b1", "end_g_000bcaps.lua", new TestModel { Name = "g_000bcaps" });
        True(capsule.Any(l => l.Contains("NEXTSTAGE \"stagML11\"")), "a cápsula volta ao lobby: " + string.Join(" | ", capsule.TakeLast(6)));
        // FATO (treasure01.lua): a recompensa vai pela COUNTER 0 (≥ 900 → SYSctlBit ADD 100 para cada jogador; depois 600…).
        ram.SetCounter(0, 1000);
        int bit = ram.Players[0].Items.Bit;
        Run("stagml11", "treasure01.lua");
        Equal(bit + 100, ram.Players[0].Items.Bit, "treasure01 com COUNTER 0 ≥ 900: +100 BIT");
        True(missing.Count == 0, "be: sem resposta: " + string.Join(", ", missing));
    });

    Test("Jogo: cronômetro das missões (chk_i_timenm0.lua da dngq2b1e): \"down\" tira 1 s da COUNTER 0 e mostra mm:ss", () =>
    {
        var dirLua = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(dir)))!, "fases", "dngq2b1e", "lua");
        var chunks = Directory.GetFiles(dirLua, "*.lua").ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        var engine = new RecordingEngine { Responder = m => m.Class == "SYSCTL" && m.Command == "GETHP" ? 150 : null }; // como o cliente
        var ram = new GameRam();
        var sys = new BeSystem { FolderName = "dngq2b1e" };
        sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
        var host = new BeHost(engine, ram, sys);
        var missing = new List<string>();
        host.Unimplemented = (n, _) => missing.Add(n);
        var stage = new StageScripts(chunks, host);
        var errors = new List<string>();
        stage.Error = (n, e) => errors.Add($"{n}: {e}");
        ram.SetCounter(0, 1800);
        host.CallName = "down";
        // O first.lua da masmorra de missão calcula minutos e segundos da COUNTER 0 (1800 → 30 e 0).
        stage.Vm.Globals["g_counter_min"] = 30.0;
        stage.Vm.Globals["g_counter_sec"] = 0.0;
        True(stage.Run("chk_i_timenm0.lua") && errors.Count == 0, string.Join("; ", errors));
        host.Tick();
        Equal(1799, ram.Counter(0), "1800 → 1799");
        True(engine.Log.Any(l => l.Contains("timenm0") && l.Contains("I<29>")) && engine.Log.Any(l => l.Contains("timenm1") && l.Contains("I<59>")), "29:59 — " + string.Join(" | ", engine.Log) + " | sem resposta: " + string.Join(",", missing));
    });

    Test("Jogo: MDLgetBulletName pela tabela 0x804DD2A0 (código / 10000 − 1)", () =>
    {
        Equal("PCWA", BeHost.BulletName(10000), "10000 físico");
        Equal("PCMA", BeHost.BulletName(40017), "40000 + linha: técnica");
        Equal("PCBA", BeHost.BulletName(50000), "50000");
        Equal("PCTK", BeHost.BulletName(60000), "60000 sonda de conversa");
        Equal("ENAT", BeHost.BulletName(70070), "70000 golpe de inimigo");
        Equal("", BeHost.BulletName(0), "fora da tabela");
    });

    Test("Jogo: scripts reais dos chefes 02..06 (boot/start/first, hit_<chefe>.lua com golpe, LUACALLs) rodam sem erro de Lua", () =>
    {
        var bosses = new (int Entry, string Folder, string[] Models)[]
        {
            (737, "stagbs02", ["f_belvam"]), (744, "stagbs03", ["f_luce", "f_luce2"]), (751, "stagbs04", ["f_devl1", "f_devl1b"]),
            (758, "stagbs05", ["f_devl22"]), (765, "stagbs06", ["f_devl3"]),
        };
        var report = new List<string>();
        foreach (var (entry, folder, models) in bosses)
        {
            using var afs = AfsArchive.Open(ndmwPath);
            using var ms = new MemoryStream();
            afs.CopyEntry(afs.Entries[entry], ms);
            var data = ms.ToArray();
            var engine = new RecordingEngine();
            var sys = new BeSystem { FolderName = folder };
            sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
            var host = new BeHost(engine, new GameRam(), sys);
            var unimpl = new List<string>();
            host.Unimplemented = (n, _) => unimpl.Add(n);
            var chunks = IgbLua.Chunks(IgbFile.Read(data), data);
            var stage = new StageScripts(chunks, host);
            var errors = new List<string>();
            stage.Error = (n, m) => errors.Add($"{n}: {m}");
            stage.Boot();
            stage.Start();
            for (int i = 0; i < 600; i++) stage.Tick();
            foreach (var m in models)
            {
                var boss = new TestModel { Hp = 5000, BulletKind = 10000, Name = m };
                if (stage.Has($"hit_{m}.lua")) { stage.RunAs($"hit_{m}.lua", boss); boss.Hp = 0; stage.RunAs($"hit_{m}.lua", boss); }
                foreach (var lua in chunks.Keys.Where(k => k.EndsWith($"_{m}.lua", StringComparison.Ordinal) && !k.StartsWith("hit_", StringComparison.Ordinal))) stage.RunAs(lua, boss);
            }
            for (int i = 0; i < 600; i++) stage.Tick();
            report.Add($"{folder}: {engine.Log.Count} comandos, erros {errors.Count}{(errors.Count > 0 ? " (" + string.Join("; ", errors.Distinct().Take(3)) + ")" : "")}, sem resposta: {string.Join(",", unimpl.Distinct())}");
            True(errors.Count == 0, $"{folder}: {string.Join("; ", errors.Distinct().Take(5))}");
        }
        Console.WriteLine("       " + string.Join(Environment.NewLine + "       ", report));
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

sealed class SeqRandom(params uint[] values) : IGameRandom
{
    public int Calls { get; private set; }
    public uint Next() => values[Calls++ % values.Length];
}

sealed class RecordingEngine : IBeEngine
{
    public List<string> Log { get; } = [];
    public double? Answer { get; set; }
    public void Deliver(BeMessage message) => Log.Add(message.ToString());
    public Dictionary<string, double> Answers { get; } = [];
    public Func<BeMessage, double?>? Responder { get; set; }
    public double? Query(BeMessage message) => Responder?.Invoke(message) ?? (message.Command == "GETGENERATEPOS" ? 0 : Answers.TryGetValue(message.Command, out var v) ? v : Answer);
}

sealed class TestModel : IBeModel
{
    public int RamNo { get; init; }
    public int Index => RamNo;
    public Action<string>? OnSequence { get; init; }
    public int Hp { get; set; }
    public int BulletKind { get; init; }
    public string Name { get; init; } = "";
    string IBeModel.ModelName => Name;
    public void SetSequence(string name) => OnSequence?.Invoke(name);
    public (double X, double Y, double Z) Position { get; init; }
    public double Yaw { get; init; }
}

sealed class FightEngine : IBeEngine
{
    public List<string> Log { get; } = [];
    public List<string> Calls { get; } = [];
    public Dictionary<string, double> Answers { get; } = [];
    public void Deliver(BeMessage message) => Log.Add(message.ToString());
    public double? Query(BeMessage message) => null;
    public double? Fight(string method, double[] args) { Calls.Add(method); return Answers.TryGetValue(method, out var v) ? v : 0; }
    public string? FightText(string method, double[] args) => "common";
    public double? FightField(string method, string field) { Calls.Add($"{method}:{field}"); return 0; }
}
