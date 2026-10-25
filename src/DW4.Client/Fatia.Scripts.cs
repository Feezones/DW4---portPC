using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
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
    static GameRam? s_ram;
    static GameRam Ram => s_ram ??= new GameRam();
    bool _padEnabled = true;
    readonly List<string> _scriptLog = [];
    readonly Dictionary<string, AudioStreamPlayer> _music = [];
    // FATO: beSoundInfo da dng1 liga esses nomes às entradas ndmw #6 e #35 (docs/gameplay/fase4-levantamento-audio.md).

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
        var sys = new BeSystem { FolderName = _stageName };
        sys.Players[0] = ("p_girumon", new[] { "w_sword01", "w_sword01", "w_sword01" });
        // A GAMERAM sobrevive à troca de fase (recarga da cena): flags e contadores são do jogo, não da cena.
        // LEVEL e as formas do registro do jogador 0 (SYSgetPlayerLevel, SYSisGetShinkaDigimon, SYSctlShinka).
        // DEPURAÇÃO (só no build Debug, não é do jogo): DW4_FLG=4,409 liga essas flags da GAMERAM ao abrir a fase (estado do enredo).
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_FLG") is { Length: > 0 } flags)
            foreach (var f in flags.Split(',', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(f, out int n)) Ram.SetFlag(n, true);
        Ram.Players[0].Level = () => _level;
        if (Ram.Players[0].Forms.Current < 0 && _shinkaRow is >= 0 and < Evolution.Forms) Ram.Players[0].Forms.Start(_shinkaRow);
        var host = new BeHost(this, Ram, sys) { Unimplemented = (n, _) => ScriptLog($"be:{n} ainda não implementado") };
        _stage = new StageScripts(chunks, host) { Error = (n, m) => ScriptLog($"ERRO {n}: {m}") };
        _stage.Boot();
        _stage.Start();
        return true;
    }

    // Diagnóstico: um chk do diretor de demo segurando a fila por 10 s vai para o log (uma vez por entrada).
    DemoManager.Entry? _demoBlocking;
    int _demoBlockedTicks;
    void WatchDemo()
    {
        var b = _stage?.Host.Demo.Blocking;
        if (b == null || !ReferenceEquals(b, _demoBlocking)) { _demoBlocking = b; _demoBlockedTicks = 0; return; }
        if (++_demoBlockedTicks == 600) ScriptLog($"[debug] fila de demo parada há 10 s em {b.Kind} {b.Message}");
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
        // A trava do ISEND zera em todo SELECTCTRL GENERATE, antes de quem trata a janela (troca de área, mensagem) consumir a mensagem.
        if (m.Class == "SELECTCTRL" && m.Command == "GENERATE") { _confirmArmed = false; _selectEnded = false; }
        if (DeliverToShop(m) || DeliverToListMenu(m) || DeliverToStageChange(m) || DeliverToRecorder(m) || DeliverToGenerators(m) || DeliverToBossBar(m) || DeliverToEnemies(m) || DeliverToObjects(m)) return;
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
            // FATO (first.lua): SETDEMOCAMERA "entry" → espera ISDEMOCAMERA → SETBOXCAMERA volta às câmeras por volume.
            case ("CAMERA", "SETDEMOCAMERA") when m.Text(0) is { } demo:
                StartCameraDemo(demo);
                break;
            case ("CAMERA", "SETBOXCAMERA"):
                _cameraDemo = null;
                break;
            case ("SOUND", "BGMSTOP"):
                foreach (var p in _music.Values) p.Stop();
                break;
        }
    }

    // PLAYERgetPos: ponto de entrada na fase. DW4_ENTRADA escolhe (0 = porta inicial, com a demo de entrada; FATO pelo first.lua).
    // PLAYERgetPosStr: só o jogador 0 existe na fatia.
    public (double X, double Y, double Z, double Yaw)? PlayerPose(int player) =>
        player == 0 ? (_player.GlobalPosition.X, _player.GlobalPosition.Y, _player.GlobalPosition.Z, _move.Facing) : null;

    public double? Query(BeMessage m) => QueryShop(m) ?? QueryListMenu(m) ?? QueryStageChange(m) ?? QueryRecorder(m) ?? m.Command switch
    {
        "GETGENERATEPOS" => int.TryParse(OS.GetEnvironment("DW4_ENTRADA"), out var e) ? e : 0,
        // ISDEMOCAMERA = 1 enquanto a demo toca (o first.lua espera enquanto "==0", até começar, e enquanto "!=0", até acabar).
        // Sem a demo exportada: sem resposta (a espera passa).
        "ISDEMOCAMERA" => _cameraDemo is { } d ? (_cameraDemoT < d.Duration ? 1 : 0) : (double?)null,
        "ISLIVE" => (m.Name == "i_bosshp01" && _bossBarOpen) || _minimap?.Has(m.Name ?? "", m.Index) == true || _objects.Any(o => o.Name == m.Name && o.Index == m.Index && !o.Dead && !o.Erased) ? 1 : 0,
        _ => null,
    };

    CameraDemo? _cameraDemo;
    float _cameraDemoT;

    void StartCameraDemo(string name)
    {
        var files = _demoPatterns.Select(p => Path.Combine(_dir, p.Replace("<nome>", name))).ToList();
        var file = files.FirstOrDefault(File.Exists) ?? files.FirstOrDefault() ?? Path.Combine(_dir, "camera-demo", name);
        if (!File.Exists(file)) { _problems.Add($"falta {file} (dw4 igb-camera-demo ndmw.afs 182 <fatia>\\camera-demo): sem a demo {name}"); return; }
        _cameraDemo = CameraDemo.FromJson(File.ReadAllText(file));
        _cameraDemoT = 0;
    }

    // Câmera da demo: posição, frente e cima amostradas; fov horizontal do igCamera (KeepAspect Width).
    void UpdateCameraDemo(float dt)
    {
        if (_cameraDemo is not { } d) return;
        _cameraDemoT += dt;
        var f = d.Sample(_cameraDemoT);
        var pos = new Vector3(f.Position.X, f.Position.Y, f.Position.Z);
        _camera.KeepAspect = Camera3D.KeepAspectEnum.Width;
        _camera.Fov = d.FovHorizontalDeg;
        _camera.LookAtFromPosition(pos, pos + new Vector3(f.Forward.X, f.Forward.Y, f.Forward.Z), new Vector3(f.Up.X, f.Up.Y, f.Up.Z));
    }

    void PlayMusic(string name)
    {
        if (!_music.TryGetValue(name, out var player))
        {
            // FATO (stagml12): SOUND PLAY também toca efeitos do banco da fase (mlBEEP01 → cue 0xA9250900 → A925_09.wav).
            if (!_musicIndex.TryGetValue(name, out int idx))
            {
                if (_sfx?.Has(name) == true) _sfx.Play(name);
                else ScriptLog($"som {name} sem entrada conhecida (nem música nem efeito do banco)");
                return;
            }
            var wav = Path.Combine(_dir, "audio", $"ndmw_{idx:D4}.wav");
            if (LoadWav(wav) is not { } stream) { _problems.Add($"falta {wav} (dw4 adx-wav ndmw.afs <fatia>\\audio 6 35)"); return; }
            AddChild(player = new AudioStreamPlayer { Stream = stream });
            _music[name] = player;
        }
        player.Play();
    }
}
