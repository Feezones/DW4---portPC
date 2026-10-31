using System;
using Godot;

namespace DW4.Client;

/// <summary>
/// Progresso do jogador ligado à vaga escolhida no menu (Session). O nível, a EXP e as skills da forma atual ficam em campos
/// da cena (_level, _exp, _skill) e voltam para a forma na GAMERAM (Evolution) antes de trocar de fase ou gravar; assim a troca
/// de fase também não zera o nível.
/// </summary>
public partial class Fatia
{
    // Forma atual na GAMERAM → campos da cena (chamado no LoadCombat, depois do GAMEDATAINIT).
    void LoadProgress()
    {
        var forms = Ram.Players[0].Forms;
        if (forms.Current != _shinkaRow || _shinkaRow < 0) return;
        var f = forms[_shinkaRow];
        _level = Math.Max(1, f.Level);
        _exp = f.Exp;
        Array.Copy(f.Skill, _skill, Math.Min(f.Skill.Length, _skill.Length));
    }

    // Campos da cena → forma atual na GAMERAM.
    void StoreProgress()
    {
        if (_shinkaRow < 0) return;
        var forms = Ram.Players[0].Forms;
        if (forms.Current < 0) forms.Start(_shinkaRow);
        if (forms.Current != _shinkaRow) return;
        var f = forms[_shinkaRow];
        f.Level = _level;
        f.Exp = _exp;
        Array.Copy(_skill, f.Skill, Math.Min(f.Skill.Length, _skill.Length));
        if (_equip != null) Ram.Players[0].Equipment = _equip;
    }

    /// <summary>Grava na vaga com o lugar onde o personagem vai voltar (sem vaga, só guarda na GAMERAM).</summary>
    void SaveProgress(string stage, int entry)
    {
        StoreProgress();
        try { Session.SaveProgress(Ram, stage, entry); }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException) { GD.PrintErr($"[save] não gravou: {e.Message}"); }
    }

    // HIPÓTESE do port: ao sair, volta pela entrada por onde entrou nesta fase.
    void SaveProgress() => SaveProgress(_stageName, int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0);

    // Esc: com vaga, grava e volta ao menu; aberto direto (DW4_FASE), fecha.
    void LeaveGame()
    {
        if (!Session.Active) { GetTree().Quit(); return; }
        StoreProgress();
        _leaving = true;
        try { Session.ReturnToMenu(GetTree(), Ram, _stageName, int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PrintErr($"[save] não gravou: {ex.Message}"); GetTree().ChangeSceneToFile(Session.MenuScene); }
    }
}
