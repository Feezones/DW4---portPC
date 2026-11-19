namespace DW4.Gameplay;

/// <summary>
/// Crescimento das skills de arma e de técnica (P2ESKILLUP, caso 13 do FightCom, 0x803736C8), lido por desmontagem
/// estática em 2026-10-24. A tabela de taxas (int16, 4 faixas de nível × 21 faixas de skill × 3 colunas, em 0x804EAF18)
/// é dado do jogo: não fica no repositório e é lida do main.dol da cópia de trabalho (<c>dw4 dol-skilltab</c>).
/// </summary>
public sealed class SkillGrowth
{
    static readonly int[] SkillBands = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 20, 30, 100, 300, 500, 1000, 2000, 3000, 4000, 6000, 10000];
    readonly short[,,] _rates;

    /// <param name="rates">[faixa de nível 0..3, faixa de skill 0..20, coluna 0..2].</param>
    public SkillGrowth(short[,,] rates)
    {
        if (rates.GetLength(0) != 4 || rates.GetLength(1) != SkillBands.Length || rates.GetLength(2) != 3)
            throw new ArgumentException("tabela de taxas deve ser 4 × 21 × 3", nameof(rates));
        _rates = rates;
    }

    /// <summary>Lê o JSON gravado por <c>dw4 dol-skilltab</c>: {"rates": [[[n, n, n] × 21] × 4]}.</summary>
    public static SkillGrowth FromJson(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var rates = new short[4, SkillBands.Length, 3];
        int i = 0;
        foreach (var lv in doc.RootElement.GetProperty("rates").EnumerateArray())
        {
            int j = 0;
            foreach (var band in lv.EnumerateArray())
            {
                int k = 0;
                foreach (var v in band.EnumerateArray()) rates[i, j, k++] = v.GetInt16();
                j++;
            }
            i++;
        }
        return new SkillGrowth(rates);
    }

    /// <summary>Limite da skill pelo nível: 30L (até 36), 20L + 360 (até 107), 10L + 1430 (até 227), senão 8L + 1884.</summary>
    public static int Limit(int level) =>
        level <= 36 ? 30 * level : level <= 107 ? 20 * level + 360 : level <= 227 ? 10 * level + 1430 : 8 * level + 1884;

    static int LevelBand(int level) => level <= 36 ? 0 : level <= 107 ? 1 : level <= 227 ? 2 : 3;

    static int SkillBand(int sk)
    {
        for (int j = 0; j < SkillBands.Length; j++) if (sk <= SkillBands[j]) return j;
        return SkillBands.Length - 1;
    }

    /// <summary>
    /// Tenta subir +1 a skill <paramref name="sk"/>. Técnica: chance = taxa·100. Arma: chance = taxa·GROW·500/1000, com o
    /// &lt;X&gt;_GROW da shinka do tipo de arma (BLUNT, SLASH, STAB, BASH, SHOT, CRUSH). Sobe se chance &gt; sorteio % 100000.
    /// Coluna: 2 com <paramref name="special"/> (i3 = 1), senão 0. FATO: a coluna 1 nunca é usada (andc com 0 no código).
    /// No limite do nível não há sorteio.
    /// </summary>
    public int Raise(int sk, int level, bool technique, int weaponGrow, bool special, IGameRandom rng)
    {
        if (sk >= Limit(level)) return sk;
        int rate = _rates[LevelBand(level), SkillBand(sk), special ? 2 : 0];
        int chance = technique ? rate * 100 : rate * weaponGrow * 500 / 1000;
        return chance > (int)(rng.Next() % 100000) ? sk + 1 : sk;
    }
}
