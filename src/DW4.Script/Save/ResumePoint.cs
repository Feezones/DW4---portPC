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

    public static (string Stage, int Entry) Adjust(GameRam ram, string stage, int entry)
    {
        if (!stage.Equals(Tutorial, StringComparison.OrdinalIgnoreCase)) return (stage, entry);
        ram.SetFlag(TutorialStarted, false);
        return (stage, 0);
    }
}
