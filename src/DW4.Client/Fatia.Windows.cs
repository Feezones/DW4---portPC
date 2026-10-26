using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Janelas 2D dos scripts com a arte do jogo (&lt;fase&gt;\janelas, dw4 fase-exportar: i_talkwn, i_messwn, i_talkky, i_messky, i_messcr,
/// i_zzstchg*, rostos n_*ic…). FATO (#821): cada janela é um ator 2D com sequências próprias (SQ_OPENL → OPENL = JOINTSET 10..26,
/// SQ_WAITL → LOOPL 30..60 em laço, SQ_CLOSEL → CLOSEL 70..86 e SQ_ERAS), numa tela 640 × 480 (a mesma do painel de status).
/// Os scripts mandam MODELCTRL &lt;janela&gt; GENERATE "[V&lt;x,y,z&gt;/]SQ_x", depois SQ_* e perguntam ISRUN/ISSEQNAME; os textos vêm por
/// TEXTCTL TEXTSET "&lt;FONTE&gt;/fontNN/I&lt;k&gt;" e SWITCH ".../SET|CLR".
/// HIPÓTESES: ISRUN = 1 enquanto a janela existe (até o SQ_ERAS); a posição do texto dentro da janela (o beTextCtrl com as caixas
/// das fontes não foi lido): o nome (…FONT03) no alto, a fala (…FONT02/font00) embaixo dele, alinhados à esquerda.
/// DECISÃO DA FATIA: a fonte do jogo não foi exportada (fonte da Godot). O i_fadewn01 continua no ColorRect do Fatia.StageChange.
/// Sem a pasta janelas (fase antiga), fica a caixa da Godot de antes.
/// </summary>
public partial class Fatia
{
    sealed class WindowActor
    {
        public required string Name { get; init; }
        public int Index { get; init; }
        public required HudActor Actor { get; init; }
        public ObjectSequencer? Seq { get; init; }
    }

    HudData? _winData;
    bool _winTried;
    Node3D? _winRoot;
    readonly Dictionary<(string, int), WindowActor> _windows = [];
    readonly Dictionary<string, (string Text, bool On, Color Color)> _winTexts = [];
    readonly Dictionary<string, Label3D> _winLabels = [];

    bool WindowsReady()
    {
        if (_winTried) return _winData != null;
        _winTried = true;
        var dir = Path.Combine(_dir, "janelas");
        if (!Directory.Exists(dir)) return false;
        _winData = HudData.Load(dir, _problems, "*_hud.json", null);
        if (_winData == null) return false;
        var vp = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        vp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true, Position = new Vector3(0, 0, 10000) });
        _winRoot = new Node3D();
        vp.AddChild(_winRoot);
        var layer = new CanvasLayer { Layer = 2 };
        var rect = new TextureRect { Texture = vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(vp);
        layer.AddChild(rect);
        AddChild(layer);
        return true;
    }

    bool TalkWindowOpen => _windows.Keys.Any(k => k.Item1 is "i_talkwn" or "i_messwn");

    static bool IsWindowName(string name) => name != "i_fadewn01" && (name.StartsWith("i_", StringComparison.Ordinal) || name.EndsWith("ic", StringComparison.Ordinal));

    bool DeliverToWindows(BeMessage m)
    {
        if (m.Class == "TEXTCTL" && m.RawArgs is { } raw && raw.Split('/') is { Length: >= 2 } parts && parts[0].Contains("FONT", StringComparison.Ordinal) && WindowsReady())
        {
            string key = parts[0] + "/" + parts[1];
            var cur = _winTexts.TryGetValue(key, out var known) ? known : (Text: "", On: false, Color: new Color(1, 1, 1));
            switch (m.Command)
            {
                case "TEXTSET":
                    cur.Text = m.Args.FirstOrDefault(a => a.Kind == 'I') is { Numbers.Length: > 0 } k ? Text((int)k.Numbers[0]) ?? "" : "";
                    break;
                case "SWITCH":
                    cur.On = raw.EndsWith("/SET", StringComparison.Ordinal);
                    break;
                case "COLOR" when m.Args.FirstOrDefault(a => a.Kind == 'V') is { Numbers.Length: 3 } c:
                    cur.Color = new Color((float)c.Numbers[0], (float)c.Numbers[1], (float)c.Numbers[2]);
                    break;
            }
            _winTexts[key] = cur;
            return parts[0].StartsWith("TALKFONT", StringComparison.Ordinal) || parts[0].StartsWith("MESSFONT", StringComparison.Ordinal);
        }
        if (m.Name == null || !IsWindowName(m.Name) || m.Class is not ("MODELCTRL" or "SELECTCTRL") || !WindowsReady()) return false;
        if (!_winData!.Actors.ContainsKey(m.Name)) return false;
        var id = (m.Name, Math.Max(0, m.Index));
        if (m.Command == "GENERATE")
        {
            var w = _windows.GetValueOrDefault(id) ?? SpawnWindow(m.Name, id.Item2);
            if (w == null) return false;
            var args = m.RawArgs ?? "";
            if (m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } p) w.Actor.Root.Position = new Vector3((float)p.Numbers[0], (float)p.Numbers[1], (float)p.Numbers[2]);
            var seq = args.Split('/').LastOrDefault(s => s.StartsWith("SQ_", StringComparison.Ordinal)) ?? (m.Class == "SELECTCTRL" ? "SQ_OPEN" : "SQ_WAIT");
            w.Seq?.Start(seq);
            return m.Class == "MODELCTRL"; // SELECTCTRL segue para quem trata a escolha/confirmação
        }
        if (m.Command.StartsWith("SQ_", StringComparison.Ordinal) && _windows.TryGetValue(id, out var win))
        {
            if (m.Command == "SQ_ERAS") RemoveWindow(id); else win.Seq?.Start(m.Command);
            return true;
        }
        return false;
    }

    WindowActor? SpawnWindow(string name, int index)
    {
        if (_winData == null || _winRoot == null || !_winData.Actors.TryGetValue(name, out int root)) return null;
        var actor = new HudActor(_winData, root, slot: 0, drawOrder: 10 + _windows.Count * 8);
        _winRoot.AddChild(actor.Root);
        var seqDir = Path.Combine(_dir, "janelas", name);
        var seqFile = Directory.Exists(seqDir) ? Directory.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        ObjectSequencer? seq = null;
        if (seqFile != null) { var json = File.ReadAllText(seqFile); seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)); }
        var w = new WindowActor { Name = name, Index = index, Actor = actor, Seq = seq };
        _windows[(name, index)] = w;
        return w;
    }

    void RemoveWindow((string, int) id)
    {
        if (!_windows.Remove(id, out var w)) return;
        w.Actor.Root.QueueFree();
    }

    double? QueryWindows(BeMessage m)
    {
        if (m.Class != "MODELCTRL" || m.Name == null || !IsWindowName(m.Name) || _winData == null || !_winData.Actors.ContainsKey(m.Name)) return null;
        _windows.TryGetValue((m.Name, Math.Max(0, m.Index)), out var w);
        return m.Command switch
        {
            "ISRUN" => w != null ? 1 : 0,
            "ISSEQNAME" when m.Text(0) is { } s => w?.Seq?.Sequence == s ? 1 : 0,
            _ => null,
        };
    }

    void TickWindows(float dt)
    {
        if (_winData == null) return;
        foreach (var (id, w) in _windows.ToList())
        {
            if (w.Seq != null)
            {
                w.Seq.Tick(dt);
                if (w.Seq.Erased) { RemoveWindow(id); continue; }
                w.Actor.Time = w.Seq.Frame / 60f;
            }
            w.Actor.Update();
        }
        // A tecla de confirmar (i_talkky/i_messky) some quando o jogador confirma.
        if (_selectEnded) foreach (var k in _windows.Keys.Where(k => k.Item1 is "i_talkky" or "i_messky").ToList()) RemoveWindow(k);
        UpdateWindowTexts();
    }

    // Texto dentro da janela de fala aberta (HIPÓTESE de posição: ver o resumo da classe).
    void UpdateWindowTexts()
    {
        if (_winRoot == null) return;
        var box = _windows.Values.Where(w => w.Name is "i_talkwn" or "i_messwn").Select(w => Bounds(w.Actor.Root)).FirstOrDefault(b => b.HasValue);
        // O rosto (n_*ic) fica à esquerda da fala: o texto começa à direita dele e o nome fica na altura do alto do rosto.
        var face = _windows.Values.Where(w => w.Name.EndsWith("ic", StringComparison.Ordinal)).Select(w => Bounds(w.Actor.Root, 20)).FirstOrDefault(b => b.HasValue);
        bool hasName = _winTexts.Any(x => x.Key.Contains("FONT03", StringComparison.Ordinal) && x.Value.On);
        foreach (var (key, t) in _winTexts)
        {
            if (!_winLabels.TryGetValue(key, out var label))
            {
                label = new Label3D { PixelSize = 1, FontSize = 18, OutlineSize = 4, NoDepthTest = true, RenderPriority = 120, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, AutowrapMode = TextServer.AutowrapMode.WordSmart, Width = 520 };
                _winRoot.AddChild(label);
                _winLabels[key] = label;
            }
            bool name = key.Contains("FONT03", StringComparison.Ordinal);
            bool main = key.EndsWith("/font00", StringComparison.Ordinal) && (key.StartsWith("TALKFONT02", StringComparison.Ordinal) || key.StartsWith("MESSFONT", StringComparison.Ordinal));
            label.Visible = t.On && box.HasValue && (name || main);
            if (!label.Visible) continue;
            var b = box!.Value;
            label.Text = t.Text.Replace("\r\n", "\n");
            label.Modulate = t.Color;
            // Dentro da tela 640 × 480 (−320..320, −240..240): a caixa medida passa da tela pelas bordas da moldura.
            float left = face is { } f ? f.End.X + 14 : Math.Max(-300, b.Position.X + 24);
            float right = Math.Min(300, b.End.X - 16);
            float top = face is { } f2 ? f2.End.Y : Math.Min(220, b.End.Y - 12);
            left = Math.Clamp(left, -300, 200);
            label.Width = Math.Max(120, right - left);
            label.Position = new Vector3(left, name ? top : top - (hasName ? 26 : 0), 50);
        }
    }

    static Aabb? Bounds(Node3D root, float minWidth = 60)
    {
        Aabb? total = null;
        foreach (var mi in root.FindChildren("*", nameof(MeshInstance3D), true, false).OfType<MeshInstance3D>())
        {
            if (!mi.IsVisibleInTree() || mi.Mesh == null) continue;
            var a = mi.GlobalTransform * mi.Mesh.GetAabb();
            if (a.Size.X < minWidth) continue; // cursores e bordas pequenas não contam como a caixa
            total = total is { } t ? t.Merge(a) : a;
        }
        return total;
    }
}
