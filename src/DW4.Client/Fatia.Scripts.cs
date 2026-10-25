using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Scripts originais da dng1 na VM própria (ADR 0002): func.lua, boot.lua, start.lua e first.lua na ordem do motor, e os
/// comandos be:* chegando aqui. Por enquanto a fatia atende o controle (STAGECTL PADINPUT), as músicas (SOUND PLAY e
/// BGMSTOP) e o ponto de entrada (PLAYERPOINT GETGENERATEPOS); o resto aparece na lista "scripts" do HUD.
/// Scripts lidos de &lt;fatia&gt;\lua, gerados por: dw4 lua-extrair ndmw.afs 179 &lt;fatia&gt;\lua
/// </summary>
public partial class Fatia : IBeEngine
{
    StageScripts? _stage;
    bool _padEnabled = true;
    readonly List<string> _scriptLog = [];
    readonly Dictionary<string, AudioStreamPlayer> _music = [];
    // FATO: beSoundInfo da dng1 liga esses nomes às entradas ndmw #6 e #35 (docs/gameplay/fase4-levantamento-audio.md).
    static readonly Dictionary<string, int> MusicIndex = new() { ["AM_DUNGE"] = 6, ["DG_CAVE1"] = 35 };

    bool StartScripts()
    {
        var dir = Path.Combine(_dir, "lua");
        if (!File.Exists(Path.Combine(dir, "boot.lua")))
        {
            _problems.Add($"sem scripts em {dir} (dw4 lua-extrair ndmw.afs 179 <fatia>\\lua): músicas fixas");
            return false;
        }
        var chunks = Directory.GetFiles(dir).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        // HIPÓTESE: nome da pasta da dng1; o boot.lua só o usa para montar a lista de arquivos, que a fatia não usa.
        var sys = new BeSystem { FolderName = "dng101b1" };
        sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
        var host = new BeHost(this, new GameRam(), sys) { Unimplemented = (n, _) => ScriptLog($"be:{n} ainda não implementado") };
        _stage = new StageScripts(chunks, host) { Error = (n, m) => ScriptLog($"ERRO {n}: {m}") };
        _stage.Boot();
        _stage.Start();
        return true;
    }

    void ScriptLog(string line)
    {
        _scriptLog.Add(line);
        if (_scriptLog.Count > 200) _scriptLog.RemoveAt(0);
        GD.Print($"[script] {line}");
    }

    public void Deliver(BeMessage m)
    {
        ScriptLog(m.ToString());
        if (DeliverToGenerators(m) || DeliverToObjects(m)) return;
        switch (m.Class, m.Command)
        {
            // FATO: "HITLANDMODEL doorwallNN OFFHIT" desliga a parede de colisão da porta (filho do igSwitch da colisão #1862).
            case ("HITLANDMODEL", "OFFHIT" or "ONHIT") when m.Name != null:
            {
                var parts = _collisionShapes.Where(c => c.Path.Contains(m.Name, StringComparison.Ordinal)).ToList();
                foreach (var c in parts) c.Shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, m.Command == "OFFHIT");
                if (parts.Count == 0) ScriptLog($"{m.Name}: parte não encontrada na colisão exportada");
                break;
            }
            case ("STAGECTL", "PADINPUT"):
                _padEnabled = m.Text(0) == "ENABLE";
                break;
            case ("SOUND", "PLAY") when m.Text(0) is { } name:
                PlayMusic(name);
                break;
            case ("SOUND", "BGMSTOP"):
                foreach (var p in _music.Values) p.Stop();
                break;
        }
    }

    // PLAYERgetPos: ponto de entrada na fase. DW4_ENTRADA escolhe (0 = porta inicial, com a demo de entrada; FATO pelo first.lua).
    public double? Query(BeMessage m) => m.Command switch
    {
        "GETGENERATEPOS" => int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0,
        _ => null,
    };

    void PlayMusic(string name)
    {
        if (!_music.TryGetValue(name, out var player))
        {
            if (!MusicIndex.TryGetValue(name, out int idx)) { ScriptLog($"música {name} sem entrada conhecida"); return; }
            var wav = Path.Combine(_dir, "audio", $"ndmw_{idx:D4}.wav");
            if (LoadWav(wav) is not { } stream) { _problems.Add($"falta {wav} (dw4 adx-wav ndmw.afs <fatia>\\audio 6 35)"); return; }
            AddChild(player = new AudioStreamPlayer { Stream = stream });
            _music[name] = player;
        }
        player.Play();
    }
}
