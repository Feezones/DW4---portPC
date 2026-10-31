namespace DW4.Gameplay;

/// <summary>
/// Abrir um B. Pack na loja de cartas (j_shbooster). FATO (0x8039ACC4..0x8039AFB0):
///  · gasta um item da DISKFOLDER com linha 10 da conItem ("B. Pack"); sem pacote a contagem não muda e o sorteio segue;
///  · sorteia 3 cartas; cada uma: r = MT % 1000, percorre a tabela "card" na ordem somando KAKURITSU e fica com a primeira
///    linha em que r &lt; soma (comparação sem sinal, os dois lados × 1000); sem nenhuma, a última linha;
///  · marca CARDLIST[linha] = 1 (carta repetida não conta de novo) e mostra o NAME em TALKFONT01/font02..04 com o ator
///    SQ_CARD06OPEN..SQ_CARD08OPEN;
///  · cor do nome por RARE: 2 → V&lt;0,1,0&gt;, 1 → V&lt;1,1,0&gt;, 0 → V&lt;1,1,1&gt;;
///  · a maior raridade das 3 escolhe 0x7DA (rara), 0x7DB (incomum) ou 0x7DC (comum) num campo do ator (DESCONHECIDO: som ou
///    texto).
/// HIPÓTESE: o gerador é o mesmo igMersenneTwister do combate (mesma chamada virtual +0x60 do sorteio dos drops).
/// </summary>
public static class CardPack
{
    public const int PackRow = 10, CardsPerPack = 3;

    public static int DrawOne(GameTables.Table card, IGameRandom rnd)
    {
        uint r = rnd.Next() % 1000 * 1000;
        uint sum = 0;
        for (int row = 0; row < card.Rows; row++)
        {
            sum += (uint)(card.Int(row, "KAKURITSU") * 1000);
            if (r < sum) return row;
        }
        return card.Rows - 1;
    }

    /// <summary>Gasta um pacote (se houver), sorteia as 3 cartas e marca a coleção. Devolve as linhas sorteadas.</summary>
    public static int[] Open(Inventory inv, GameTables.Table card, byte[] cardList, IGameRandom rnd)
    {
        inv.Remove(new ItemRef("conItem", PackRow));
        var rows = new int[CardsPerPack];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = DrawOne(card, rnd);
            if (rows[i] >= 0 && rows[i] < cardList.Length) cardList[rows[i]] = 1;
        }
        return rows;
    }

    public static (float R, float G, float B) RareColor(int rare) => rare switch { 2 => (0, 1, 0), 1 => (1, 1, 0), _ => (1, 1, 1) };
}
