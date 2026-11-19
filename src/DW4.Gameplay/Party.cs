namespace DW4.Gameplay;

/// <summary>Chip de backup deixado por um jogador caído: BIT e EXP guardados no chip (BKCHIPDATA[2p], [2p + 1]).</summary>
public sealed record BackupChip(int Bit, long Exp);

/// <summary>
/// Regras de grupo do original (até 4 jogadores), lidas por desmontagem estática em 2026-10-25
/// (docs/gameplay/fase4-levantamento-grupo.md). Base para a party online: progresso (FLAG, COUNTER, DIFFICULTY) é do
/// grupo; nível, EXP, BIT, skills e inventário são de cada jogador. EXP em grupo: <see cref="DamageFormula.ExpShare"/>.
/// </summary>
public static class Party
{
    public const int MaxPlayers = 4;

    /// <summary>
    /// Morte (BKCHIPBITEXP, 0x80377F70): todo o BIT vai para o chip; fora dos níveis 1 e 999, perde 20% da faixa do nível
    /// anterior, sem descer de nível: perda = (NE(L−1) − NE(L−2))/5 e EXP nova = max(EXP − perda, NE(L−1)).
    /// Devolve o chip e a EXP que fica com o jogador.
    /// </summary>
    public static (BackupChip Chip, long Exp) Die(int level, long exp, int bit)
    {
        if (level == 1 || level == PlayerGrowth.MaxLevel) return (new BackupChip(bit, 0), exp);
        long floor = PlayerGrowth.NextExp(level - 1);
        long loss = (floor - PlayerGrowth.NextExp(level - 2)) / 5;
        long kept = Math.Max(exp - loss, floor);
        return (new BackupChip(bit, exp - kept), kept);
    }

    /// <summary>Quantidade de inimigos do gerador para o grupo: PlayerData[nº de jogadores − 1] (0x802FD16C).</summary>
    public static int PlayerDataIndex(int players) => Math.Clamp(players, 1, MaxPlayers) - 1;
}
