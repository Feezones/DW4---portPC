using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Loja com a arte do jogo (pasta loja da fase, dw4 fase-exportar: j_shback, j_shbase, j_shwin do #825) e a fonte do jogo.
/// FATO: o j_shwin tem o painel de itens em x −148..238, y −125..155 da tela 640 × 480; a parte da lista (shopitem) vai de y −64 a
/// 155; as caixas STWINFONT da Central Area são de 16 px por linha: nomes 168, ícone 14, preço 98, título 224, ajuda 351 (3 linhas).
/// FATO: o código nativo das lojas não manda posição de texto (o TEXTCTL SETPOSITION só é usado pelos nomes de item no chão).
/// HIPÓTESE (a geometria fecha certo): cabeçalho de 28 px no alto da lista, 12 linhas de 16 px de y 127 a −65 e a ajuda de 3 linhas
/// na faixa de baixo (−125..−64). Variante do j_shwin: SQ_AWINOPEN comprando (Sales), SQ_BWINOPEN vendendo, SQ_JUNKOPEN no
/// ferro-velho (levantamento das classes ShopCtrl, fase5-lojas.md). Sem a pasta loja ou a fonte, fica a caixa da Godot.
/// </summary>
public partial class Fatia
{
    HudData? _shopArtData;
    bool _shopArtTried;
    Node3D? _shopArtRoot;
    readonly List<(HudActor Actor, ObjectSequencer? Seq)> _shopArtActors = [];
    readonly List<MeshInstance3D> _shopArtTexts = [];
    string _shopArtVariant = "";

    bool ShopArtReady()
    {
        if (_shopArtTried) return _shopArtData != null;
        _shopArtTried = true;
        var dir = Path.Combine(_dir, "loja");
        if (!Directory.Exists(dir) || GameFont() == null) return false;
        _shopArtData = HudData.Load(dir, _problems, "*_hud.json", null);
        if (_shopArtData == null) return false;
        var vp = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        vp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true, Position = new Vector3(0, 0, 10000) });
        _shopArtRoot = new Node3D();
        vp.AddChild(_shopArtRoot);
        var layer = new CanvasLayer { Layer = 5 };
        var rect = new TextureRect { Texture = vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(vp);
        layer.AddChild(rect);
        AddChild(layer);
        return true;
    }

    // Abre ou troca a variante da janela; fecha quando a loja fecha.
    void UpdateShopArt(IReadOnlyList<string> lines, string title, string help)
    {
        if (_shopArtRoot == null || _shopArtData == null) return;
        string variant = !ShopOpen ? "" : _shopKind == Array.IndexOf(SubWndNames, "JUNK") ? "SQ_JUNKOPEN" : _shopSelling ? "SQ_BWINOPEN" : "SQ_AWINOPEN";
        if (variant != _shopArtVariant)
        {
            foreach (var (a, _) in _shopArtActors) a.Root.QueueFree();
            _shopArtActors.Clear();
            _shopArtVariant = variant;
            if (variant.Length > 0)
                foreach (var (name, seqName) in new[] { ("j_shback", "SQ_OPEN"), ("j_shbase", "SQ_OPEN"), ("j_shwin", variant) })
                {
                    if (!_shopArtData.Actors.TryGetValue(name, out int root)) continue;
                    var actor = new HudActor(_shopArtData, root, slot: 0, drawOrder: _shopArtActors.Count * 20);
                    _shopArtRoot.AddChild(actor.Root);
                    if (name == "j_shback") actor.Root.Position = new Vector3(0, 0, -2000); // fundo atrás (como o i_stback do menu)
                    var dir = Path.Combine(_dir, "loja", name);
                    var file = Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*_seq.json").FirstOrDefault() : null;
                    ObjectSequencer? seq = null;
                    if (file != null) { var json = File.ReadAllText(file); seq = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json)); seq.Start(seqName); }
                    _shopArtActors.Add((actor, seq));
                }
        }
        foreach (var t in _shopArtTexts) t.QueueFree();
        _shopArtTexts.Clear();
        if (variant.Length == 0) return;
        var font = GameFont()!;
        void Put(string text, float x, float yTop, float width, bool right = false, Color? color = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            var mesh = font.Build("MINIMUM", text, 16, width, out var size);
            if (mesh == null) return;
            var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = font.Material(color ?? new Color(0.92f, 0.92f, 0.92f), 120) };
            mi.Position = new Vector3(right ? x + width - size.X : x, yTop, 60);
            _shopArtRoot.AddChild(mi);
            _shopArtTexts.Add(mi);
        }
        Put(title, -142, 153, 370);
        for (int r = 0; r < lines.Count && r < ShopPage; r++)
        {
            var parts = lines[r].Split('\t');
            float y = 127 - 16 * r;
            var color = r == _shopCursor ? new Color(1f, 0.95f, 0.4f) : (Color?)null;
            Put(r == _shopCursor ? ">" : "", -146, y, 12, color: color);
            Put(parts[0], -128, y, 168, color: color);
            if (parts.Length > 1) Put(parts[1], 130, y, 98, right: true, color: color);
        }
        Put(help, -142, -68, 351);
    }

    void TickShopArt(float dt)
    {
        foreach (var (a, seq) in _shopArtActors)
        {
            if (seq != null) { seq.Tick(dt); a.Time = seq.Frame / 60f; a.Root.Visible = seq.Alpha > 0; }
            a.Update();
        }
    }
}
