namespace DW4.Gameplay;

/// <summary>Gerador do combate: inteiro de 32 bits sem sinal (os módulos do jogo são sem sinal: mulhwu/divwu).</summary>
public interface IGameRandom
{
    uint Next();
}

/// <summary>
/// Parâmetros de um combatente pelos nomes de coluna que o handler FIGHTCALC consulta. Jogador e inimigo usam o mesmo
/// registro; campos que não se aplicam ficam em 0.
/// </summary>
public sealed record Fighter
{
    public int Tsuyosa { get; init; }
    public int Mamori { get; init; }
    public int Kashikosa { get; init; }
    public int Seishin { get; init; }
    public int Subayasa { get; init; }
    public int Meichuritsu { get; init; }
    public int Chimeiritsu { get; init; }
    /// <summary>MAHIRITSU (paralisia): do jogador vem da arma; do inimigo, da tabela enemy.</summary>
    public int Mahiritsu { get; init; }
    /// <summary>Jogador: chance de bloqueio (BOUGYORITSU) e de esquiva (KAIHIRITSU), em %.</summary>
    public int Bougyoritsu { get; init; }
    public int Kaihiritsu { get; init; }
    public int MaxHp { get; init; }
    public IReadOnlyList<int> Skill { get; init; } = [];
    public int WeaponSkillNo { get; init; }
    /// <summary>ATTACKZOKUSEI do golpe físico: 0 fogo, 1 gelo, 2 trovão, 3 trevas; −1 nenhum (o E2PDAMAGE não tem veneno).</summary>
    public int AttackElement { get; init; } = -1;
    public int HpDrain { get; init; }
    public int MpDrain { get; init; }
    /// <summary>
    /// Resistências por elemento (fogo, gelo, trovão, trevas, veneno). Inimigo: RES_* em milésimos. Jogador:
    /// HONOOTEIKO, KOORITEIKO, KAMINARITEIKO, YAMITEIKO.
    /// </summary>
    public IReadOnlyList<int> Resist { get; init; } = [];

    public int SkillAt(int i) => i >= 0 && i < Skill.Count ? Skill[i] : 0;
    public int Res(int element) => element >= 0 && element < Resist.Count ? Resist[element] : 0;
}

/// <summary>Técnica (linha da pcTech): potência = −HP, índice de SKILL, elemento (0..4 ou −1) e CHIMEIRITSU.</summary>
public sealed record TechniqueStats(int Hp, int SkillIndex, int Element, int Chimeiritsu = 0);

/// <summary>Golpe de inimigo (linha da eneatk): TYPE (0 físico, 1 mágico, 2 % do HP máximo), TSUYOSA, acerto, crítico, elemento.</summary>
public sealed record EnemyAttack(int Type, int Tsuyosa, int Meichuritsu, int Chimeiritsu, int Element);

/// <summary>Modificadores do objeto auxiliar do comando anterior (aux[1], [3], [4], [5], [2]); HIPÓTESE: efeitos de estado.</summary>
public sealed record StatusMods(int CritDown = 0, int EvadeDown = 0, int PowerDown = 0, int DefenseDown = 0, int StunDown = 0);

/// <summary>Resultado de um cálculo de dano: dano, elemento usado (−1 = nenhum), golpe forte (variação 1025) e drenos.</summary>
public sealed record DamageResult(int Damage, int Element, bool Strong, int HpDrained = 0, int MpDrained = 0);

/// <summary>
/// Regras de combate transcritas do handler FIGHTCALC do main.dol (switch FightCom em 0x80372A24), lidas por desmontagem
/// estática em 2026-10-24 (docs/gameplay/fase4-levantamento-combate.md). Conta inteira com divisão truncada; sorteios sem
/// sinal e na mesma ordem do código, inclusive os que o jogo pula. FATO: operações, constantes e ordem. HIPÓTESE: papel
/// das entradas auxiliares e o comportamento da divisão por zero (o código não testa), marcados abaixo.
/// </summary>
public static class DamageFormula
{
    public const int Max = 99999;
    static readonly int[] Variation = [950, 975, 1000, 1025];

    static int Roll(IGameRandom rng, uint modulo) => (int)(rng.Next() % modulo);

    // ---------- Jogador → inimigo ----------

    /// <summary>P2EISHITCHECK (caso 1). Técnicas acertam sempre (sem sorteio).</summary>
    public static bool PlayerHits(Fighter p, Fighter target, bool technique, StatusMods mods, IGameRandom rng)
    {
        if (technique) return true;
        int chance = p.Meichuritsu * (20 * p.SkillAt(p.WeaponSkillNo) + 6000 + 30 * p.Subayasa);
        int evade = Div(chance, target.Subayasa * (100 - mods.EvadeDown) / 100);
        return evade > Roll(rng, 10000);
    }

    /// <summary>P2EISCRITICAL (caso 2), golpe físico.</summary>
    public static bool PlayerCritical(Fighter p, IGameRandom rng) => p.Chimeiritsu > Roll(rng, 200);

    /// <summary>P2EISCRITICAL (caso 2), técnica.</summary>
    public static bool TechniqueCritical(TechniqueStats t, Fighter target, IGameRandom rng)
    {
        if (t.Element < 0) return t.Chimeiritsu > Roll(rng, 100);
        int res = target.Res(t.Element);
        return (t.Chimeiritsu + 100) * Div(10000, res * 12 / 10 + 50) / 100 > Roll(rng, 1000);
    }

    /// <summary>P2EDAMAGE (caso 5), golpe físico.</summary>
    public static DamageResult Physical(Fighter a, Fighter d, bool critical, StatusMods mods, IGameRandom rng)
    {
        int skill = a.SkillAt(a.WeaponSkillNo);
        int dmg = Clamp(Div(6 * a.Tsuyosa + 4 * skill, d.Mamori - mods.DefenseDown));
        if (dmg == 0) return new DamageResult(0, -1, false); // 0x80373010 e 0x803732C4: nenhum sorteio
        int element = a.AttackElement;
        if (element >= 0)
        {
            // FATO: com resistência menor que o sorteio % 1000, soma dano·(1000 − res)/1000.
            // HIPÓTESE: sem elemento (−1) não há sorteio.
            int res = d.Res(element);
            if (res < Roll(rng, 1000)) dmg += dmg * (1000 - res) / 1000;
        }
        dmg = Clamp(dmg); // 0x80373298
        var (final, strong) = Finish(dmg, critical ? CritBonus(skill) : 0, rng);
        return new DamageResult(final, element, strong,
            a.HpDrain != 0 ? final * a.HpDrain / 100 : 0, a.MpDrain != 0 ? final * a.MpDrain / 100 : 0);
    }

    /// <summary>P2EDAMAGE (caso 5), técnica (código / 10000 = 4; linha da pcTech = código % 1000). Sem drenos.</summary>
    public static DamageResult Technique(Fighter a, Fighter d, TechniqueStats t, bool critical, IGameRandom rng)
    {
        int power = -t.Hp;
        if (power == 0) return new DamageResult(0, -1, false);
        int skill = a.SkillAt(t.SkillIndex);
        int dmg = Clamp(Div(20 * power + 10 * a.Kashikosa + 5 * skill, d.Seishin));
        dmg = dmg * (1000 - (t.Element >= 0 ? d.Res(t.Element) : 0)) / 1000;
        dmg = Clamp(dmg);
        if (dmg == 0) return new DamageResult(0, -1, false); // 0x803731A0: o ZOKUSEI nem é lido
        var (final, strong) = Finish(dmg, critical ? CritBonus(skill) : 0, rng);
        return new DamageResult(final, t.Element, strong);
    }

    static int CritBonus(int skill) =>
        skill <= 1000 ? skill / 20 + 30 : skill <= 2000 ? (skill - 1000) / 50 + 80 : (skill - 2000) / 100 + 100;

    // ---------- Inimigo → jogador ----------

    /// <summary>E2PISHITCHECK (caso 0). Golpes com TYPE ≠ 0 acertam sempre.</summary>
    public static bool EnemyHits(Fighter enemy, EnemyAttack atk, Fighter player, IGameRandom rng)
    {
        if (atk.Type != 0) return true;
        if (player.Bougyoritsu > Roll(rng, 100)) return false; // bloqueio
        int evade = player.Subayasa * (player.Kaihiritsu + 100) / 100;
        return Div(atk.Meichuritsu * enemy.Subayasa, evade) > Roll(rng, 100);
    }

    /// <summary>E2PDAMAGE (caso 3).</summary>
    public static DamageResult EnemyDamage(Fighter enemy, EnemyAttack atk, Fighter player, StatusMods mods, IGameRandom rng)
    {
        int dmg;
        int element = atk.Element;
        int res = element >= 0 ? player.Res(element) : 0;
        switch (atk.Type)
        {
            case 0:
            {
                dmg = Clamp(Div((enemy.Tsuyosa - mods.PowerDown) * atk.Tsuyosa, player.Mamori));
                if (element >= 0 && res != 0 && !(res / 5 > Roll(rng, 1000))) dmg += ElementBonus(dmg, res);
                if (dmg != 0)
                {
                    int c1 = enemy.Chimeiritsu - mods.CritDown, c2 = atk.Chimeiritsu;
                    if (!((100 - c1) * (100 - c2) > Roll(rng, 10000))) dmg += dmg * (c2 / 2 + 90) / 100;
                }
                break;
            }
            case 1:
            {
                dmg = Clamp(Div(enemy.Kashikosa * atk.Tsuyosa, player.Seishin));
                if (element >= 0)
                    dmg = res <= 1000 ? dmg * ((1000 - res) / 20 + 50) / 100 : res <= 4200 ? dmg * ((5000 - res) / 80) / 100 : dmg / 10;
                if (dmg != 0 && atk.Chimeiritsu != 0) // 0x80375840 e 0x8037585C: sem sorteio com dano 0 ou c = 0
                {
                    int c = atk.Chimeiritsu;
                    int v = res <= 1000 ? 1000 - res + 100 * c : 100 * c - (res - 1000) / 10;
                    // FATO (0x803758D4..900): soma (c/2 + dano + 90)/100 em vez de multiplicar. Provável bug do jogo, mantido.
                    if (!(v > Roll(rng, 10000))) dmg += (c / 2 + dmg + 90) / 100;
                }
                break;
            }
            case 2:
                dmg = Clamp(player.MaxHp * atk.Tsuyosa / 100);
                if (element >= 0 && res != 0) dmg += ElementBonus(dmg, res);
                break;
            default:
                return new DamageResult(0, -1, false); // 0x8037530C
        }
        dmg = Clamp(dmg);
        if (dmg == 0) return new DamageResult(0, element, false); // 0x80375AFC: dano 0 pula a variação, sem sorteio
        var (final, strong) = Finish(dmg, 0, rng);
        return new DamageResult(final, element, strong);
    }

    // HIPÓTESE de nome: o código lê HONOOTEIKO etc. e com valor alto o bônus cai (faixas 1000 e 5000).
    static int ElementBonus(int dmg, int res) =>
        res <= 1000 ? dmg * ((1000 - res) / 20 + 50) / 100 : res <= 5000 ? dmg * ((5000 - res) / 80) / 100 : 0;

    // ---------- Comum ----------

    /// <summary>Crítico (bônus em %) e variação final: k = sorteio &amp; 3, m = sorteio % (25·dano).</summary>
    static (int Damage, bool Strong) Finish(int dmg, int critPercent, IGameRandom rng)
    {
        if (critPercent != 0) dmg += dmg * critPercent / 100;
        int k = (int)(rng.Next() & 3);
        int m = Roll(rng, (uint)(25 * dmg));
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

    /// <summary>
    /// Experiência de um jogador por inimigo derrotado (P2EEXPUP, caso 6). <paramref name="players"/> = NINZU (1–4).
    /// Com 1 jogador, 100% do EXP; com 4, 80% para quem matou e 40% para os outros.
    /// </summary>
    public static int ExpShare(int enemyExp, int expRitsu, int players, bool killer)
    {
        int e = enemyExp;
        if (expRitsu != 0) e = e * (expRitsu + 100) / 100;
        e = players switch { 2 => e * 140 / 3, 3 => e * 160 / 4, 4 => e * 200 / 5, _ => e * 100 / 2 };
        return killer ? e / 50 : e / 100;
    }

    static int Clamp(int v) => Math.Clamp(v, 0, Max);
    static int Div(int a, int b) => b == 0 ? 0 : a / b; // HIPÓTESE: o código não testa zero; divw por zero é indefinido no manual
}
