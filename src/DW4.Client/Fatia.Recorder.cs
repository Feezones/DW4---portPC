using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using DW4.Gameplay;
using DW4.Script.Be;

namespace DW4.Client;

/// <summary>
/// Gravador da fase (n_recorderdng) e o que o hit_n_recorderdng.lua original pede: sonda de conversa, janela de mensagem,
/// trava de jogador/inimigos/geradores e o minimapa (j_map101b1 + j_mapcur). Levantamento: docs/gameplay/fase4-objetos-dng1.md.
/// Dados (fora do repositório):
///   &lt;fatia&gt;\textos\textos.json   (dw4 igb-textos area.afs 9 &lt;fatia&gt;\textos)
///   &lt;fatia&gt;\mapa                 (dw4 igb-objetos/igb-seq ndmw.afs 179 … j_map101b1 j_mapcur, mesma estrutura de &lt;fatia&gt;\objetos)
/// </summary>
public partial class Fatia
{
    // FATO (ndmw #857): o primeiro quadro de todo ataque (ACTION TALKWF) liga HITBOX 60000 (0,4,7) (3,3,3): a sonda de conversa.
    const int TalkProbe = 60000;
    // HIPÓTESE: volume de conversa do gravador (não lido ainda): caixa 10 × 20 × 10 na base do objeto.
    static readonly Vector3 TalkVolume = new(10, 20, 10);
    /// <summary>Modelo da forma atual do jogador (MODEL_NAME da shinka). Vem da vaga (Session) e muda na evolução.</summary>
    public static string PlayerModel { get; set; } = "p_girumon";

    bool _playerCtrl = true, _enemiesFrozen, _generatorsFrozen;
    bool _confirmDown, _confirmArmed, _selectEnded;
    List<string>? _texts;
    bool _textsTried;
    Label? _message;
    PanelContainer? _messagePanel;
    string? _messageText;
    Minimap? _minimap;

    // Na troca de ação do jogador: a caixa 60000 dura 1 quadro (Set 60000 1 1) numa ação de 1 quadro, então a sonda é
    // conferida uma vez por ação, sem depender do quadro exato.
    void CheckTalkProbe()
    {
        if (_runner == null || _stage == null) return;
        // O SequenceRunner pula a ação TALKWF e guarda a sonda à parte (TakeTalkProbe).
        var probes = _runner.ActionHitBoxes.Where(h => h.Type == TalkProbe).ToList();
        if (_runner.TakeTalkProbe() is { } tp) probes.Add(tp);
        foreach (var h in probes)
        {
            var c = BoxCenter(h);
            foreach (var o in _objects.Where(o => o.Name.StartsWith("n_", StringComparison.Ordinal) && !o.Dead).ToList())
            {
                var p = o.Actor.Root.GlobalPosition;
                if (Math.Abs(c.X - p.X) > h.Size.X / 2 + TalkVolume.X / 2 || Math.Abs(c.Z - p.Z) > h.Size.Z / 2 + TalkVolume.Z / 2
                    || c.Y + h.Size.Y / 2 < p.Y || c.Y - h.Size.Y / 2 > p.Y + TalkVolume.Y) continue;
                var script = $"hit_{o.Name}.lua";
                if (!_stage.Has(script)) continue;
                o.BulletKind = TalkProbe;
                _stage.RunAs(script, o);
                // O script guardou a direção (MDLgetRotStr) antes; no fim ele devolve com MOVETRANS "pos/rot" (FATO, hit_n_digilfm.lua).
                // HIPÓTESE: entre os dois o motor vira o NPC para quem falou.
                var to = _player.GlobalPosition - p;
                if (to.X * to.X + to.Z * to.Z > 1e-4f) o.Actor.Root.Rotation = new Vector3(o.Actor.Root.Rotation.X, MathF.Atan2(to.X, to.Z), o.Actor.Root.Rotation.Z);
                return;
            }
        }
    }

    /// <summary>Mensagens do gravador e da janela de texto. true se tratou.</summary>
    bool DeliverToRecorder(BeMessage m)
    {
        if (m.Class == "SELECTCTRL" && m.Command == "GENERATE") { _confirmArmed = false; _selectEnded = false; } // espera um toque novo
        switch (m.Class, m.Command)
        {
            case ("MODELCTRL", "SQ_WAIT") when m.Name == PlayerModel:
                _runner?.Stop(); // corta o ataque que abriu a conversa
                return true;
            case ("MODELCTRL", "GENERATE") when m.Name != null && m.Name.Length > 0 && (m.Name == _mapBase || m.Name == _mapCursor):
                _minimap ??= Minimap.Create(Path.Combine(_dir, "mapa"), this, _problems);
                _minimap?.Generate(m.Name, m.Index, m.Text(0), m.Name == _mapCursor);
                return true;
            case ("PLAYER", "OFCTRL"): _playerCtrl = false; return true;
            case ("PLAYER", "ONCTRL"): _playerCtrl = true; return true;
            // FATO (gene00.lua): MOVETRANS "V<pos>/V<0,giro,0>" põe o jogador no ponto (o 2º vetor em graus). Só o jogador 0 na fatia.
            case ("PLAYER", "MOVETRANS") when m.Index <= 0 && m.Arg(0) is { Kind: 'V', Numbers.Length: 3 } mp:
                _player.GlobalPosition = new Vector3((float)mp.Numbers[0], (float)mp.Numbers[1], (float)mp.Numbers[2]);
                _player.Velocity = Vector3.Zero;
                _move.Stop();
                if (m.Arg(1) is { Kind: 'V', Numbers.Length: 3 } mr) _move.Face(Mathf.DegToRad((float)mr.Numbers[1]));
                _snapToFloor = true;
                return true;
            case ("ENEMY", "OFCTRL"): _enemiesFrozen = true; return true;
            case ("ENEMY", "ONCTRL"): _enemiesFrozen = false; return true;
            case ("GENERATER", "ONFREEZE"): _generatorsFrozen = true; return true;
            case ("GENERATER", "OFFREEZE"): _generatorsFrozen = false; return true;
            // FATO: "TALKFONT01/font00/I<k>": k = posição na beUnicodeObjList do pacote (113 de 136 conferidos).
            case ("TEXTCTL", "TEXTSET"):
                _messageText = m.Args.FirstOrDefault(a => a.Kind == 'I') is { Numbers.Length: > 0 } k ? Text((int)k.Numbers[0]) : null;
                return true;
            case ("TEXTCTL", "SWITCH") when m.RawArgs is { } sw:
                ShowMessage(sw.EndsWith("/SET", StringComparison.Ordinal) ? _messageText : null);
                return true;
        }
        return false;
    }

    double? QueryRecorder(BeMessage m)
    {
        switch (m.Command)
        {
            // HIPÓTESE: ISEND = 1 depois que o jogador confirmou (toque que começou depois do SELECTCTRL GENERATE); volta a 0 no
            // próximo GENERATE. funcOneMessage: espera enquanto "!=0" (fim anterior) e enquanto "==0" (até confirmar).
            case "ISEND" when m.Class is "TEXTCTL" or "SELECTCTRL": return _selectEnded ? 1 : 0;
            // ISSEQNAME = 1 quando o modelo está na sequência pedida (o DM espera enquanto "==0").
            case "ISSEQNAME" when m.Text(0) is { } seq:
                if (m.Class == "PLAYER" || m.Name == PlayerModel) return (_runner?.Running == true ? _runner.Sequence : "SQ_WAIT") == seq ? 1 : 0;
                var o = _objects.FirstOrDefault(o => o.Name == m.Name && o.Index == m.Index);
                return o == null ? null : o.Sequence == seq ? 1 : 0;
        }
        return null;
    }

    string? Text(int index)
    {
        if (!_textsTried)
        {
            _textsTried = true;
            var file = Path.Combine(_dir, "textos", "textos.json");
            if (!File.Exists(file)) _problems.Add($"falta {file} (dw4 igb-textos area.afs 9 <fatia>\\textos): mensagens sem texto");
            else
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                _texts = doc.RootElement.GetProperty("texts").EnumerateArray()
                    .OrderBy(t => t.GetProperty("index").GetInt32()).Select(t => t.GetProperty("text").GetString() ?? "").ToList();
            }
        }
        return _texts != null && index >= 0 && index < _texts.Count ? _texts[index] : $"(texto {index})";
    }

    // DECISÃO DA FATIA: a janela i_messwn e a fonte do jogo ainda não foram exportadas; o texto sai numa caixa da Godot embaixo.
    void ShowMessage(string? text)
    {
        if (_messagePanel == null)
        {
            var layer = new CanvasLayer { Layer = 4 }; // acima do fade (i_fadewn01, camada 3): o aviso da troca de fase aparece com a tela preta
            _messagePanel = new PanelContainer { Visible = false };
            _messagePanel.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
            _messagePanel.Position = new Vector2(-260, -160);
            _messagePanel.CustomMinimumSize = new Vector2(520, 110);
            _message = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _message.AddThemeFontSizeOverride("font_size", 28);
            _messagePanel.AddChild(_message);
            layer.AddChild(_messagePanel);
            AddChild(layer);
        }
        _messagePanel.Visible = text != null;
        if (text != null) _message!.Text = text.Replace("\r\n", "\n");
    }
}

/// <summary>
/// Minimapa: j_map101b1 (KIND_MAPBASE) e j_mapcur (KIND_MAPCURSOR) numa SubViewport própria.
/// FATO: geometria em unidades de mundo no plano XZ; projeção ortográfica de 360 × 360 unidades num igViewportAttr
/// x 500, y 52, 120 × 120 da tela 640 × 480; MAPBASE centra pela média das posições dos jogadores (y = 0); MAPCURSOR = posição
/// do jogador. HIPÓTESES: vista de cima com −Z para cima na tela (a matriz de visão não foi achada); o mapa fica no canto
/// depois do gravador até sair da fase (nenhum script manda SQ_CLOSE).
/// </summary>
sealed class Minimap
{
    readonly string _dir;
    readonly HudData _data;
    readonly SubViewport _vp;
    readonly Camera3D _cam;
    readonly TextureRect _rect;
    readonly List<(string Key, HudActor Actor, ObjectSequencer? Seq, int Player)> _actors = [];

    Minimap(string dir, HudData data, Node owner)
    {
        _dir = dir;
        _data = data;
        _vp = new SubViewport { Size = new Vector2I(240, 240), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _cam = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 360, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 5000, Current = true };
        _vp.AddChild(_cam);
        var layer = new CanvasLayer { Layer = 1 };
        _rect = new TextureRect { Texture = _vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        layer.AddChild(_vp);
        layer.AddChild(_rect);
        owner.AddChild(layer);
    }

    public static Minimap? Create(string dir, Node owner, List<string> problems)
    {
        var data = HudData.Load(dir, problems, "objetos.json", $"falta {Path.Combine(dir, "objetos.json")} (exportação do j_map101b1/j_mapcur): sem minimapa");
        return data == null ? null : new Minimap(dir, data, owner);
    }

    public void Generate(string name, int index, string? sequence, bool cursor)
    {
        if (!_data.Actors.TryGetValue(name, out int root)) return;
        int player = Math.Max(0, index);
        string key = $"{name}/{player}";
        if (_actors.Any(a => a.Key == key)) return;
        var actor = new HudActor(_data, root, slot: cursor ? player : 0);
        var seqDir = Path.Combine(_dir, name);
        var seqFile = Directory.Exists(seqDir) ? Directory.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        ObjectSequencer? seq = null;
        if (seqFile != null)
        {
            var json = File.ReadAllText(seqFile);
            seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
            // j_map101b1: GENERATE "SQ_OPEN"; j_mapcur: um SQ_OPEN0n por jogador (HIPÓTESE: n = jogador).
            if (!(sequence != null && sequence.StartsWith("SQ_", StringComparison.Ordinal) && seq.Start(sequence))) seq.Start($"SQ_OPEN0{player}");
        }
        _vp.AddChild(actor.Root);
        _actors.Add((key, actor, seq, cursor ? player : -1));
        if (!cursor) _rect.Visible = true;
    }

    public bool Has(string name, int index) => _actors.Any(a => a.Key == $"{name}/{Math.Max(0, index)}");

    public void Update(float dt, Vector3 player, Vector2 window)
    {
        if (!_rect.Visible) return;
        var center = new Vector3(player.X, 0, player.Z);
        _cam.Position = center + new Vector3(0, 1000, 0);
        _cam.RotationDegrees = new Vector3(-90, 0, 0);
        foreach (var (_, actor, seq, who) in _actors)
        {
            seq?.Tick(dt);
            if (seq != null) actor.Time = seq.Frame / 60f;
            if (who == 0) actor.Root.Position = center;
            actor.Update();
        }
        // Mesma escala do HUD (altura da janela); em tela larga, preso à borda direita como na tela 4:3 (ScreenLayout).
        var fit = ScreenLayout.Fit(window.X, window.Y);
        var (x, y) = fit.ToWindow(500, 52, anchor: true);
        _rect.Position = new Vector2(x, y);
        _rect.Size = new Vector2(120 * fit.Scale, 120 * fit.Scale);
    }
}
