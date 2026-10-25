namespace DW4.Gameplay;

/// <summary>
/// Atributos-base do jogador por nível e experiência para o próximo nível, transcritos do RECALCPLAYER do main.dol
/// (0x8036FDE8, curvas em 0x8036F808 e 0x8036FB80, NEXTEXP em 0x80370DBC), lidos por desmontagem estática em 2026-10-24.
/// FATO: tabelas e contas. As somas de equipamento, chips e efeitos de estado ficam fora daqui.
/// </summary>
public static class PlayerGrowth
{
    public const int MaxLevel = 999;
    // Faixas de nível: i = primeiro com LEVEL ≤ Lv[i]; L0 = nível inicial da faixa (0x804EAD24 e 0x804EAD74).
    static readonly int[] Lv = [5, 10, 20, 30, 50, 70, 110, 150, 230, 999];
    static readonly int[] L0 = [1, 5, 10, 20, 30, 50, 70, 110, 150, 230];
    // MHP/MMP (0x804EAD4C e 0x804EAD9C) e atributos (0x804EADC4 e 0x804EADEC): acumulado e inclinação da faixa.
    static readonly int[] HpA = [0, 40, 140, 640, 1440, 3840, 7440, 17840, 25040, 34640];
    static readonly int[] HpB = [10, 20, 50, 80, 120, 180, 260, 180, 120, 80];
    static readonly int[] StatC = [0, 40, 85, 165, 235, 355, 455, 615, 735, 895];
    static readonly int[] StatD = [10, 9, 8, 7, 6, 5, 4, 3, 2, 1];

    static int Band(int level)
    {
        for (int i = 0; i < Lv.Length; i++) if (level <= Lv[i]) return i;
        return Lv.Length - 1;
    }

    /// <summary>MHP ou MMP: INI + UPCNT + GROW·(A + B·(L − L0))/100, limitado a 99999.</summary>
    public static int MaxHpMp(int level, int ini, int grow, int upCount = 0)
    {
        int i = Band(level);
        return Math.Clamp(ini + upCount + grow * (HpA[i] + HpB[i] * (level - L0[i])) / 100, 0, 99999);
    }

    /// <summary>
    /// TSUYOSA (STR), MAMORI (AC), KASHIKOSA (INT), SEISHIN (MND), SUBAYASA (DEX): INI + UPCNT + GROW·(C + D·(L − L0))/100,
    /// limitado a 9999. O limite inferior 1 vale para o total com equipamento.
    /// </summary>
    public static int Stat(int level, int ini, int grow, int upCount = 0)
    {
        int i = Band(level);
        return Math.Clamp(ini + upCount + grow * (StatC[i] + StatD[i] * (level - L0[i])) / 100, 0, 9999);
    }

    /// <summary>EXP total acumulado para passar do nível <paramref name="level"/>: L²·(L + 140) + 200·L; 0 no 999.</summary>
    public static long NextExp(int level) => level >= MaxLevel ? 0 : (long)level * level * (level + 140) + 200L * level;
}
