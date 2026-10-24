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

    /// <summary>
    /// Jogador sem armaduras nem chips (RECALCPLAYER simplificado): base da shinka pelo nível + arma. FATO: cada soma;
    /// armaduras, chips e efeitos ficam para quando houver inventário.
    /// </summary>
    public Fighter Player(int shinkaRow, int level, int weaponRow, IReadOnlyList<int> skill, IReadOnlyList<int>? upCount = null)
    {
        var s = this["shinka"];
        var w = this["weapon"];
        int Up(int i) => upCount != null && i < upCount.Count ? upCount[i] : 0;
        int Base(string key, int k) => PlayerGrowth.Stat(level, s.Int(shinkaRow, key + "_INI"), s.Int(shinkaRow, key + "_GROW"), Up(k + 2));
        int Clamp(int v) => Math.Clamp(v, 1, 9999);
        return new Fighter
        {
            Tsuyosa = Clamp(Base("STR", 0) + w.Int(weaponRow, "TSUYOSA")),
            Mamori = Clamp(Base("AC", 1)),
            Kashikosa = Clamp(Base("INT", 2) + w.Int(weaponRow, "KASHIKOSA")),
            Seishin = Clamp(Base("MND", 3) + w.Int(weaponRow, "SEISHIN")),
            Subayasa = Clamp(Base("DEX", 4) + w.Int(weaponRow, "SUBAYASA")),
            Meichuritsu = w.Int(weaponRow, "MEICHURITSU"),
            Chimeiritsu = Math.Max(1, w.Int(weaponRow, "CHIMEIRITSU")),
            Mahiritsu = w.Int(weaponRow, "MAHIRITSU"),
            Kaihiritsu = 1,
            MaxHp = PlayerGrowth.MaxHpMp(level, s.Int(shinkaRow, "MHP_INI"), s.Int(shinkaRow, "MHP_GROW"), Up(0)),
            Skill = skill,
            WeaponSkillNo = w.Int(weaponRow, "SKILL"),
            Resist = [s.Int(shinkaRow, "RES_FIRE"), s.Int(shinkaRow, "RES_ICE"), s.Int(shinkaRow, "RES_THUNDER"), s.Int(shinkaRow, "RES_DARK")],
        };
    }
}
