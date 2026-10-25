using System;
using System.Text.RegularExpressions;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Troca de área pelas saídas (matz01/matz02 → funcStageChange do func.lua, tudo nos scripts originais) e o fade de tela
/// i_fadewn01. Levantamento: docs/gameplay/fase4-objetos-dng1.md ("Saídas e troca de fase").
/// DECISÃO DA FATIA: a janela i_zzstchgwb/i_zzstchgwn e o i_fadewn01 ainda não foram exportados; a janela é uma caixa da
/// Godot com "Sim / Não" e o fade um retângulo preto. Carregar outra fase é a Fase 5: NEXTSTAGE para a própria dng101b1
/// recarrega a fatia na entrada pedida; para outra fase, a tela fica preta com o aviso.
/// </summary>
public partial class Fatia
{
    // FATO (nomes dos scripts): SQ_BLIN30, SQ_BLOUT30, SQ_BLIN60, SQ_BLOUT00 (preto inteiro, "L") e SQ_BHLFIN30/SQ_BHLFOUT30 (meio preto,
    // "HLF"). O padrão antigo exigia "SQ_B" + IN/OUT direto: SQ_BLOUT30 não começava, o ISRUN ficava 0 e o DMchkSD ISRUN "==0" da
    // demo do stag0101 segurava a fila para sempre.
    static readonly Regex FadeSeq = new(@"^SQ_B(L|HLF)(IN|OUT)(\d+)$", RegexOptions.Compiled);

    ColorRect? _fade;
    float _fadeFrom, _fadeTo, _fadeLen, _fadeT;
    string _fadeSeq = "";
    PanelContainer? _choicePanel;
    Label? _choiceLabel;
    string? _choiceText;
    int _choice = -1; // −1: janela fechada; 0 = sim, 1 = não (HIPÓTESE pela ordem dos DMcase)
    bool _choiceMoveHeld;
    Vector2 _menuInput;
    int _nextEntry;
    bool _choiceWindowOpen, _infoWindowOpen;

    bool DeliverToStageChange(BeMessage m)
    {
        switch (m.Class, m.Name, m.Command)
        {
            case ("MODELCTRL", "i_fadewn01", "GENERATE") when m.Text(0) is { } seq:
                StartFade(seq);
                return true;
            case ("MODELCTRL", "i_fadewn01", _) when m.Command.StartsWith("SQ_", StringComparison.Ordinal):
                StartFade(m.Command);
                return true;
            // FATO (funcHealGate): GAMERAM ADDHP "I<jogador>/I<quantia>/I<1>" e ADDMP "I<jogador>/I<quantia>" (a quantia é o
            // máximo: cura tudo). HIPÓTESE: soma com limite no máximo, como o ADDHP/ADDMP do jogo (0x8036F220).
            case ("GAMERAM", _, "ADDHP" or "ADDMP") when _pf != null && m.Arg(0) is { Numbers.Length: > 0 } who && who.Numbers[0] == 0 && m.Arg(1) is { Numbers.Length: > 0 } amount:
                if (m.Command == "ADDHP") _hp = Math.Clamp(_hp + (int)amount.Numbers[0], 0, _pf.MaxHp);
                else _mp = Math.Clamp(_mp + (int)amount.Numbers[0], 0, _pf.MaxMp);
                ScriptLog($"{m.Command}: HP {_hp}/{_pf.MaxHp} MP {_mp}/{_pf.MaxMp}");
                return true;
            case ("SELECTCTRL", "i_zzstchgwn", "GENERATE"):
                _choice = 0;
                _choiceMoveHeld = true; // só vale um movimento que começou depois de abrir (o que levou ao tapete ainda pode estar apertado)
                UpdateChoice();
                return true;
            // Janela de info do Digimon (funcOpenDigimonInfoWin): o first.lua dos campos espera ISRUN "==0" e "!=0" (abrir e fechar)
            // antes do fade; sem resposta a espera passava direto e as esperas do fade travavam a fila (stag0101).
            case ("MODELCTRL", "i_dminfown", "GENERATE"): _infoWindowOpen = true; return true;
            case ("MODELCTRL", "i_dminfown", "SQ_CLOSE"): _infoWindowOpen = false; return true;
            case ("MODELCTRL", "i_zzstchgwb", "GENERATE"):
                _choiceWindowOpen = true; // gerada com atraso de 10 quadros: o script espera ISRUN antes de ler a escolha
                return true;
            case ("MODELCTRL", "i_zzstchgwb", "SQ_CLOSE"):
                _choiceText = null;
                _choiceWindowOpen = false;
                if (_choicePanel != null) _choicePanel.Visible = false;
                return true;
            case ("MODELCTRL", "i_zzstchgwb", _):
                return true;
            // FATO: matz01 manda "STCHGFONT00/font00/I<10>" ("Goblin Pass") na janela de troca.
            case ("TEXTCTL", _, "TEXTSET") when m.RawArgs?.StartsWith("STCHGFONT", StringComparison.Ordinal) == true:
                _choiceText = m.Args.Count > 2 && m.Args[2] is { Kind: 'I', Numbers.Length: > 0 } k ? Text((int)k.Numbers[0]) : null;
                UpdateChoice();
                return true;
            case ("TEXTCTL", _, "SWITCH" or "ALIGN" or "COLOR") when m.RawArgs?.StartsWith("STCHGFONT", StringComparison.Ordinal) == true:
                return true;
            case ("PLAYERPOINT", _, "SETGENERATEPOS"):
                _nextEntry = m.Args.Count > 0 && m.Args[0] is { Kind: 'I', Numbers.Length: > 0 } e ? (int)e.Numbers[0] : 0;
                return true;
            case ("STAGECTL", _, "NEXTSTAGE") when m.Text(0) is { } stage:
                NextStage(stage);
                return true;
            case ("MODELCTRL", null, "ONCTRL"):
                _playerCtrl = true;
                _enemiesFrozen = false;
                return true;
        }
        return false;
    }

    double? QueryStageChange(BeMessage m)
    {
        switch (m.Class, m.Name, m.Command)
        {
            // ISRUN = 1 enquanto o fade anda; ISSEQNAME = 1 na sequência pedida. SQ_STCHANGELP fica "rodando" (laço).
            case ("MODELCTRL", "i_fadewn01", "ISRUN"): return _fade != null && (_fadeT < _fadeLen || _fadeSeq == "SQ_STCHANGELP") ? 1 : 0;
            case ("MODELCTRL", "i_fadewn01", "ISSEQNAME"): return _fadeSeq == m.Text(0) ? 1 : 0;
            // ISEND (comum, Fatia.Recorder.cs) = 1 depois do confirmar; ISSELECTNOW = 1 enquanto escolhe; GETSELECT = a opção.
            case ("MODELCTRL", "i_zzstchgwb", "ISRUN"): return _choiceWindowOpen ? 1 : 0;
            case ("MODELCTRL", "i_dminfown", "ISRUN"): return _infoWindowOpen ? 1 : 0;
            case ("SELECTCTRL", "i_zzstchgwn", "ISSELECTNOW"): return _choice >= 0 && !_selectEnded ? 1 : 0;
            case ("SELECTCTRL", "i_zzstchgwn", "GETSELECT"): return _choice;
            case ("SYSCTL", "HP", "GETHP"): return _pf != null ? _hp : 1;
            // funcHealGate (func.lua do lobby) compara HP/MP com os máximos antes de curar.
            case ("SYSCTL", "HP", "GETMHP"): return _pf?.MaxHp ?? 1;
            case ("SYSCTL", "MP", "GETMP"): return _pf != null ? _mp : 1;
            case ("SYSCTL", "MP", "GETMMP"): return _pf?.MaxMp ?? 1;
        }
        return null;
    }

    void ClearFade()
    {
        _fadeSeq = "";
        _fadeT = _fadeLen = 0;
        if (_fade != null) _fade.Color = new Color(0, 0, 0, 0);
    }

    // Sequências do i_fadewn01 pelo nome (HIPÓTESE): SQ_B[HLF](IN|OUT)n = preto (ou meio preto) entrando/saindo em n quadros;
    // SQ_STCHANGE01 escurece em 30 quadros e fica em SQ_STCHANGELP.
    void StartFade(string seq)
    {
        if (_fade == null)
        {
            var layer = new CanvasLayer { Layer = 3 };
            _fade = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            _fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(_fade);
            AddChild(layer);
        }
        float alpha = _fade.Color.A;
        var match = FadeSeq.Match(seq);
        if (match.Success)
        {
            bool half = match.Groups[1].Value == "HLF";
            float dark = half ? 0.5f : 1f;
            bool fadeIn = match.Groups[2].Value == "IN";
            float len = float.Parse(match.Groups[3].Value);
            // BLIN: do preto para a cena; BHLFIN: do meio preto para a cena; OUT: da cena para o preto (ou meio).
            (_fadeFrom, _fadeTo) = fadeIn ? (half ? alpha : 1f, 0f) : (alpha, dark);
            _fadeLen = Math.Max(1, len);
        }
        else if (seq == "SQ_STCHANGE01") (_fadeFrom, _fadeTo, _fadeLen) = (alpha, 1f, 30);
        else return;
        _fadeT = 0;
        _fadeSeq = seq;
    }

    void TickStageChange(float dt)
    {
        TickListMenu();
        if (_fade != null && _fadeT < _fadeLen)
        {
            _fadeT = Math.Min(_fadeLen, _fadeT + dt * 60);
            _fade.Color = new Color(0, 0, 0, _fadeFrom + (_fadeTo - _fadeFrom) * _fadeT / _fadeLen);
            // HIPÓTESE: no fim, SQ_STCHANGE01 segue para o laço SQ_STCHANGELP e os outros fades voltam a SQ_WAIT (o funcStageChange
            // espera enquanto a sequência ainda é SQ_BHLFIN30).
            if (_fadeT >= _fadeLen) _fadeSeq = _fadeSeq == "SQ_STCHANGE01" ? "SQ_STCHANGELP" : "SQ_WAIT";
        }
        if (_choice >= 0 && _choicePanel?.Visible == true)
        {
            bool move = Math.Abs(_menuInput.X) > 0.5f || Math.Abs(_menuInput.Y) > 0.5f;
            if (move && !_choiceMoveHeld) { _choice = 1 - _choice; UpdateChoice(); }
            _choiceMoveHeld = move;
        }
    }

    void UpdateChoice()
    {
        if (_choicePanel == null)
        {
            var layer = new CanvasLayer { Layer = 4 };
            _choicePanel = new PanelContainer();
            _choicePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
            _choicePanel.Position = new Vector2(-180, -70);
            _choicePanel.CustomMinimumSize = new Vector2(360, 140);
            _choiceLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _choiceLabel.AddThemeFontSizeOverride("font_size", 28);
            _choicePanel.AddChild(_choiceLabel);
            layer.AddChild(_choicePanel);
            AddChild(layer);
        }
        _choicePanel.Visible = _choice >= 0;
        string options = _choice == 0 ? "▶ Sim      Não" : "  Sim    ▶ Não";
        _choiceLabel!.Text = (_choiceText != null ? _choiceText.Replace("\r\n", "\n") + "\n\n" : "") + options;
    }

    void NextStage(string stage)
    {
        if (IsMenuStage(stage)) { MenuStageInPlace(stage); return; }
        ScriptLog($"troca de fase: {stage}, entrada {_nextEntry}");
        _choice = -1;
        if (_choicePanel != null) _choicePanel.Visible = false;
        var stages = OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases";
        if (stage == _stageName || System.IO.Directory.Exists(System.IO.Path.Combine(stages, stage)))
        {
            // Recarrega a cena com a fase e a entrada pedidas (o first.lua decide a demo pela entrada). A GAMERAM continua.
            SaveProgress(stage, _nextEntry);
            OS.SetEnvironment("DW4_FASE", stage);
            OS.SetEnvironment("DW4_ENTRADA", _nextEntry.ToString());
            OS.SetEnvironment("DW4_INICIO", "");
            _leaving = true; // o resto deste quadro e os próximos não tocam mais na árvore
            GetTree().ReloadCurrentScene();
            return;
        }
        ShowMessage($"Troca para {stage} (entrada {_nextEntry})\nEssa fase ainda não foi exportada (dw4 fase-exportar). Esc sai.");
    }
}
