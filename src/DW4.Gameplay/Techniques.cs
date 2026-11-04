namespace DW4.Gameplay;

/// <summary>Uma técnica da pcTech: linha, rótulo da sequência (LABEL), custo em MP, recarga (DELAY) e o que o dano usa.</summary>
public sealed record Technique(int Row, string Name, string Label, int Mp, int Delay, int Rank, TechniqueStats Stats);

/// <summary>
/// Técnicas do jogador, lidas por desmontagem estática em 2026-10-24 (docs/gameplay/fase4-levantamento-combate.md).
/// FATO: a pcTech tem 20 famílias de 5 graus (linha = base + grau − 1); a shinka liga a Digimon às famílias (TECH1..6,
/// TECHn_LEVEL, TECHn_SKILL1..5); a WAZALIST (RECALC 0x80370994) guarda uma técnica por família, no maior grau liberado;
/// o painel (0x80349474) só aceita sem recarga e com MP suficiente, desconta o MP e põe a recarga = DELAY quadros de 60 Hz.
/// </summary>
public static class Techniques
{
    public const int Families = 6, Ranks = 5;

    /// <summary>Técnica de uma linha da pcTech. Elemento pelo ZOKUSEI (fire/ice/thunder/dark/poison; outro → −1).</summary>
    public static Technique Row(GameTables db, int row)
    {
        var t = db["pcTech"];
        int element = t.Text(row, "ZOKUSEI") switch { "fire" => 0, "ice" => 1, "thunder" => 2, "dark" => 3, "poison" => 4, _ => -1 };
        return new Technique(row, t.Text(row, "NAME"), t.Text(row, "LABEL"), t.Int(row, "MP"), t.Int(row, "DELAY"), t.Int(row, "RANK"),
            new TechniqueStats(t.Int(row, "HP"), t.Int(row, "SKILL"), element, t.Int(row, "CHIMEIRITSU")));
    }

    /// <summary>
    /// WAZALIST: para n = 1..6, se TECHn_LEVEL ≤ nível, o maior grau m (5 → 1) com TECHn_SKILLm ≤ SKILL[pcTech[TECHn].SKILL]
    /// entra como a linha TECHn + m − 1. Famílias bloqueadas ficam de fora (a lista mantém a ordem de TECH1..6).
    /// </summary>
    public static List<Technique> List(GameTables db, int shinkaRow, int level, IReadOnlyList<int> skill)
    {
        var s = db["shinka"];
        var pc = db["pcTech"];
        var list = new List<Technique>();
        for (int n = 1; n <= Families; n++)
        {
            int baseRow = s.Int(shinkaRow, $"TECH{n}");
            if (s.Int(shinkaRow, $"TECH{n}_LEVEL") > level) continue;
            int idx = pc.Int(baseRow, "SKILL");
            int sk = idx >= 0 && idx < skill.Count ? skill[idx] : 0;
            for (int m = Ranks; m >= 1; m--)
                if (s.Int(shinkaRow, $"TECH{n}_SKILL{m}") <= sk) { list.Add(Row(db, baseRow + m - 1)); break; }
        }
        return list;
    }
}

/// <summary>Recargas do painel de técnicas, por linha da pcTech, em segundos.</summary>
public sealed class TechniquePanel
{
    readonly Dictionary<int, float> _cooldown = [];

    public float Cooldown(Technique t) => _cooldown.TryGetValue(t.Row, out var c) ? c : 0;

    /// <summary>
    /// Confirmação no painel: sem recarga e com MP ≥ custo, desconta o MP, põe a recarga DELAY/60 s e devolve o LABEL da
    /// sequência (SQ_WAZA); senão null (no jogo, CURSOR_NG). HIPÓTESE: a recarga é por técnica (o código guarda no slot).
    /// </summary>
    public string? TryUse(Technique t, ref int mp)
    {
        if (Cooldown(t) > 0 || mp < t.Mp) return null;
        mp -= t.Mp;
        _cooldown[t.Row] = t.Delay / 60f;
        return t.Label;
    }

    public void Tick(float dt)
    {
        foreach (var k in _cooldown.Keys.ToList()) _cooldown[k] = Math.Max(0, _cooldown[k] - dt);
    }
}
