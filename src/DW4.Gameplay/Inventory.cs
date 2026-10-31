namespace DW4.Gameplay;

/// <summary>Um item de uma tabela do jogo: nome da tabela (weapon, armor1, armor2, abChip, conItem, insItem, impItem) e linha.</summary>
public readonly record struct ItemRef(string Table, int Row);

/// <summary>
/// Inventário de um jogador (GAMERAM/GETPLAYER), lido por desmontagem estática em 2026-10-24
/// (docs/gameplay/fase4-levantamento-itens.md). FATO: DEVICEFOLDER guarda armas, armaduras e chips até 36 itens
/// (0x803AC294); DISKFOLDER conta os discos (conItem) por tipo, até 9 (0x803AC2B4); itens importantes (impItem) ligam um
/// bit da FLAGFOLDER; o item cheio não é pego. Um inventário por jogador, para a party online.
/// </summary>
public sealed class Inventory
{
    public const int DeviceMax = 36, DiskMax = 9;

    readonly List<ItemRef> _devices = [];
    readonly Dictionary<int, int> _disks = [];
    readonly HashSet<int> _flags = [];

    public IReadOnlyList<ItemRef> Devices => _devices;
    public int Bit { get; set; }

    /// <summary>XDATA do jogador. FATO: o GETSHOPDATA (0x803518F8) limita a soma a 9999 (0x270F). HIPÓTESE: o mesmo teto por jogador.</summary>
    public const int XDataMax = 9999;
    int _xdata;
    public int XData { get => _xdata; set => _xdata = Math.Clamp(value, 0, XDataMax); }

    /// <summary>
    /// Fortalecer chip com X-data (janela XDATA, beNDMWShopCtrlXdataChip, 0x803A4C94). FATO:
    ///  · a lista tem só os itens de tipo 3 (abChip) do DEVICEFOLDER (0x803A5070); sem nenhum, a mensagem 0x491 ("No Ability Chip");
    ///  · o custo é a NEXTX da linha atual do chip (0x803B06F0 lê a coluna na linha do item); −1 = não fortalece (0x492); XDATA
    ///    menor que o custo, mensagem 0x489;
    ///  · confirmar faz XDATA −= NEXTX e a linha do chip +1 (a próxima linha é o mesmo chip "+30", "+60"…), e depois RECALCPLAYER.
    /// </summary>
    public bool UpgradeChip(int deviceIndex, int nextX)
    {
        if (deviceIndex < 0 || deviceIndex >= _devices.Count || _devices[deviceIndex].Table != "abChip") return false;
        if (nextX < 0 || XData < nextX) return false;
        XData -= nextX;
        _devices[deviceIndex] = _devices[deviceIndex] with { Row = _devices[deviceIndex].Row + 1 };
        return true;
    }

    // ---------- Banco (D.S.G. Bank, janela BANK / beNDMWShopCtrlEditBit) ----------
    // FATO: os campos BANKBIT (inteiro) e BANKDEVICE (lista) do jogador na GAMERAM (tabela 0x804EAC08); a gravação copia o
    // BANKDEVICE para 96 posições, com −1 nas vazias (0x80364F14). FATO pelos textos: o banco guarda itens e BITS ("BITS Bank",
    // "Item Bank", mswkPRG1300..1356). HIPÓTESES: teto do BANKBIT igual ao do BIT (9 999 999); só itens do DEVICEFOLDER entram.
    public const int BankDeviceMax = 96, BitMax = 9_999_999;
    readonly List<ItemRef> _bank = [];
    public int BankBit { get; set; }
    public IReadOnlyList<ItemRef> BankDevices => _bank;

    public bool DepositBit(int amount)
    {
        if (amount <= 0 || amount > Bit) return false;
        int room = BitMax - BankBit;
        if (amount > room) return false;
        Bit -= amount;
        BankBit += amount;
        return true;
    }

    public bool WithdrawBit(int amount)
    {
        if (amount <= 0 || amount > BankBit || Bit + amount > BitMax) return false;
        BankBit -= amount;
        Bit += amount;
        return true;
    }

    /// <summary>Guarda um item do DEVICEFOLDER no banco (o chamador recusa item equipado).</summary>
    public bool DepositDevice(ItemRef item)
    {
        if (_bank.Count >= BankDeviceMax || item.Table is not ("weapon" or "armor1" or "armor2" or "abChip") || !_devices.Remove(item)) return false;
        _bank.Add(item);
        return true;
    }

    public bool WithdrawDevice(ItemRef item)
    {
        if (_devices.Count >= DeviceMax || !_bank.Remove(item)) return false;
        _devices.Add(item);
        return true;
    }

    /// <summary>Volta o banco gravado (save do port).</summary>
    public void RestoreBank(int bit, IEnumerable<ItemRef> devices)
    {
        BankBit = Math.Clamp(bit, 0, BitMax);
        _bank.Clear();
        _bank.AddRange(devices.Take(BankDeviceMax));
    }
    public int Disks(int conItemRow) => _disks.TryGetValue(conItemRow, out var n) ? n : 0;
    public bool HasFlag(int impItemRow) => _flags.Contains(impItemRow);
    /// <summary>Discos por linha da conItem (para gravar).</summary>
    public IReadOnlyDictionary<int, int> DiskCounts => _disks;
    /// <summary>Linhas da impItem ligadas na FLAGFOLDER (para gravar).</summary>
    public IReadOnlyCollection<int> ImportantFlags => _flags;

    /// <summary>
    /// Guarda um item pego (0x803AC1A0). Devolve false quando a pasta está cheia (o item fica no chão). insItem não é
    /// guardado: é efeito imediato (ver <see cref="Instant"/>).
    /// </summary>
    public bool Add(ItemRef item)
    {
        switch (item.Table)
        {
            case "weapon" or "armor1" or "armor2" or "abChip":
                if (_devices.Count >= DeviceMax) return false;
                _devices.Add(item);
                return true;
            case "conItem":
                if (Disks(item.Row) >= DiskMax) return false;
                _disks[item.Row] = Disks(item.Row) + 1;
                return true;
            case "impItem":
                _flags.Add(item.Row);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Item guardado? (DEVICEFOLDER para armas, armaduras e chips; DISKFOLDER &gt; 0; FLAGFOLDER para impItem.)</summary>
    public bool Has(ItemRef item) => item.Table switch
    {
        "conItem" => Disks(item.Row) > 0,
        "impItem" => _flags.Contains(item.Row),
        _ => _devices.Contains(item),
    };

    /// <summary>Tira um item (o primeiro igual na pasta; um disco; o bit do impItem). false se não havia.</summary>
    public bool Remove(ItemRef item)
    {
        switch (item.Table)
        {
            case "conItem":
                if (Disks(item.Row) <= 0) return false;
                _disks[item.Row]--;
                return true;
            case "impItem":
                return _flags.Remove(item.Row);
            default:
                return _devices.Remove(item);
        }
    }

    /// <summary>
    /// Disco usado no painel (0x80349B0C): exige quantidade &gt; 0, gasta 1 e devolve a cura de HP e MP da conItem (o jogo
    /// soma com limite no máximo, como o ADDHP/ADDMP em 0x8036F220). Null sem disco. A recarga do slot não está aqui
    /// (unidade ainda não confirmada).
    /// </summary>
    public (int Hp, int Mp)? UseDisk(GameTables db, int conItemRow)
    {
        if (Disks(conItemRow) <= 0) return null;
        _disks[conItemRow]--;
        var t = db["conItem"];
        return (t.Int(conItemRow, "HP"), t.Int(conItemRow, "MP"));
    }

    /// <summary>
    /// insItem pego: efeito imediato. Linha 0 = BIT (a quantia vem do gerador; regra ainda não lida); as outras levam UP no
    /// atributo NOURYOKU. HIPÓTESE: os plugs somam no UPCNT permanente que o RECALC lê.
    /// </summary>
    public static (string Nouryoku, int Up) Instant(GameTables db, int insItemRow)
    {
        var t = db["insItem"];
        return (t.Text(insItemRow, "NOURYOKU"), t.Int(insItemRow, "UP"));
    }
}
