namespace DW4.Gameplay;

/// <summary>
/// Aumentar atributo do Digimon com X-data ("Raise Digimon parameter", janela XDATA). FATO (0x803947B8..0x803949F0):
///  · atributo escolhido 0..6 = TSUYOSA, MAMORI, KASHIKOSA, SEISHIN, SUBAYASA, MHP, MMP (o valor atual do registro do jogador);
///  · custo = (valor − 45) / 5 arredondado para cima (divisão inteira e +1 se sobra resto), no máximo 400; abaixo de 0, 1;
///  · XDATA −= custo e UPCNT[k] += 1, com k = 2, 3, 4, 5, 6, 0, 1 (o RECALCPLAYER soma o UPCNT no atributo).
/// HIPÓTESES: o valor atual é o do RECALC (com equipamento); a condição de "já está no máximo" (texto mswkPRG1162) não foi lida.
/// </summary>
public static class XDataStatus
{
    public static readonly string[] Columns = ["TSUYOSA", "MAMORI", "KASHIKOSA", "SEISHIN", "SUBAYASA", "MHP", "MMP"];
    static readonly int[] UpCountIndex = [2, 3, 4, 5, 6, 0, 1];

    public static int Cost(int value)
    {
        int x = value - 45;
        int c = x / 5 + (x % 5 != 0 ? 1 : 0);
        return c > 400 ? 400 : c < 0 ? 1 : c;
    }

    public static int UpCountSlot(int param) => UpCountIndex[param];

    /// <summary>Gasta o X-data e sobe o UPCNT do atributo; false sem X-data suficiente.</summary>
    public static bool Raise(Inventory inv, int[] upCount, int param, int value)
    {
        if (param < 0 || param >= Columns.Length) return false;
        int cost = Cost(value);
        if (inv.XData < cost) return false;
        inv.XData -= cost;
        upCount[UpCountIndex[param]]++;
        return true;
    }
}
