namespace DW4.Gameplay.Online;

/// <summary>Um membro do grupo como as recompensas o veem: está na mesma fase, HP e nível atuais.</summary>
public sealed record PartyMemberState(string Name, bool InStage, int Hp, int Level);

/// <summary>
/// Divisão de EXP e BIT e escala de dificuldade no grupo online.
///
/// FATO (original, docs/gameplay/fase4-levantamento-grupo.md):
///  · NINZU = jogadores na partida (1..4). O gerador usa PlayerData[NINZU − 1]: mais jogadores = mais inimigos no total
///    (_geneCount), mesmo máximo vivo; HP e força dos inimigos não mudam. Isto já roda no port pelo GameRam.Ninzu.
///  · EXP (P2EEXPUP): cada jogador com HP ≠ 0 e nível ≠ 999 recebe <see cref="DamageFormula.ExpShare"/>; quem matou recebe a
///    parte maior. Com 4 jogadores quem mata leva 80% do que levaria sozinho e os outros 40%.
///
/// DECISÃO DO PORT (pedido do responsável, 2026-11-02): no original o BIT vai inteiro para quem pega. Aqui a quantia (calculada
/// como no original, pelo nível e BITRITSU de quem pegou) é dividida em partes iguais entre os membros vivos na mesma fase; o
/// resto da divisão fica com quem pegou.
/// </summary>
public static class PartyRewards
{
    static bool Active(PartyMemberState m) => m.InStage && m.Hp > 0;

    /// <summary>NINZU do grupo: membros na mesma fase (vivos ou caídos), de 1 a 4.</summary>
    public static int Ninzu(IEnumerable<PartyMemberState> members) =>
        Math.Clamp(members.Count(m => m.InStage), 1, Party.MaxPlayers);

    /// <summary>EXP de cada membro pela morte de um inimigo (0 para quem está fora da fase, caído ou no nível máximo).</summary>
    public static IReadOnlyDictionary<string, int> Exp(IReadOnlyList<PartyMemberState> members, string killer, int enemyExp, int expRitsu = 0)
    {
        int ninzu = Ninzu(members);
        var res = new Dictionary<string, int>(PartyGroup.Names);
        foreach (var m in members)
            res[m.Name] = Active(m) && m.Level != PlayerGrowth.MaxLevel
                ? DamageFormula.ExpShare(enemyExp, expRitsu, ninzu, PartyGroup.Names.Equals(m.Name, killer))
                : 0;
        return res;
    }

    /// <summary>BIT pego por <paramref name="picker"/>, dividido entre os membros vivos na fase (o resto fica com quem pegou).</summary>
    public static IReadOnlyDictionary<string, int> Bit(IReadOnlyList<PartyMemberState> members, string picker, int amount)
    {
        var res = new Dictionary<string, int>(PartyGroup.Names);
        foreach (var m in members) res[m.Name] = 0;
        var share = members.Where(Active).Select(m => m.Name).ToList();
        if (!share.Contains(picker, PartyGroup.Names)) share.Add(picker);
        int each = amount / share.Count, rest = amount - each * share.Count;
        foreach (var n in share) res[n] = each;
        res[picker] = each + rest;
        return res;
    }
}
