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

    /// <summary>
    /// Grava na vaga (no servidor) com o lugar onde o personagem vai voltar; sem vaga, só guarda na GAMERAM. A tarefa diz se o
    /// servidor confirmou (falha também aparece na tela por Session.SaveFailed).
    /// </summary>
    System.Threading.Tasks.Task<bool> SaveProgress(string stage, int entry)
    {
        StoreProgress();
        return Session.SaveProgress(Ram, stage, entry);
    }

    // HIPÓTESE do port: ao sair, volta pela entrada por onde entrou nesta fase.
    int CurrentEntry => int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0;
    System.Threading.Tasks.Task<bool> SaveProgress() => SaveProgress(_stageName, CurrentEntry);

    // Esc: com vaga, grava no servidor e volta ao menu (só depois da confirmação); aberto direto (DW4_FASE), fecha.
    void LeaveGame()
    {
        if (!Session.Active) { GetTree().Quit(); return; }
        StoreProgress();
        _leaving = true;
        Toast("Gravando no servidor...", 20);
        Session.ReturnToMenu(GetTree(), Ram, _stageName, CurrentEntry, failed: why =>
        {
            if (!IsInsideTree()) return;
            _leaving = false;
            HideToast();
            SaveWindow($"Não deu para gravar no servidor:\n{why}",
            [
                ("Tentar de novo", () => { CloseSaveWindow(); LeaveGame(); }),
                ("Sair sem gravar", () => { CloseSaveWindow(); _leaving = true; Session.Leave(GetTree()); }),
                ("Continuar jogando", CloseSaveWindow),
            ]);
        });
    }

    // Fechar a janela no meio do jogo: grava no servidor antes de fechar (a Godot só fecha sozinha fora de uma vaga).
    bool _closing;
    async void CloseRequested()
    {
        if (_closing) return;
        _closing = true;
        Toast("Gravando no servidor...", 20);
        bool ok = await SaveProgress();
        if (ok || !IsInsideTree()) { GetTree().Quit(); return; }
        _closing = false;
        HideToast();
        SaveWindow($"Não deu para gravar no servidor:\n{Session.LastError}\nFechar mesmo assim?",
        [
            ("Tentar de novo", () => { CloseSaveWindow(); CloseRequested(); }),
            ("Fechar sem gravar", () => GetTree().Quit()),
            ("Continuar jogando", CloseSaveWindow),
        ]);
    }

    void OnSaveFailed(string why)
    {
        if (IsInsideTree() && !_closing) Toast($"Não gravou no servidor: {why}", 4);
    }

    // A conta entrou em outro aparelho: volta ao menu sem gravar (o outro aparelho vale) e o menu explica.
    void OnKicked()
    {
        if (!IsInsideTree() || _leaving) return;
        _leaving = true;
        Session.MenuNotice = DW4.Net.Protocol.KickedReason + " Este aparelho voltou ao menu sem gravar, para não apagar o progresso do outro.";
        Callable.From(() => Session.Leave(GetTree())).CallDeferred();
    }
}
