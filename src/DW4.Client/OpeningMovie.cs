using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using DW4.Formats.Afs;
using DW4.Formats.Audio;
using DW4.Formats.Sofdec;

namespace DW4.Client;

/// <summary>
/// Abertura do jogo (stagIS09). FATO (first.lua da stagIS09): be:sd("MOVIE", nil, −1, "PLAY", "&lt;região&gt;_opening.sfd"), com a região do
/// SYSgetArea (0 AJP_, 1 AUS_, 2 AEU_; o port responde 1), e STAGECTL PADINPUT ENABLE 120 logo depois; o nextStage.lua leva ao
/// título (stagis10). O .sfd é lido do próprio area.afs (entrada cujo nome é o prefixo da região), separado com o
/// <see cref="MpegPs"/>; o vídeo sai do <see cref="Mpeg1Video"/> numa linha de execução própria e o ADX vira um AudioStreamWav.
/// O som é o relógio: o quadro mostrado é o da posição do som × 29,97.
/// HIPÓTESE: o PADINPUT ENABLE 120 libera pular com um botão depois de 120 quadros (2 s). DESCONHECIDO: se o fim do filme ou o
/// botão é que chama o nextStage.lua. As faixas pretas fazem parte do próprio vídeo (FATO: os quadros decodificados têm as bordas).
/// O port toca uma vez ao abrir, antes do menu inicial; DW4_SEM_FILME=1 pula. Sem o area.afs, não toca.
/// </summary>
public partial class OpeningMovie : Control
{
    const string Region = "AUS_"; // SYSgetArea do port = 1 (BeHost)
    const double SkipAfter = 2.0;

    public event Action? Finished;

    sealed record Planes(int Index, byte[] Y, byte[] Cb, byte[] Cr);

    readonly BlockingCollection<Planes> _frames = new(boundedCapacity: 6);
    readonly CancellationTokenSource _stop = new();
    Task? _decoder;
    volatile bool _decoderDone;
    AudioStreamPlayer? _audio;
    double _fps = 30000 / 1001.0, _clock;
    int _width, _height;
    bool _started, _done;
    Planes? _pending;
    Image? _imgY, _imgCb, _imgCr;
    ImageTexture? _texY, _texCb, _texCr;
    ColorRect? _screen;
    ShaderMaterial? _material;
    AudioStreamWav? _sound;
    string? _error;

    /// <summary>Caminho do area.afs: DW4_ARQUIVOS (pasta files da extração) ou &lt;pasta das fases&gt;\..\extracted\files.</summary>
    public static string AreaAfs()
    {
        var files = OS.GetEnvironment("DW4_ARQUIVOS") is { Length: > 0 } a ? a
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(OS.GetEnvironment("DW4_FASES") is { Length: > 0 } s ? s : @"D:\dw4-work\fases")) ?? "", "extracted", "files");
        return Path.Combine(files, "area.afs");
    }

    public static bool CanPlay() => OS.GetEnvironment("DW4_SEM_FILME") != "1" && File.Exists(AreaAfs());

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(new ColorRect { Color = Colors.Black, AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        _screen = new ColorRect { Color = Colors.Black, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_screen);
        _decoder = Task.Run(Load);
    }

    // Linha de execução do vídeo: lê o .sfd, prepara o som e decodifica quadro a quadro (a fila limita a folga).
    void Load()
    {
        try
        {
            byte[] sfd;
            using (var afs = AfsArchive.Open(AreaAfs()))
            {
                var entry = afs.Entries.FirstOrDefault(e => e.Name == Region) ?? throw new InvalidDataException($"area.afs sem a entrada {Region}");
                using var ms = new MemoryStream();
                afs.CopyEntry(entry, ms);
                sfd = ms.ToArray();
            }
            var (streams, _, _) = MpegPs.Demux(sfd);
            if (!streams.TryGetValue(0xE0, out var video)) throw new InvalidDataException("sem vídeo 0xE0");
            if (streams.TryGetValue(0xC0, out var audio))
            {
                var pcm = Adx.Decode(audio.Payload.ToArray(), out var h);
                var bytes = new byte[pcm.Length * 2];
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                _sound = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = h.SampleRate, Stereo = h.Channels == 2, Data = bytes };
            }
            var dec = new Mpeg1Video(video.Payload.ToArray());
            (_width, _height, _fps) = (dec.Width, dec.Height, dec.FrameRate > 0 ? dec.FrameRate : _fps);
            while (!_stop.IsCancellationRequested && dec.Next() is { } f)
            {
                // Só a área visível (Width × Height) das linhas guardadas.
                var y = new byte[f.Width * f.Height];
                var cb = new byte[y.Length / 4];
                var cr = new byte[y.Length / 4];
                for (int r = 0; r < f.Height; r++) Buffer.BlockCopy(f.Y, r * f.Stride, y, r * f.Width, f.Width);
                for (int r = 0; r < f.Height / 2; r++)
                {
                    Buffer.BlockCopy(f.Cb, r * f.Stride / 2, cb, r * f.Width / 2, f.Width / 2);
                    Buffer.BlockCopy(f.Cr, r * f.Stride / 2, cr, r * f.Width / 2, f.Width / 2);
                }
                _frames.Add(new Planes(f.Index, y, cb, cr), _stop.Token);
            }
            if (dec.Problems.Count > 0) GD.PrintErr($"[filme] {dec.Problems.Count} problemas; o primeiro: {dec.Problems[0]}");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _error = e.Message;
        }
        finally { _decoderDone = true; }
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        if (_error != null) { GD.PrintErr($"[filme] não tocou: {_error}"); Finish(); return; }
        if (!_started)
        {
            if (_width == 0 || (_frames.Count == 0 && !_decoderDone)) return; // ainda carregando
            Begin();
        }
        _clock = _audio is { Playing: true } a
            ? a.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix() - AudioServer.GetOutputLatency()
            : _clock + delta;
        int target = (int)Math.Floor(Math.Max(0, _clock) * _fps);
        Planes? show = null;
        while (true)
        {
            if (_pending == null && !_frames.TryTake(out _pending)) break;
            if (_pending.Index > target) break;
            show = _pending;
            _pending = null;
        }
        if (show != null) Upload(show);
        Layout();
        bool audioOver = _audio == null || !_audio.Playing;
        if (_decoderDone && _frames.Count == 0 && _pending == null && audioOver) Finish();
    }

    void Begin()
    {
        _started = true;
        GD.Print($"[filme] {Region}opening.sfd {_width}x{_height} a {_fps:0.##} fps, som {(_sound != null ? "ADX" : "ausente")}");
        var shader = new Shader
        {
            Code = """
                shader_type canvas_item;
                uniform sampler2D tex_y : filter_linear;
                uniform sampler2D tex_cb : filter_linear;
                uniform sampler2D tex_cr : filter_linear;
                void fragment() {
                    float y = (texture(tex_y, UV).r - 16.0 / 255.0) * 1.164;
                    float cb = texture(tex_cb, UV).r - 0.5;
                    float cr = texture(tex_cr, UV).r - 0.5;
                    COLOR = vec4(y + 1.596 * cr, y - 0.392 * cb - 0.813 * cr, y + 2.017 * cb, 1.0);
                }
                """,
        };
        _material = new ShaderMaterial { Shader = shader };
        _screen!.Material = _material;
        if (_sound != null)
        {
            _audio = new AudioStreamPlayer { Stream = _sound, Bus = PortSettings.MusicBus };
            AddChild(_audio);
            _audio.Play();
        }
    }

    void Upload(Planes p)
    {
        if (_imgY == null)
        {
            _imgY = Image.CreateFromData(_width, _height, false, Image.Format.L8, p.Y);
            _imgCb = Image.CreateFromData(_width / 2, _height / 2, false, Image.Format.L8, p.Cb);
            _imgCr = Image.CreateFromData(_width / 2, _height / 2, false, Image.Format.L8, p.Cr);
            _texY = ImageTexture.CreateFromImage(_imgY);
            _texCb = ImageTexture.CreateFromImage(_imgCb);
            _texCr = ImageTexture.CreateFromImage(_imgCr);
            _material!.SetShaderParameter("tex_y", _texY);
            _material.SetShaderParameter("tex_cb", _texCb);
            _material.SetShaderParameter("tex_cr", _texCr);
            _screen!.Visible = true;
            return;
        }
        _imgY.SetData(_width, _height, false, Image.Format.L8, p.Y);
        _imgCb!.SetData(_width / 2, _height / 2, false, Image.Format.L8, p.Cb);
        _imgCr!.SetData(_width / 2, _height / 2, false, Image.Format.L8, p.Cr);
        _texY!.Update(_imgY);
        _texCb!.Update(_imgCb);
        _texCr!.Update(_imgCr);
    }

    // 4:3 centrado na janela (a imagem inteira, com as faixas do próprio vídeo).
    void Layout()
    {
        if (_screen == null || _width == 0) return;
        var view = Size;
        float scale = Math.Min(view.X / _width, view.Y / _height);
        _screen.Size = new Vector2(_width, _height) * scale;
        _screen.Position = (view - _screen.Size) / 2;
    }

    public override void _Input(InputEvent e)
    {
        if (_done || !_started || _clock < SkipAfter) return;
        bool press = e is InputEventKey { Pressed: true, Echo: false } or InputEventJoypadButton { Pressed: true } or InputEventMouseButton { Pressed: true };
        if (!press) return;
        GetViewport().SetInputAsHandled();
        Finish();
    }

    void Finish()
    {
        if (_done) return;
        _done = true;
        GD.Print($"[filme] fim em {_clock:0.0} s");
        _stop.Cancel();
        _audio?.Stop();
        // Esvazia a fila para a linha de execução não ficar presa no Add.
        while (_frames.TryTake(out _)) { }
        Finished?.Invoke();
        QueueFree();
    }

    public override void _ExitTree()
    {
        _stop.Cancel();
        while (_frames.TryTake(out _)) { }
        try { _decoder?.Wait(500); } catch (AggregateException) { }
    }
}
