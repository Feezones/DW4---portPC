using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// stagIS13 (o callshop.lua do lobby faz NEXTSTAGE "stagIS13"). FATO: o first.lua só manda LOADCTRL2 NULL GENERATE e quem conduz
/// é o motor (beNDMWLoadCtrl2 / beNDMWLoadIntf2*: SelSlot, MakeChr, NameSel, Diffselect, MesWin). Os textos da fase dão o fluxo de
/// gravar: "Do you want to save?" (Save. / Do not save.) e "Return to the lobby?" (Return to the lobby. / End the game.). A
/// gatedisc.lua da fase volta ao lobby com SETGENERATEPOS I&lt;3&gt; e NEXTSTAGE "stagML11".
/// PORT: criar e chamar Digimon ficam no menu inicial (vagas da conta, MainMenu); aqui fica só gravar e sair, numa janela da
/// Godot com os textos do jogo. "End the game" volta ao menu inicial. Gravar põe o personagem no lobby, entrada 3, onde ele
/// apareceria ao chamar o Digimon pela stagIS13.
/// </summary>
public partial class Fatia
{
    const string LobbyStage = "stagML11";
    const int LobbyEntry = 3;

    CanvasLayer? _saveLayer;
    Dictionary<string, string>? _textByKey;

    bool DeliverToSavePoint(BeMessage m)
    {
        if (m.Class != "LOADCTRL2" || m.Command != "GENERATE") return false;
        AskSave();
        return true;
    }

    string TextKey(string key, string fallback)
    {
        if (_textByKey == null)
        {
            _textByKey = [];
            var file = Path.Combine(_dir, "textos", "textos.json");
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var t in doc.RootElement.GetProperty("texts").EnumerateArray())
                    if (t.TryGetProperty("key", out var k) && k.GetString() is { } ks) _textByKey[ks] = t.GetProperty("text").GetString() ?? "";
            }
        }
        return _textByKey.TryGetValue(key, out var s) && s.Trim().Length > 0 ? s.Trim() : fallback;
    }

    void AskSave() => SaveWindow(TextKey("mswkMem0200", "Do you want to save?"),
    [
        (TextKey("mswkMem0201", "Save."), () =>
        {
            SaveProgress(LobbyStage, LobbyEntry);
            SaveWindow(Session.Active ? TextKey("mswkMem0216", "Digimon was saved.") : "Sem vaga escolhida no menu: nada foi gravado.",
                [("OK", AskLeave)]);
        }),
        (TextKey("mswkMem0202", "Do not save."), AskLeave),
    ]);

    void AskLeave() => SaveWindow(TextKey("mswkMem0221", "Return to the lobby?"),
    [
        (TextKey("mswkMem0222", "Return to the lobby."), () => { CloseSaveWindow(); _nextEntry = LobbyEntry; NextStage(LobbyStage); }),
        (TextKey("mswkMem0223", "End the game."), () =>
        {
            CloseSaveWindow();
            if (!Session.Active) { GetTree().Quit(); return; }
            _leaving = true;
            Session.ReturnToMenu(GetTree(), Ram, LobbyStage, LobbyEntry, save: false);
        }),
    ]);

    // Janela com as escolhas; trava o controle do jogador enquanto está aberta.
    void SaveWindow(string text, (string Label, Action Pick)[] options)
    {
        CloseSaveWindow();
        _padEnabled = false;
        _saveLayer = new CanvasLayer { Layer = 20 };
        var center = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        var label = new Label { Text = text.Replace("\r\n", "\n"), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 26);
        box.AddChild(label);
        Button? first = null;
        foreach (var (l, pick) in options)
        {
            var b = new Button { Text = l, CustomMinimumSize = new Vector2(0, 44) };
            b.AddThemeFontSizeOverride("font_size", 22);
            b.Pressed += pick;
            box.AddChild(b);
            first ??= b;
        }
        panel.AddChild(box);
        center.AddChild(panel);
        _saveLayer.AddChild(center);
        AddChild(_saveLayer);
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    void CloseSaveWindow()
    {
        if (_saveLayer == null) return;
        _saveLayer.QueueFree();
        _saveLayer = null;
        _padEnabled = true;
    }
}
