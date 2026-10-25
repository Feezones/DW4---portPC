using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DW4.Client;

/// <summary>
/// Efeitos por ação, como o jogo faz (levantamento de áudio de 2026-10-24): beSeInfoData do ator → beSeData(ação, som,
/// atraso) → beSoundData(nome → cue 0xA9GGCC00) → WAV "A9GG_CC.wav" gerado por <c>dw4 igb-sons</c> na pasta sons.
/// HIPÓTESE: atraso em quadros de 60 Hz. Volume e pan do cue e o volume S8 ainda não entram.
/// </summary>
public sealed class SoundEffects
{
    readonly Dictionary<string, int> _cue = [];
    readonly Dictionary<string, Dictionary<string, (string Sound, int Delay)>> _byActor = [];
    readonly Dictionary<int, AudioStreamWav?> _streams = [];
    readonly List<(float Wait, AudioStreamWav Stream)> _pending = [];
    readonly string _dir;
    readonly Node _owner;
    readonly Func<string, AudioStreamWav?> _load;

    public SoundEffects(string dir, Node owner, Func<string, AudioStreamWav?> load, List<string> problems)
    {
        _dir = dir;
        _owner = owner;
        _load = load;
        if (!System.IO.Directory.Exists(dir)) { problems.Add($"falta {dir} (dw4 igb-sons ndmw.afs 178 e 898 <fatia>\\sons): sem efeitos"); return; }
        foreach (var file in System.IO.Directory.EnumerateFiles(dir, "*_sons.json"))
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            // beSoundData por slot: [nome, Int3, S4 tipo, S5 índice, Int6 cue, S7 canais, S8 volume…].
            foreach (var s in doc.RootElement.GetProperty("sounds").EnumerateArray())
            {
                var v = s.GetProperty("values").EnumerateArray().ToArray();
                if (v.Length > 4 && v[0].ValueKind == JsonValueKind.String && v[4].ValueKind == JsonValueKind.Number && v[4].GetInt32() != -1)
                    _cue.TryAdd(v[0].GetString()!, v[4].GetInt32());
            }
            foreach (var se in doc.RootElement.GetProperty("se").EnumerateArray())
            {
                var actor = se.GetProperty("name").GetString() ?? "";
                if (!_byActor.TryGetValue(actor, out var map)) _byActor[actor] = map = [];
                foreach (var e in se.GetProperty("entries").EnumerateArray())
                {
                    var v = e.GetProperty("values").EnumerateArray().ToArray();
                    if (v.Length < 2) continue;
                    int delay = v.Length > 2 && v[2].ValueKind == JsonValueKind.Number ? v[2].GetInt32() : 0;
                    map.TryAdd(v[0].GetString() ?? "", (v[1].GetString() ?? "", delay));
                }
            }
        }
    }

    public int Count => _cue.Count;

    /// <summary>O nome é um efeito do banco de sons (beSoundData com cue), não uma música.</summary>
    public bool Has(string sound) => _cue.ContainsKey(sound);

    /// <summary>Ação nova do ator: agenda o som dela, se houver.</summary>
    public void Action(string actor, string action)
    {
        if (!_byActor.TryGetValue(actor, out var map) || !map.TryGetValue(action, out var se)) return;
        Play(se.Sound, se.Delay / 60f);
    }

    public void Play(string sound, float wait = 0)
    {
        if (!_cue.TryGetValue(sound, out int cue)) return;
        if (!_streams.TryGetValue(cue, out var stream))
        {
            var file = System.IO.Path.Combine(_dir, $"A9{(cue >> 16) & 0xFF:X2}_{(cue >> 8) & 0xFF:X2}.wav");
            _streams[cue] = stream = System.IO.File.Exists(file) ? _load(file) : null;
            // DECISÃO DA FATIA: toca uma vez; o loop (passos, carga) depende de quem para o som, que ainda não foi lido.
            if (stream != null) stream.LoopMode = AudioStreamWav.LoopModeEnum.Disabled;
        }
        if (stream != null) _pending.Add((wait, stream));
    }

    public void Tick(float dt)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (wait, stream) = _pending[i];
            wait -= dt;
            if (wait > 0) { _pending[i] = (wait, stream); continue; }
            _pending.RemoveAt(i);
            var p = new AudioStreamPlayer { Stream = stream };
            _owner.AddChild(p);
            p.Finished += p.QueueFree;
            p.Play();
        }
    }
}
