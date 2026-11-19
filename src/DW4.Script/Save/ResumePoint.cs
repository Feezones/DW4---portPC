using DW4.Script.Be;

namespace DW4.Script.Save;

/// <summary>
/// Ajusta o ponto de volta de uma vaga antes de entrar (o original só grava nos pontos de save do lobby; o port grava ao trocar de
/// fase e ao sair, então uma vaga pode ter sido gravada no meio do tutorial).
/// FATO (stag0001): o first.lua só monta a abertura do tutorial (OFCTRL, mensagens, PADINPUT ENABLE, MATCTRL RESTART) com a flag
/// 503 desligada; com ela ligada pula tudo e deixa o controle travado (o start.lua já fez PADINPUT DISABLE, MATCTRL PAUSE e
/// PLAYER SQ_HIDE). O restart.lua do próprio tutorial volta ao começo com FLGset(503, 0) e NEXTSTAGE stag0001 na entrada 0, e o
/// mate00.lua do lobby só manda para a stag0001 com a 503 desligada. Por isso uma vaga gravada na stag0001 volta ao começo do
/// tutorial do mesmo jeito que o restart.lua.
/// </summary>
public static class ResumePoint
{
    public const string Tutorial = "stag0001";
    public const int TutorialStarted = 503;

    /// <summary>COUNTER 7: o gene03.lua (3ª onda do tutorial) soma 1 a cada inimigo derrotado; nada mais no tutorial zera.</summary>
    public const int TutorialKills = 7;

    /// <summary>
    /// Vagas gravadas até a 0.6.8: o port rodava o restart.lua ("Treinar de novo") junto com o "Ir para o Death Valley" no fim do
    /// tutorial (DemoManager.BuildingCase), o FLGset(503, 0) dele apagava a 503 e o mate00.lua do lobby mandava de novo ao tutorial.
    /// Quem já chegou à 3ª onda (COUNTER 7 &gt; 0) fez o tutorial: volta a 503. No original a 503 só fica desligada com a COUNTER 7
    /// ligada entre o restart.lua e o first.lua seguinte, que liga a 503 de novo (e o tutorial não grava).
    /// </summary>
    public static bool RepairTutorialDone(GameRam ram)
    {
        if (ram.FlagOn(TutorialStarted) == 1 || ram.Counter(TutorialKills) <= 0) return false;
        ram.SetFlag(TutorialStarted, true);
        return true;
    }

    public static (string Stage, int Entry) Adjust(GameRam ram, string stage, int entry)
    {
        if (!stage.Equals(Tutorial, StringComparison.OrdinalIgnoreCase)) return (stage, entry);
        ram.SetFlag(TutorialStarted, false);
        return (stage, 0);
    }
}
