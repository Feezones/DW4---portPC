using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// DEPURAÇÃO (só no build Debug, não é do jogo): DW4_PRINT="5,12,20" grava a tela nesses segundos em user://prints/&lt;fase&gt;_&lt;s&gt;.png,
/// para conferir fases sem ninguém olhando (varredura com --quit-after).
/// </summary>
public partial class Fatia
{
    readonly Queue<float> _printAt = new();
    float _printClock;

    void SetupPrints()
    {
        if (!OS.IsDebugBuild()) return;
        foreach (var t in OS.GetEnvironment("DW4_PRINT").Split(',', StringSplitOptions.RemoveEmptyEntries)
                     .Select(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : -1).Where(f => f >= 0).OrderBy(f => f))
            _printAt.Enqueue(t);
    }

    // DEPURAÇÃO: DW4_ABRIR=pausa|config|status|banco|xdata|labo|loja:WPU abre essa janela aos 3 s (para o DW4_PRINT fotografar).
    bool _opened;

    void OpenForPrint()
    {
        var what = OS.GetEnvironment("DW4_ABRIR");
        if (what.StartsWith("loja:", StringComparison.Ordinal))
        {
            DeliverToShop(new DW4.Script.Be.BeMessage("SUBWND", null, -1, "GENERATE", [new DW4.Script.Be.BeArg('\0', what[5..], [])], what[5..]));
            return;
        }
        switch (what)
        {
            case "pausa": OpenPause(); break;
            case "config": OpenSettings(); break;
            case "status": OpenMenu(); break;
            case "banco": OpenBankArt(); break;
            case "xdata": OpenXDataMenu(); break;
            case "labo": OpenEvolve(); break;
            case "cartas": OpenCardShop(); break;
            case "avaliar": OpenAppraisal(); break;
            case "salvar": MenuStageInPlace("stagIS12"); break;
            // Atalhos de item: 3 discos de HP e 2 de MP nos atalhos 1 e 2, e a janela de itens aberta.
            case "itens" when _db != null:
                for (int i = 0; i < 3; i++) _inventory.Add(new ItemRef("conItem", 0));
                for (int i = 0; i < 2; i++) _inventory.Add(new ItemRef("conItem", 3));
                _inventory.QuickItems[0] = 0; _inventory.QuickItems[1] = 3;
                _inventory.Add(new ItemRef("conItem", 1)); _inventory.Add(new ItemRef("conItem", 4));
                OpenQuickWindow(true);
                // Cursor andando várias vezes (o relato da janela que crescia a cada seta).
                for (int i = 0; i < 8; i++) { _quickCursor = i % 4; RefreshQuickWindow(); }
                break;
            // EXP e subida de nível: EXP para passar do nível atual (mostra o +EXP, o painel LEVEL UP e o brilho).
            case "nivel" when _db != null:
                AddExp((int)(PlayerGrowth.NextExp(_level) - _exp));
                break;
        }
    }

    // DEPURAÇÃO: DW4_GERAR=g_tcounter00 manda MODELCTRL <nome> 0 GENERATE (sem argumentos) aos 5 s, como o script faria.
    float _debugGenClock;

    // DEPURAÇÃO: DW4_TECLAS="2:J,3.5:Enter,5:Down,6:clique@480,300,7:direito" manda eventos reais de tecla/mouse (aperta e solta 3 quadros
    // depois) nesses segundos da fase, pelo mesmo caminho da entrada do jogador (conferir janelas sem ninguém no controle).
    float _keysClock;
    List<(float At, string Key)>? _keys;
    readonly List<(int Frames, InputEvent Ev)> _keysUp = [];

    void TickDebugKeys(float dt)
    {
        if (!OS.IsDebugBuild()) return;
        _keys ??= OS.GetEnvironment("DW4_TECLAS").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Split(':', 2))
            .Where(p => p.Length == 2 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            .Select(p => (float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), p[1])).ToList();
        if (_keysClock == 0 && int.TryParse(OS.GetEnvironment("DW4_BIT"), out int debugBit)) _inventory.Bit = debugBit; // DEPURAÇÃO: BIT inicial
        _keysClock += dt;
        for (int i = _keysUp.Count - 1; i >= 0; i--)
        {
            if (_keysUp[i].Frames > 1) { _keysUp[i] = (_keysUp[i].Frames - 1, _keysUp[i].Ev); continue; }
            Input.ParseInputEvent(_keysUp[i].Ev);
            _keysUp.RemoveAt(i);
        }
        while (_keys.Count > 0 && _keys[0].At <= _keysClock)
        {
            var key = _keys[0].Key;
            _keys.RemoveAt(0);
            GD.Print($"[teclas] {_keysClock:0.0} s: {key}");
            if (key.StartsWith("clique@", StringComparison.Ordinal) || key.StartsWith("mover@", StringComparison.Ordinal))
            {
                var xy = key[(key.IndexOf('@') + 1)..].Split(';');
                var pos = new Vector2(float.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture));
                Input.ParseInputEvent(new InputEventMouseMotion { Position = pos, GlobalPosition = pos });
                if (key.StartsWith("mover@", StringComparison.Ordinal)) continue;
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos, GlobalPosition = pos });
                _keysUp.Add((3, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos, GlobalPosition = pos }));
                continue;
            }
            if (key == "direito")
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true });
                _keysUp.Add((3, new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }));
                continue;
            }
            var code = OS.FindKeycodeFromString(key);
            if (code == Key.None) { GD.Print($"[teclas] tecla desconhecida: {key}"); continue; }
            Input.ParseInputEvent(new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = true });
            _keysUp.Add((3, new InputEventKey { Keycode = code, PhysicalKeycode = code, Pressed = false }));
        }
    }

    void TickPrints(float dt)
    {
        TickDebugKeys(dt);
        TickRaiseDebug(dt);
        if (OS.IsDebugBuild() && _debugGenClock < 5 && OS.GetEnvironment("DW4_GERAR") is { Length: > 0 } gen && (_debugGenClock += dt) >= 5)
            Deliver(new DW4.Script.Be.BeMessage("MODELCTRL", gen, 0, "GENERATE", [], null));
        if (_printAt.Count == 0) return;
        if (!_opened && _printClock >= 3 && OS.GetEnvironment("DW4_ABRIR").Length > 0) { _opened = true; OpenForPrint(); }
        _printClock += dt;
        if (_printClock < _printAt.Peek()) return;
        var t = _printAt.Dequeue();
        DirAccess.MakeDirRecursiveAbsolute("user://prints");
        var path = $"user://prints/{_stageName}_{t:0}.png";
        GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[print] {ProjectSettings.GlobalizePath(path)}");
        if (_printAt.Count == 0 && OS.GetEnvironment("DW4_ABRIR").Length > 0) GetTree().Quit(); // captura de janela: fecha sozinho
    }
}
