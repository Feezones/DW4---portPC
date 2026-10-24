namespace DW4.Gameplay;

/// <summary>Técnica com os campos de estado da pcTech (TIME, HP, SUBAYASA, CHIMEIRITSU, MHIRITSU, KAIHIRITSU, TSUYOSA, MAMORI).</summary>
public sealed record TechniqueEffect(int Time, int Hp = 0, int Subayasa = 0, int Chimeiritsu = 0, int Mahiritsu = 0, int Kaihiritsu = 0,
    int Tsuyosa = 0, int Mamori = 0);

/// <summary>Golpe inimigo com os campos de estado da eneatk: KOUKA ("doku", "sleep", "ashidome", "freeze", "powerdown"…).</summary>
public sealed record EnemyEffect(int Type, string Kouka, int KoukaRank, int KoukaTime, int Mahiritsu = 0, int Gedoku = 0);

/// <summary>
/// Estados de combate (casos 7–19 do FightCom no main.dol), lidos por desmontagem estática em 2026-10-24
/// (docs/gameplay/fase4-levantamento-combate.md). Cada método devolve a duração em quadros quando o estado pega e null
/// quando não pega. FATO: contas e sorteios. HIPÓTESE: o significado de i2/i3/i4 (dano, HP máximo, faixa do dano) e o −1
/// das durações zeradas (provável "sem fim"), marcados nos parâmetros.
/// </summary>
public static class StatusFormula
{
    static int Roll(IGameRandom rng, uint m) => (int)(rng.Next() % m);
    static int Div(int a, int b) => b == 0 ? 0 : a / b; // o código não protege a divisão (i2, MHP, SEISHIN)
    static int OrForever(int d) => d == 0 ? -1 : d;

    /// <summary>Curva da skill da técnica: sk ≤ 1000 → sk/4 + b0; ≤ 2000 → (sk − 1000)/5 + b1; senão (sk − 2000)/10 + b2.</summary>
    public static int SkillCurve(int sk, int b0, int b1, int b2) =>
        sk <= 1000 ? sk / 4 + b0 : sk <= 2000 ? (sk - 1000) / 5 + b1 : (sk - 2000) / 10 + b2;

    // ---------- Jogador → inimigo ----------

    /// <summary>P2ESTUNTIME (14). HIPÓTESE: <paramref name="damage"/> = i3 e <paramref name="targetMaxHp"/> = i2.</summary>
    public static int? PlayerStun(Fighter p, int targetResStun, int damage, int targetMaxHp, IGameRandom rng)
    {
        int frac50 = Div(50 * damage, targetMaxHp), frac30 = Div(30 * damage, targetMaxHp);
        int chance = (p.Mahiritsu + frac50 + 100 - targetResStun) / 2;
        if (!(chance > Roll(rng, 100))) return null;
        return (frac30 + 3 * p.Mahiritsu) * (200 - targetResStun) / 400;
    }

    /// <summary>P2EPOISONTIME (15) "TIME".</summary>
    public static int? PlayerPoison(Fighter p, int skill, TechniqueEffect t, int targetResPoison, IGameRandom rng) =>
        SkillCurve(skill, 750, 1000, 1200) * (1000 - targetResPoison) / 1000 > Roll(rng, 1000) ? OrForever(p.Kashikosa / 2 + t.Time) : null;

    /// <summary>P2EPOISONTIME (15) "DAMAGE": dano por tique.</summary>
    public static int PlayerPoisonTick(TechniqueEffect t) => -(t.Hp / 5);

    /// <summary>P2ESLEEPTIME (16), P2EBINDTIME (17) e o "TIME" de 18 e 19 (mesmo código).</summary>
    public static int? PlayerDisable(Fighter p, int skill, TechniqueEffect t, int targetSeishin, IGameRandom rng) =>
        Div(SkillCurve(skill, 550, 800, 1000) * 100, targetSeishin) > Roll(rng, 1000) ? OrForever(p.Kashikosa / 2 + t.Time) : null;

    /// <summary>Redução de SUBAYASA (18) e de TSUYOSA/MAMORI (19): −max(tec − sk/10, −80% do inimigo); tec é negativo na tabela.</summary>
    public static int PlayerStatDown(int techValue, int skill, int enemyValue) => -Math.Max(techValue - skill / 10, -(enemyValue * 80 / 100));

    // ---------- Inimigo → jogador ----------

    static int Factor(Fighter enemy, Fighter player) => Div(3 * enemy.Kashikosa, player.Seishin) + 100 - player.Seishin / 40;

    /// <summary>
    /// E2PSTUNTIME (7). HIPÓTESE: <paramref name="damage"/> = i3 e <paramref name="damageRank"/> = i4 (só faixas 1 e 2
    /// atordoam nos golpes TYPE 0 e 2). <paramref name="mahiteikou"/> vem da armadura 1 (negativo reduz a chance).
    /// </summary>
    public static int? EnemyStun(Fighter enemy, EnemyEffect atk, Fighter player, int mahiteikou, int damage, int damageRank,
        StatusMods mods, IGameRandom rng)
    {
        int m;
        switch (atk.Type)
        {
            case 0 when damageRank is >= 1 and < 3: m = enemy.Mahiritsu - mods.StunDown; break;
            case 1 when atk.Mahiritsu > 0: m = atk.Mahiritsu; break;
            case 2 when damageRank is >= 1 and < 3: m = 0; break;
            default: return null;
        }
        int s = Div(50 * damage, player.MaxHp) + m + mahiteikou;
        return (s + 100) / 2 > Roll(rng, 100) ? OrForever((s + 30) / 4) : null;
    }

    /// <summary>E2PPOISONTIME (8) "TIME": só com KOUKA "doku".</summary>
    public static int? EnemyPoison(EnemyEffect atk, int dokuteikou, IGameRandom rng) =>
        atk.Kouka != "doku" ? null : 15 * atk.KoukaRank + dokuteikou + 30 > Roll(rng, 100) ? dokuteikou / 2 + atk.KoukaTime : null;

    /// <summary>E2PPOISONTIME (8) "DAMAGE": % do HP máximo por tique.</summary>
    public static int EnemyPoisonTick(EnemyEffect atk, Fighter player) => player.MaxHp * atk.KoukaRank / 100;

    /// <summary>E2PSLEEPTIME (9) "sleep", E2PBINDTIME (10) "ashidome" (TYPE 2 pega sempre) e o "TIME" de 11 "freeze" e 12 "powerdown".</summary>
    public static int? EnemyDisable(Fighter enemy, EnemyEffect atk, Fighter player, string kouka, IGameRandom rng)
    {
        if (atk.Kouka != kouka) return null;
        int f = Factor(enemy, player);
        if (kouka == "ashidome" && atk.Type == 2) return atk.KoukaTime;
        int chance = kouka is "freeze" or "powerdown" ? 160 * atk.KoukaRank / 100 * f : 5 * atk.KoukaRank * f;
        return chance > Roll(rng, 10000) ? atk.KoukaTime * f / 100 : null;
    }

    /// <summary>Quanto o golpe tira de SUBAYASA/MEICHURITSU (11) ou TSUYOSA/MAMORI (12): % entre 20 e 80 do valor do jogador.</summary>
    public static int EnemyStatDown(Fighter enemy, EnemyEffect atk, Fighter player, int playerValue)
    {
        int pct = Math.Clamp(atk.KoukaRank + Div(150 * enemy.Kashikosa, player.Seishin) / 100 - player.Seishin / 50, 20, 80);
        return playerValue * pct / 100;
    }
}
