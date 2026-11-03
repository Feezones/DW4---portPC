using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Script.Save;

/// <summary>
/// Guarda o progresso da história nas flags permanentes da dificuldade, como o original faz ao gravar.
/// FATO (start.lua da stagIS12, ndmw.afs #811, sha256 E7B0CFCA…, a tela de gravar): copia as flags de trabalho para o bloco
/// permanente da dificuldade atual (Normal 700.., Hard 741.. e 7..12/74../87, Very Hard 771.. e 13..18/78../88) e liga as flags
/// 518+/530+/542+ pelos itens da FLAGFOLDER. A gameStart.lua da stagIS10 faz o caminho inverso a cada começo de jogo (mesma
/// tabela, conferida pela listagem das duas), então quem grava sem passar por aqui perde o enredo ao entrar de novo: as flags de
/// trabalho (FLG 2 = já falou com a Ofanimon etc.) voltam zeradas e o jogo recomeça do tutorial.
/// O port grava sem a stagIS12 (pontos de save, troca de fase, saída), por isso <see cref="Commit"/> roda em toda gravação.
/// </summary>
public static class PermanentFlags
{
    /// <summary>(destino permanente, origem de trabalho, quantidade) por dificuldade, na ordem do start.lua.</summary>
    static readonly (int Permanent, int Work, int Count)[][] Blocks =
    [
        [(700, 1, 6), (706, 19, 2), (708, 67, 4), (712, 83, 1), (713, 86, 1), (714, 89, 11), (725, 320, 8), (733, 329, 5), (738, 335, 1),
         (801, 49, 1), (804, 383, 1), (807, 82, 1), (810, 334, 1)],
        [(7, 1, 6), (741, 19, 2), (74, 67, 4), (743, 83, 1), (87, 86, 1), (744, 89, 11), (755, 320, 8), (763, 329, 5), (768, 335, 1),
         (802, 49, 1), (805, 383, 1), (808, 82, 1), (811, 334, 1)],
        [(13, 1, 6), (771, 19, 2), (78, 67, 4), (773, 83, 1), (88, 86, 1), (774, 89, 11), (785, 320, 8), (793, 329, 5), (798, 335, 1),
         (803, 49, 1), (806, 383, 1), (809, 82, 1), (812, 334, 1)],
    ];

    /// <summary>FATO (start.lua da stagIS12): FLG base+i ← SYSctlFlagFolder("IS", i), i = 0..11.</summary>
    static readonly int[] FolderBase = [518, 530, 542];
    const int FolderItems = 12;

    /// <summary>Copia as flags de trabalho para as permanentes da dificuldade da GAMERAM (o start.lua da stagIS12).</summary>
    public static void Commit(GameRam ram)
    {
        int d = ram.Difficulty;
        if (d < 0 || d >= Blocks.Length) return; // o start.lua só mostra um MsgBox de erro
        var items = ram.Players[0].Items;
        for (int i = 0; i < FolderItems; i++) ram.SetFlag(FolderBase[d] + i, items.Has(new ItemRef("impItem", i)));
        foreach (var (permanent, work, count) in Blocks[d])
            for (int i = 0; i < count; i++) ram.SetFlag(permanent + i, ram.FlagOn(work + i) == 1);
    }

    /// <summary>
    /// Vaga gravada antes desta correção: as permanentes ficaram zeradas, mas as de trabalho ainda têm o enredo. Copia para as
    /// permanentes da dificuldade em que a vaga foi gravada (antes de trocar a dificuldade e de rodar a gameStart.lua).
    /// Numa vaga já gravada por aqui não muda nada (as duas já são iguais).
    /// </summary>
    public static void Repair(CharacterSave save)
    {
        var ram = new GameRam();
        save.Apply(ram, 0);
        Commit(ram);
        save.Flags = Convert.ToBase64String(ram.Flags);
    }
}
