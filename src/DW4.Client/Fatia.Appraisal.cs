using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Guardião da Avaliação (connoisseur.lua → SUBWND GENERATE "CNOS/I&lt;jogador&gt;").
/// FATO (textos mswkPRG1400..1457 da stagML12): "Appraiser", "Select the Item to be appraised.", a avaliação custa 300 BITS por
/// item, sem BIT ela é cancelada e volta ao lobby, e no fim pergunta se quer avaliar outro.
/// DESCONHECIDO: como o jogo marca um item como desconhecido (o inventário do port ainda não tem esse estado). Por isso a janela
/// abre com os textos do jogo, diz que não há item para avaliar e fecha; antes o NPC falava e não abria nada.
/// </summary>
public partial class Fatia
{
    const int AppraisalCost = 300;

    bool DeliverToAppraisal(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "CNOS") return false;
        OpenAppraisal();
        return true;
    }

    void OpenAppraisal() => Menu(
        $"{TextKey("mswkPRG1400", "Appraiser")}\n{TextKey("mswkPRG1450", "Select the Item to be appraised.")}  " +
        $"Nenhum item desconhecido na pasta ({AppraisalCost} BITS por avaliação).",
        [(TextKey("mswkPRG1457", "End appraisal."), CloseSaveWindow)]);
}
