namespace DW4.Gameplay;

/// <summary>Gerador de números do jogo (método virtual +0x60 do objeto de sorteio; o algoritmo ainda é DESCONHECIDO).</summary>
public interface IGameRandom
{
    /// <summary>Inteiro não negativo.</summary>
    int Next();
}

/// <summary>Parâmetros do atacante pelos nomes de coluna que o jogo consulta (TSUYOSA, KASHIKOSA, SKILL[ ], ATTACKZOKUSEI…).</summary>
public sealed record AttackerStats(int Tsuyosa, int Kashikosa, IReadOnlyList<int> Skill, int WeaponSkillNo, int AttackElement,
    int HpDrain = 0, int MpDrain = 0)
{
    public int SkillAt(int i) => i >= 0 && i < Skill.Count ? Skill[i] : 0;
}

/// <summary>Parâmetros do alvo: MAMORI, SEISHIN e RES_FIRE, RES_ICE, RES_THUNDER, RES_DARK, RES_POISON (milésimos).</summary>
public sealed record DefenderStats(int Mamori, int Seishin, IReadOnlyList<int> Resist)
{
    public int Res(int element) => element >= 0 && element < Resist.Count ? Resist[element] : 0;
}

/// <summary>Técnica (linha da pcTech): potência = −HP, índice de SKILL do atacante e elemento (0..4 ou −1).</summary>
public sealed record TechniqueStats(int Hp, int SkillIndex, int Element);

/// <summary>Resultado do P2EDAMAGE: dano, elemento usado (−1 = nenhum), golpe forte (variação 1025) e drenos.</summary>
public sealed record DamageResult(int Damage, int Element, bool Strong, int HpDrained = 0, int MpDrained = 0);

/// <summary>
/// Dano do jogador no inimigo, transcrito do caso P2EDAMAGE (5) do handler FIGHTCALC no main.dol (0x80372EC4–0x80373578),
/// lido por desmontagem estática em 2026-10-24 (docs/gameplay/fase4-levantamento-combate.md). Toda a conta é inteira, com
/// divisão truncada (divw). Ordem dos sorteios igual à do código. FATO: operações e constantes. HIPÓTESE: o papel de cada
/// entrada (crit vem de BLTisCritical, a redução de defesa vem do objeto da checagem anterior) e o tratamento de casos que
/// o código não protege (divisão por zero), marcados abaixo.
/// </summary>
public static class DamageFormula
{
    public const int Max = 99999;
    static readonly int[] Variation = [950, 975, 1000, 1025];

    /// <summary>Golpe físico (código do golpe / 10000 ≠ 4).</summary>
    public static DamageResult Physical(AttackerStats a, DefenderStats d, bool critical, int defenseDown, IGameRandom rng)
    {
        int skill = a.SkillAt(a.WeaponSkillNo);
        int defense = d.Mamori - defenseDown;
        int dmg = Clamp(Div(6 * a.Tsuyosa + 4 * skill, defense));
        int element = a.AttackElement;
        if (element >= 0)
        {
            // FATO: se a resistência for menor que o sorteio % 1000, soma dano·(1000 − res)/1000.
            // HIPÓTESE: sem elemento (−1) não há sorteio nem bônus.
            int res = d.Res(element);
            if (res < rng.Next() % 1000) dmg += dmg * (1000 - res) / 1000;
        }
        var (final, strong) = Common(dmg, critical, skill, rng);
        return new DamageResult(final, element, strong,
            a.HpDrain != 0 ? final * a.HpDrain / 100 : 0, a.MpDrain != 0 ? final * a.MpDrain / 100 : 0);
    }

    /// <summary>Técnica (código do golpe / 10000 = 4). Sem drenos.</summary>
    public static DamageResult Technique(AttackerStats a, DefenderStats d, TechniqueStats t, bool critical, IGameRandom rng)
    {
        int power = -t.Hp;
        if (power == 0) return new DamageResult(0, -1, false);
        int skill = a.SkillAt(t.SkillIndex);
        int dmg = Clamp(Div(20 * power + 10 * a.Kashikosa + 5 * skill, d.Seishin));
        int res = t.Element >= 0 ? d.Res(t.Element) : 0;
        dmg = dmg * (1000 - res) / 1000;
        var (final, strong) = Common(dmg, critical, skill, rng);
        return new DamageResult(final, t.Element, strong);
    }

    static (int Damage, bool Strong) Common(int dmg, bool critical, int skill, IGameRandom rng)
    {
        if (critical)
        {
            int bonus = skill <= 1000 ? skill / 20 + 30 : skill <= 2000 ? (skill - 1000) / 50 + 80 : (skill - 2000) / 100 + 100;
            dmg += dmg * bonus / 100;
        }
        int k = rng.Next() & 3;
        int spread = 25 * dmg;
        int m = spread > 0 ? rng.Next() % spread : 0; // HIPÓTESE: com dano 0 o jogo não sorteia (o código não protege o módulo por zero)
        dmg = Clamp((dmg * Variation[k] + m) / 1000);
        return (dmg, k == 3);
    }

    /// <summary>Faixa do dano (BLTgetDamageRank, 0x802EE91C): 3 se o alvo morreu, 2 acima de 50% do HP máximo, 1 acima de 25%.</summary>
    public static int Rank(int damage, int targetHp, int targetMaxHp)
    {
        if (targetHp <= 0) return 3;
        float f = targetMaxHp > 0 ? (float)damage / targetMaxHp : 0;
        return f > 0.5f ? 2 : f > 0.25f ? 1 : 0;
    }

    static int Clamp(int v) => Math.Clamp(v, 0, Max);
    static int Div(int a, int b) => b == 0 ? 0 : a / b; // HIPÓTESE: divw por zero dá 0 no Gekko (resultado indefinido no manual)
}
