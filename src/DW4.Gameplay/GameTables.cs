using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>
/// As tabelas de balanceamento do jogo (weapon, enemy, eneatk, shinka…), lidas do JSON que <c>dw4 igb-db</c> grava na
/// cópia de trabalho (os dados não ficam no repositório). Células por nome de coluna, como o FIGHTCALC consulta.
/// </summary>
public sealed class GameTables
{
    public sealed class Table(int rows, Dictionary<string, object?[]> columns)
    {
        public int Rows { get; } = rows;
        public bool Has(string column) => columns.ContainsKey(column);

        public int Int(int row, string column) =>
            columns.TryGetValue(column, out var c) && row >= 0 && row < c.Length ? c[row] switch { int i => i, bool b => b ? 1 : 0, float f => (int)f, _ => 0 } : 0;

        public string Text(int row, string column) =>
            columns.TryGetValue(column, out var c) && row >= 0 && row < c.Length ? c[row] as string ?? "" : "";

        public IEnumerable<int> RowsWhere(string column, string value) => Enumerable.Range(0, Rows).Where(r => Text(r, column) == value);
    }

    readonly Dictionary<string, Table> _tables;
    GameTables(Dictionary<string, Table> tables) => _tables = tables;

    public Table this[string name] => _tables[name];
    public bool Contains(string name) => _tables.ContainsKey(name);

    public static GameTables FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var tables = new Dictionary<string, Table>();
        foreach (var t in doc.RootElement.EnumerateObject())
        {
            var cols = new Dictionary<string, object?[]>();
            foreach (var c in t.Value.GetProperty("columns").EnumerateArray())
                cols[c.GetProperty("name").GetString() ?? ""] = c.GetProperty("cells").EnumerateArray().Select(v => v.ValueKind switch
                {
                    JsonValueKind.Number when v.TryGetInt32(out int i) => (object?)i,
                    JsonValueKind.Number => v.GetSingle(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => v.GetString(),
                    _ => null,
                }).ToArray();
            tables[t.Name] = new Table(t.Value.GetProperty("rows").GetInt32(), cols);
        }
        return new GameTables(tables);
    }

    /// <summary>Linha da eneatk (código do golpe % 10000; HIPÓTESE forte: o código é o número do HITBOX, 70004 → 4).</summary>
    public EnemyAttack EnemyAttackRow(int code)
    {
        var t = this["eneatk"];
        int row = code % 10000;
        string zok = t.Text(row, "ZOKUSEI");
        int element = zok switch { "fire" => 0, "ice" => 1, "thunder" => 2, "dark" => 3, _ => -1 };
        return new EnemyAttack(t.Int(row, "TYPE"), t.Int(row, "TSUYOSA"), t.Int(row, "MEICHURITSU"), t.Int(row, "CHIMEIRITSU"), element);
    }

    /// <summary>Campos de estado do golpe inimigo (eneatk: TYPE, KOUKA, KOUKARANK, KOUKATIME, MAHIRITSU, GEDOKU).</summary>
    public EnemyEffect EnemyEffectRow(int code)
    {
        var t = this["eneatk"];
        int row = code % 10000;
        return new EnemyEffect(t.Int(row, "TYPE"), t.Text(row, "KOUKA"), t.Int(row, "KOUKARANK"), t.Int(row, "KOUKATIME"),
            t.Int(row, "MAHIRITSU"), t.Int(row, "GEDOKU"));
    }

    /// <summary>Inimigo pela linha da tabela enemy (Int@7 do gerador, HIPÓTESE forte).</summary>
    public Fighter Enemy(int row)
    {
        var t = this["enemy"];
        return new Fighter
        {
            Tsuyosa = t.Int(row, "TSUYOSA"), Mamori = t.Int(row, "MAMORI"), Kashikosa = t.Int(row, "KASHIKOSA"),
            Seishin = t.Int(row, "SEISHIN"), Subayasa = t.Int(row, "SUBAYASA"), Chimeiritsu = t.Int(row, "CHIMEIRITSU"),
            Mahiritsu = t.Int(row, "MAHIRITSU"), MaxHp = t.Int(row, "HPMAX"),
            Resist = [t.Int(row, "RES_FIRE"), t.Int(row, "RES_ICE"), t.Int(row, "RES_THUNDER"), t.Int(row, "RES_DARK"), t.Int(row, "RES_POISON")],
        };
    }

    /// <summary>Jogador sem armaduras nem chips: atalho para o <see cref="Player(int, int, Equipment, IReadOnlyList{int}, IReadOnlyList{int}?)"/>.</summary>
    public Fighter Player(int shinkaRow, int level, int weaponRow, IReadOnlyList<int> skill, IReadOnlyList<int>? upCount = null) =>
        Player(shinkaRow, level, new Equipment(weaponRow), skill, upCount);

    static readonly string[] Elements = ["FIRE", "ICE", "THUNDER", "DARK"];

    /// <summary>
    /// RECALCPLAYER (0x8036FDE8), lido por desmontagem estática em 2026-10-24: base da shinka pelo nível + UPCNT + arma
    /// (POWERSOCKET[0]) + chips da arma (POWERPOCKET) + armadura 1 (MAINSLOT) + chips dela (MAINPOCKET) + armadura 2
    /// (SUBSLOT). FATO: qual coluna soma em cada atributo e os limites. Fora daqui: UPDATA/DOWNDATA (efeitos temporários),
    /// o valor de revisão de cada item (item+0x14) e o bônus do SHUSEINOURYOKU da armadura 2, que ainda não existem no port.
    /// </summary>
    public Fighter Player(int shinkaRow, int level, Equipment eq, IReadOnlyList<int> skill, IReadOnlyList<int>? upCount = null)
    {
        var s = this["shinka"];
        var w = this["weapon"];
        int Up(int i) => upCount != null && i < upCount.Count ? upCount[i] : 0;
        int Base(string key, int k) => PlayerGrowth.Stat(level, s.Int(shinkaRow, key + "_INI"), s.Int(shinkaRow, key + "_GROW"), Up(k + 2));
        int Weapon(string col) => w.Int(eq.WeaponRow, col);
        int A1(string col) => eq.Armor1Row >= 0 ? this["armor1"].Int(eq.Armor1Row, col) : 0;
        int A2(string col) => eq.Armor2Row >= 0 ? this["armor2"].Int(eq.Armor2Row, col) : 0;
        int WChips(string col) => eq.WeaponChips.Sum(r => this["abChip"].Int(r, col));
        int AChips(string col) => eq.ArmorChips.Sum(r => this["abChip"].Int(r, col));
        int Clamp(int v) => Math.Clamp(v, 1, 9999);
        int Max(int v) => Math.Min(v, 99999);
        int element = eq.WeaponChips.Select(r => this["abChip"].Text(r, "ZOKUSEI") switch
        {
            "fire" => 0, "ice" => 1, "thunder" => 2, "dark" => 3, _ => -1,
        }).Where(e => e >= 0).DefaultIfEmpty(-1).First();
        return new Fighter
        {
            Tsuyosa = Clamp(Base("STR", 0) + Weapon("TSUYOSA") + WChips("TSUYOSA") + A2("TSUYOSA")),
            Mamori = Clamp(Base("AC", 1) + A1("MAMORI") + A2("MAMORI")),
            Kashikosa = Clamp(Base("INT", 2) + Weapon("KASHIKOSA") + WChips("KASHIKOSA") + A1("KASHIKOSA") + AChips("KASHIKOSA") + A2("KASHIKOSA")),
            Seishin = Clamp(Base("MND", 3) + Weapon("SEISHIN") + WChips("SEISHIN") + A1("SEISHIN") + AChips("SEISHIN") + A2("SEISHIN")),
            Subayasa = Clamp(Base("DEX", 4) + Weapon("SUBAYASA") + WChips("SUBAYASA") + A1("SUBAYASA") + AChips("SUBAYASA") + A2("SUBAYASA")),
            Meichuritsu = Weapon("MEICHURITSU"),
            Chimeiritsu = Math.Max(1, Weapon("CHIMEIRITSU") + WChips("CHIMEIRITSU")),
            Mahiritsu = Weapon("MAHIRITSU"),
            Kaihiritsu = Math.Max(1, AChips("CHOHATURITSU")),
            Bougyoritsu = A1("BOUGYORITSU"),
            Mahiteikou = A1("MAHITEIKOU"),
            Dokuteikou = A1("DOKUTEIKOU"),
            Bitritsu = A2("BITRITSU"),
            Expritsu = A2("EXPRITSU"),
            Luckritsu = A2("LUCKRITSU"),
            AttackDelay = Math.Min(70, Weapon("DELAY")),
            MaxHp = Max(PlayerGrowth.MaxHpMp(level, s.Int(shinkaRow, "MHP_INI"), s.Int(shinkaRow, "MHP_GROW"), Up(0)) + A1("MAXHP") + A2("MAXHP")),
            // FATO (X-data, 0x803948E4..0x803949F0): UPCNT[0] = MHP, [1] = MMP, [2..6] = STR, AC, INT, MND, DEX.
            MaxMp = Max(PlayerGrowth.MaxHpMp(level, s.Int(shinkaRow, "MMP_INI"), s.Int(shinkaRow, "MMP_GROW"), Up(1)) + A1("MAXMP") + A2("MAXMP")),
            Skill = skill,
            WeaponSkillNo = Weapon("SKILL"),
            AttackElement = element,
            HpDrain = WChips("HPDRAIN"),
            MpDrain = WChips("MPDRAIN"),
            Resist = Elements.Select((e, i) => Clamp(s.Int(shinkaRow, "RES_" + e) + Up(7 + i) + A1("RES_" + e) + AChips("RES_" + e) + A2("RES_" + e))).ToArray(),
        };
    }

    /// <summary>Arma equipável (0x8036EA70, 0x80384384): SOUBI tem o bit da Digimon e SKILL[arma.SKILL] ≥ SKILLPOINT.</summary>
    public bool CanEquipWeapon(int weaponRow, int digimonNo, IReadOnlyList<int> skill)
    {
        var w = this["weapon"];
        int idx = w.Int(weaponRow, "SKILL");
        return (w.Int(weaponRow, "SOUBI") & (1 << digimonNo)) != 0 && idx >= 0 && idx < skill.Count && skill[idx] >= w.Int(weaponRow, "SKILLPOINT");
    }

    /// <summary>Armadura equipável (0x803860A8): só nível ≥ LVPOINT (o SOUBI das armaduras é usado na loja).</summary>
    public bool CanEquipArmor(string table, int row, int level) => level >= this[table].Int(row, "LVPOINT");
}

/// <summary>
/// Equipamento de um jogador: arma ativa (POWERSOCKET[0]), armaduras (MAINSLOT = armor1, SUBSLOT = armor2; −1 = vazio) e
/// linhas da abChip encaixadas na arma (POWERPOCKET) e na armadura (MAINPOCKET). Na digievolução o jogo desequipa
/// armaduras e chips e re-escolhe a arma (0x8039CA4C).
/// </summary>
public sealed record Equipment(int WeaponRow, int Armor1Row = -1, int Armor2Row = -1)
{
    public IReadOnlyList<int> WeaponChips { get; init; } = [];
    public IReadOnlyList<int> ArmorChips { get; init; } = [];
}
