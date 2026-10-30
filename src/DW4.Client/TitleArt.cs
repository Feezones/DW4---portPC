using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Arte da tela de título (stagIS10) para o menu inicial do port. FATO:
///  · o first.lua da stagIS10 gera j_startback SQ_OPEN e entrega ao TITLE2 nativo;
///  · os atores j_startback (fundo animado), j_startlogo (logotipo), j_startmenu01 ("Press START button", SQ_LINESL00 pisca e
///    SQ_LINEON00 confirma) e j_startmenu02 ("Game Start" / "Option", SQ_LINESL00/01 com SQ_LINEDW01/SQ_LINEUP00 entre as linhas,
///    SQ_LINEON00/01 confirmam e SQ_CANCEL volta) ficam no pacote de idioma (area.afs #620), exportados com dw4 titulo-exportar
///    para &lt;fases&gt;\stagis10\titulo.
/// FATO: o j_startback é a camada BG02 do pacote (cenário, planos de z −1728 a +30) e é vista pela câmera de demo "entry" da
/// stagIS10 (z 730 olhando para −z, fov horizontal 54,43°, near 10, far 2000); conferido com a prévia do dw4 hud-previa, que dá o
/// túnel digital do título. Os outros (camada WN01) são janelas na tela 640 × 480, na origem (HIPÓTESE: o TITLE2 não foi lido; as
/// malhas já trazem a posição na tela e a prévia fecha certo). Duas SubViewports que acompanham a proporção da janela.
/// </summary>
public sealed class TitleArt
{
    readonly HudData _h;
    readonly string _dir;
    readonly Node _owner;
    readonly SubViewport _vp, _bgVp;
    readonly Node3D _root, _bgRoot;
    readonly Dictionary<string, (HudActor Actor, ObjectSequencer? Seq)> _actors = [];

    TitleArt(HudData h, string dir, Node owner, int layer)
    {
        _h = h;
        _dir = dir;
        _owner = owner;
        _vp = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _vp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true, Position = new Vector3(0, 0, 10000) });
        _root = new Node3D();
        _vp.AddChild(_root);
        _bgVp = new SubViewport { Size = new Vector2I(640, 480), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _bgVp.AddChild(new Camera3D { Fov = ScreenLayout.VerticalFov(54.432224f), KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 10, Far = 2000, Current = true, Position = new Vector3(0, 0, 730) });
        _bgVp.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black } });
        _bgRoot = new Node3D();
        _bgVp.AddChild(_bgRoot);
        var canvas = new CanvasLayer { Layer = layer };
        foreach (var vp in new[] { _bgVp, _vp })
        {
            var rect = new TextureRect { Texture = vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
            rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            canvas.AddChild(vp);
            canvas.AddChild(rect);
        }
        owner.AddChild(canvas);
    }

    /// <summary>Carrega a arte de &lt;fases&gt;\stagis10\titulo; null se não foi exportada.</summary>
    public static TitleArt? Create(Node owner, string stagesDir, int layer, List<string> problems)
    {
        var dir = Path.Combine(stagesDir, "stagis10", "titulo");
        var h = HudData.Load(dir, problems, "*_hud.json", $"falta {dir} (dw4 titulo-exportar area.afs 620 {dir}): menu na caixa da Godot");
        return h == null ? null : new TitleArt(h, dir, owner, layer);
    }

    /// <summary>Gera o ator (se preciso) e começa a sequência.</summary>
    public void Play(string actor, string sequence)
    {
        if (!_actors.TryGetValue(actor, out var a))
        {
            if (!_h.Actors.TryGetValue(actor, out int root)) return;
            var hud = new HudActor(_h, root, slot: 0, drawOrder: _actors.Count * 20);
            (actor == "j_startback" ? _bgRoot : _root).AddChild(hud.Root);
            var file = Directory.Exists(Path.Combine(_dir, actor)) ? Directory.EnumerateFiles(Path.Combine(_dir, actor), "*_seq.json").FirstOrDefault() : null;
            ObjectSequencer? seq = null;
            if (file != null) { var json = File.ReadAllText(file); seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)); }
            _actors[actor] = a = (hud, seq);
        }
        a.Actor.Root.Visible = true;
        a.Seq?.Start(sequence);
    }

    public void Remove(string actor)
    {
        if (!_actors.Remove(actor, out var a)) return;
        a.Actor.Root.QueueFree();
    }

    public bool Has(string actor) => _actors.ContainsKey(actor);

    /// <summary>Sequência atual do ator (null se não existe ou acabou em SQ_ERAS).</summary>
    public string? Sequence(string actor) => _actors.TryGetValue(actor, out var a) && a.Seq is { Erased: false } s ? s.Sequence : null;

    public void Tick(float dt)
    {
        if (_owner.IsInsideTree())
        {
            var win = _owner.GetViewport().GetVisibleRect().Size;
            var size = new Vector2I((int)Math.Round(ScreenLayout.Fit(win.X, win.Y).WideWidth), 480);
            if (_vp.Size != size) { _vp.Size = size; _bgVp.Size = size; }
        }
        foreach (var name in _actors.Keys.ToList())
        {
            var (actor, seq) = _actors[name];
            if (seq != null)
            {
                seq.Tick(dt);
                if (seq.Erased) { Remove(name); continue; }
                actor.Time = seq.Frame / 60f;
                actor.SetAlpha(seq.Alpha);
            }
            actor.Update();
        }
    }
}
