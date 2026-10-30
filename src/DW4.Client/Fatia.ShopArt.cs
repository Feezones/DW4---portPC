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
    ColorRect? _shopArtDim;

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
        // Tela larga: a janela da loja é 4:3 centrada; as faixas dos lados escurecem como no menu de status.
        _shopArtDim = new ColorRect { Color = new Color(0, 0, 0, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _shopArtDim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(vp);
        layer.AddChild(_shopArtDim);
        layer.AddChild(rect);
        AddChild(layer);
        return true;
    }

    // Abre ou troca a variante da janela (vazia fecha). Serve à loja e aos menus do banco, X-data e LABO (Fatia.ArtMenu.cs).
    void UpdateShopArt(string variant, IReadOnlyList<string> lines, string title, string help, int cursor)
    {
        if (_shopArtRoot == null || _shopArtData == null) return;
        if (variant != _shopArtVariant)
        {
            foreach (var (a, _) in _shopArtActors) a.Root.QueueFree();
            _shopArtActors.Clear();
            _shopArtVariant = variant;
            if (variant.Length > 0)
                // Sem o j_shback: é um fundo de cenário (esfera em grade) visto por câmera de perspectiva, e na camada ortográfica saía como
                // uma bola verde por cima de tudo (captura de 2026-10-30); as faixas escurecidas fazem o papel de fundo.
                foreach (var (name, seqName) in new[] { ("j_shbase", "SQ_OPEN"), ("j_shwin", variant) })
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
        if (_shopArtDim != null) _shopArtDim.Visible = variant.Length > 0;
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
        // Posições pelas linhas da arte (capturas de 2026-10-30 na tela 16:9): as linhas do j_shwin vêm a cada 19 unidades, a primeira
        // em y 113 (o texto fica logo acima de cada linha); a variante SINKA (LABO) tem a lista no painel da esquerda.
        bool sinka = variant.StartsWith("SQ_SINKA", StringComparison.Ordinal);
        float titleX = sinka ? -300 : -142, titleY = sinka ? 118 : 153, rowX = sinka ? -283 : -128, rowY = sinka ? 93 : 128;
        bool xdata = variant.StartsWith("SQ_XDATA", StringComparison.Ordinal);
        float nameW = sinka ? 200 : xdata ? 110 : 168, priceRight = sinka ? -62 : xdata ? 120 : 228, helpX = sinka ? -300 : -142, helpY = sinka ? -150 : -100;
        // Quadro do jogador do j_shbase (canto de cima à esquerda): o nível depois de "Lv.".
        if (_shopArtActors.Count > 1) Put($"{_level}", -222, 200, 60); // só com o j_shbase gerado
        Put(title, titleX, titleY, 370);
        for (int r = 0; r < lines.Count && r < ShopPage; r++)
        {
            var parts = lines[r].Split('\t');
            float y = rowY - ShopRowStep * r;
            var color = r == cursor ? new Color(1f, 0.95f, 0.4f) : (Color?)null;
            Put(r == cursor ? ">" : "", rowX - 18, y, 12, color: color);
            // Sem valor à direita, o nome pode ocupar a linha toda (não quebra por cima da linha de baixo).
            Put(parts[0], rowX, y, parts.Length > 1 ? nameW : 340, color: color);
            if (parts.Length > 1) Put(parts[1], priceRight - 98, y, 98, right: true, color: color);
        }
        Put(help, helpX, helpY, 300);
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
