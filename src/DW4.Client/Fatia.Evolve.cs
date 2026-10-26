using System;
using System.Globalization;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Troca de forma (digievolução). FATO (docs/gameplay/fase4-levantamento-evolucao.md):
///  · a Digi-lab do lobby abre o menu (digilabshop → SUBWND GENERATE "LABO/I&lt;jogador&gt;", beNDMWShopCtrlEvolve) com as formas
///    liberadas e o nível de cada;
///  · a escolha (0x8039CA4C) faz CURRENT2BASE e BASE2CURRENT (cada forma com nível, EXP, skills e UPCNT próprios), tira armaduras e
///    chips e re-escolhe a arma. Sem custo nem condição além de a forma estar liberada.
/// PORT: janela da Godot com a lista; o modelo novo entra recarregando a fase no mesmo ponto. A arma da fatia continua a
/// w_sword01 (DECISÃO DA FATIA); só as armaduras e os chips saem.
/// Modelos das formas: &lt;fatia&gt;\formas\&lt;MODEL_NAME&gt; (dw4 igb-gltf/igb-seq ndmw.afs 850..865); o Guilmon também aceita a pasta antiga
/// &lt;fatia&gt;\guilmon.
/// </summary>
public partial class Fatia
{
    string PlayerFolder => PlayerModel == "p_girumon" && System.IO.Directory.Exists(System.IO.Path.Combine(_common, "guilmon"))
        ? "guilmon" : System.IO.Path.Combine("formas", PlayerModel);

    string FormName => _db != null && _shinkaRow >= 0 ? _db["shinka"].Text(_shinkaRow, "NAME") : PlayerModel;
    // Nome do modelo sem "p_": o LABEL/SQ_<nome>OPEN dos rostos do HUD e do menu de status (girumon, agumon, vmon…).
    string FaceKey => PlayerModel.StartsWith("p_", System.StringComparison.Ordinal) ? PlayerModel[2..] : PlayerModel;

    /// <summary>A pasta do modelo da forma existe na pasta comum? (o menu inicial só deixa criar o que o cliente desenha).</summary>
    public static bool HasPlayerModel(string commonDir, string model) =>
        System.IO.Directory.Exists(System.IO.Path.Combine(commonDir, "formas", model))
        || model == "p_girumon" && System.IO.Directory.Exists(System.IO.Path.Combine(commonDir, "guilmon"));

    bool DeliverToEvolve(BeMessage m)
    {
        if (m.Class != "SUBWND" || m.Command != "GENERATE" || m.Arg(0)?.Text != "LABO") return false;
        OpenEvolve();
        return true;
    }

    void OpenEvolve()
    {
        if (_db == null) { ScriptLog("SUBWND LABO: sem tabelas, troca de forma desligada"); return; }
        StoreProgress();
        var forms = Ram.Players[0].Forms;
        var shinka = _db["shinka"];
        var options = Enumerable.Range(0, Evolution.Forms).Where(d => forms[d].Unlocked).Select(d =>
        {
            string model = shinka.Text(d, "MODEL_NAME");
            bool here = d == forms.Current, drawable = HasPlayerModel(_common, model);
            string label = $"{shinka.Text(d, "NAME")}  nv {forms[d].Level}" + (here ? "  (atual)" : drawable ? "" : "  (sem modelo exportado)");
            return (label, (Action)(() => { if (!here && drawable) Evolve(d, model); else CloseSaveWindow(); }));
        }).ToList();
        options.Add(("Voltar", CloseSaveWindow));
        SaveWindow("Digievolução", options.ToArray());
    }

    void Evolve(int digimonNo, string model)
    {
        var forms = Ram.Players[0].Forms;
        if (!forms.Switch(digimonNo)) { CloseSaveWindow(); return; }
        PlayerModel = model;
        // FATO (0x8039CA4C): tira armaduras e chips e re-escolhe a arma (WEAPONEQUIPSTRONG: a de maior TSUYOSA que a forma nova pode
        // usar). A forma nova traz nível, EXP e skills dela (LoadProgress na recarga). Sem arma usável, fica a da fatia (Recalc).
        var weapons = _inventory.Devices.Where(d => d.Table == "weapon").Select(d => d.Row);
        int best = _db != null ? Evolution.StrongestWeapon(_db, weapons, digimonNo, forms[digimonNo].Skill) : -1;
        _equip = best >= 0 ? new Equipment(best) : null;
        Ram.Players[0].Equipment = _equip;
        ScriptLog($"digievolução: {model} (DIGIMONNO {digimonNo})");
        var p = _player.GlobalPosition;
        int entry = int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0;
        try { Session.SaveProgress(Ram, _stageName, entry); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { GD.PrintErr($"[save] não gravou: {ex.Message}"); }
        OS.SetEnvironment("DW4_INICIO", string.Create(CultureInfo.InvariantCulture, $"{p.X},{p.Y + 1},{p.Z}"));
        _leaving = true;
        GetTree().ReloadCurrentScene();
    }
}
