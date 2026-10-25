using System;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Menu de destinos do teleporte do lobby (mate00.lua → função local com trios fase/entrada/texto). FATO pelo script:
/// TEXTCTL TEXTSET "STCHGFONT01/font0k/I&lt;t&gt;" para cada opção k = 1..n, SELECTCTRL i_zzstchgcur GENERATE "I&lt;n&gt;", espera
/// ISEND/ISSELECTNOW, e DMswtSD GETSELECT com DMcase 0..n−1 = NEXTSTAGE da opção e DMcase 4 = desistir (volta o fade e o
/// controle). HIPÓTESE: desistir devolve 4. DECISÃO DA FATIA: o i_zzstchgcur ainda não foi exportado; é uma caixa da Godot.
/// </summary>
public partial class Fatia
{
    // FATO (mate00.lua): o caso de desistir é sempre o DMcase 4, qualquer que seja o número de opções.
    const int ListCancel = 4;

    readonly string?[] _listTexts = new string?[6];
    int _listCount, _listChoice = -1, _listResult;
    // Menu aberto: "i_zzstchgcur" (teleporte, desistir = 4) ou "i_messcr" (escolha da conversa, funcChoiceMessage).
    string _listOwner = "";
    bool _listCancellable;
    bool _listMoveHeld;
    PanelContainer? _listPanel;
    Label? _listLabel;

    bool DeliverToListMenu(BeMessage m)
    {
        switch (m.Class, m.Name, m.Command)
        {
            // FATO (funcChoiceMessage, bML01009.lua): TEXTSET "TALKFONT02/font0k/I<t>" para cada opção k = 1..n e depois
            // SELECTCTRL i_messcr GENERATE "NOCANCEL/I<n>/V<x,y,z>"; o script lê GETSELECT (0..n−1). Os textos também vão para a
            // janela de conversa (não consumidos aqui).
            case ("TEXTCTL", _, "TEXTSET") when m.RawArgs?.StartsWith("TALKFONT02/font0", StringComparison.Ordinal) == true:
                if (m.RawArgs.Length > 16 && m.RawArgs[16] - (int)'0' is var o && o >= 1 && o < _listTexts.Length)
                    _listTexts[o] = m.Args.Count > 2 && m.Args[2] is { Kind: 'I', Numbers.Length: > 0 } ot ? Text((int)ot.Numbers[0]) : null;
                return false;
            case ("SELECTCTRL", "i_messcr", "GENERATE"):
                _listOwner = "i_messcr";
                _listCancellable = false; // HIPÓTESE: sem "NOCANCEL" haveria desistir, valor não lido
                _listCount = Math.Clamp(m.Arg(1) is { Numbers.Length: > 0 } mn ? (int)mn.Numbers[0] : 2, 1, _listTexts.Length - 1);
                _listChoice = 0;
                _listResult = 0;
                _listMoveHeld = true;
                UpdateList();
                return true;
            case ("SELECTCTRL", "i_zzstchgcur", "GENERATE"):
                _listOwner = "i_zzstchgcur";
                _listCancellable = true;
                _listCount = Math.Clamp(m.Arg(0) is { Numbers.Length: > 0 } n ? (int)n.Numbers[0] : 1, 1, _listTexts.Length - 1);
                _listChoice = 0;
                _listResult = 0;
                _listMoveHeld = true; // o movimento que levou ao teleporte ainda pode estar apertado
                UpdateList();
                return true;
            // "STCHGFONT01/font0k/I<t>": texto da opção k (1..n).
            case ("TEXTCTL", _, "TEXTSET") when m.RawArgs?.StartsWith("STCHGFONT01/font0", StringComparison.Ordinal) == true:
                if (m.RawArgs.Length > 17 && char.IsDigit(m.RawArgs[17]) && m.RawArgs[17] - '0' is var k && k < _listTexts.Length)
                    _listTexts[k] = m.Args.Count > 2 && m.Args[2] is { Kind: 'I', Numbers.Length: > 0 } t ? Text((int)t.Numbers[0]) : null;
                UpdateList();
                return true;
        }
        return false;
    }

    double? QueryListMenu(BeMessage m) => (m.Class, m.Name, m.Command) switch
    {
        ("SELECTCTRL", "i_zzstchgcur" or "i_messcr", "ISSELECTNOW") => _listChoice >= 0 && !_selectEnded ? 1 : 0,
        ("SELECTCTRL", "i_zzstchgcur" or "i_messcr", "GETSELECT") => _listResult,
        _ => null,
    };

    void TickListMenu()
    {
        if (_listChoice < 0) return;
        // Desistir: K / Backspace / botão B (o mesmo que fecha o menu de status).
        if (_listCancellable && (Input.IsKeyPressed(Key.K) || Input.IsKeyPressed(Key.Backspace) || Input.IsJoyButtonPressed(0, JoyButton.B)))
        {
            _listResult = ListCancel;
            _selectEnded = true;
        }
        if (_selectEnded)
        {
            if (_listResult != ListCancel) _listResult = _listChoice;
            _listChoice = -1;
            Array.Clear(_listTexts);
            UpdateList();
            return;
        }
        bool move = Math.Abs(_menuInput.Y) > 0.5f;
        if (move && !_listMoveHeld)
        {
            _listChoice = (_listChoice + (_menuInput.Y > 0 ? 1 : _listCount - 1)) % _listCount;
            UpdateList();
        }
        _listMoveHeld = move;
    }

    void UpdateList()
    {
        if (_listPanel == null)
        {
            var layer = new CanvasLayer { Layer = 4 };
            _listPanel = new PanelContainer();
            _listPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
            _listPanel.Position = new Vector2(-200, -110);
            _listPanel.CustomMinimumSize = new Vector2(400, 220);
            _listLabel = new Label { VerticalAlignment = VerticalAlignment.Center };
            _listLabel.AddThemeFontSizeOverride("font_size", 26);
            _listPanel.AddChild(_listLabel);
            layer.AddChild(_listPanel);
            AddChild(layer);
        }
        _listPanel.Visible = _listChoice >= 0;
        if (_listChoice < 0) return;
        var lines = new System.Text.StringBuilder();
        for (int i = 0; i < _listCount; i++)
            lines.AppendLine($"{(i == _listChoice ? "▶ " : "   ")}{(_listTexts[i + 1] ?? "?").Replace("\r\n", " ")}");
        _listLabel!.Text = lines.ToString().TrimEnd();
    }
}
