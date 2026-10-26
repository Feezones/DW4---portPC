using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace DW4.Client;

/// <summary>
/// Fonte do jogo (fatia\fonte, dw4 fonte-exportar do ndmw #79). FATO: duas famílias beFontImage, MINIMUM (linha 0.1848) e NORMAL
/// (linha 0.28), na mesma textura 512 × 512; cada caractere tem avanço, deslocamento x/y, largura/altura e UV com V contado de baixo.
/// FATO (usa_pack): as caixas TALKFONT têm linhas de 28 px (NORMAL) e 20 px (MINIMUM) na tela 640 × 480.
/// HIPÓTESE: a escala é altura da linha da caixa ÷ linha da família; o caractere fica com a base na linha (y do caractere).
/// </summary>
public sealed class BitmapFont
{
    public readonly record struct Glyph(float Advance, float X, float Y, float W, float H, Vector2 Uv0, Vector2 Uv1);

    public sealed class Family
    {
        public required string Name { get; init; }
        public float Line { get; init; }
        public Dictionary<int, Glyph> Glyphs { get; } = [];
    }

    public Dictionary<string, Family> Families { get; } = [];
    public Texture2D Texture { get; }

    BitmapFont(Texture2D texture) => Texture = texture;

    public static BitmapFont? Load(string dir)
    {
        var json = Path.Combine(dir, "fonte.json");
        if (!File.Exists(json)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        BitmapFont? font = null;
        foreach (var f in doc.RootElement.GetProperty("fontes").EnumerateArray())
        {
            if (font == null)
            {
                var img = Image.LoadFromFile(Path.Combine(dir, f.GetProperty("textura").GetString()!));
                if (img == null) return null;
                font = new BitmapFont(ImageTexture.CreateFromImage(img));
            }
            var fam = new Family { Name = f.GetProperty("nome").GetString()!, Line = f.GetProperty("linha").GetSingle() };
            foreach (var c in f.GetProperty("caracteres").EnumerateArray())
            {
                float V(int i) => c[i].GetSingle();
                // V conta de baixo: na imagem, y = 1 − v.
                fam.Glyphs[c[0].GetInt32()] = new Glyph(V(1), V(2), V(3), V(4), V(5), new Vector2(V(6), 1 - V(9)), new Vector2(V(8), 1 - V(7)));
            }
            font.Families[fam.Name] = fam;
        }
        return font;
    }

    /// <summary>
    /// Malha do texto em pixels da tela (y para cima, origem no alto à esquerda da primeira linha), quebrando palavras em
    /// <paramref name="width"/>. Caractere sem desenho avança como espaço.
    /// </summary>
    public ArrayMesh? Build(string family, string text, float lineHeight, float width, out Vector2 size)
    {
        size = Vector2.Zero;
        if (!Families.TryGetValue(family, out var fam) || text.Length == 0) return null;
        float scale = lineHeight / fam.Line;
        var space = fam.Glyphs.TryGetValue(' ', out var sp) ? sp : new Glyph(fam.Line * 0.2f, 0, 0, 0, 0, default, default);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        float x = 0, y = -lineHeight, maxX = 0;

        float WordWidth(int start)
        {
            float w = 0;
            for (int i = start; i < text.Length && text[i] != ' ' && text[i] != '\n'; i++)
                w += (fam.Glyphs.TryGetValue(text[i], out var g) ? g.Advance : space.Advance) * scale;
            return w;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r') continue;
            if (ch == '\n') { x = 0; y -= lineHeight; continue; }
            if (ch != ' ' && x > 0 && (i == 0 || text[i - 1] == ' ') && x + WordWidth(i) > width) { x = 0; y -= lineHeight; }
            if (ch == ' ' && x == 0 && i > 0) continue;
            if (!fam.Glyphs.TryGetValue(ch, out var g)) g = space;
            if (g.W > 0 && ch != ' ')
            {
                float x0 = x + g.X * scale, y0 = y + g.Y * scale, x1 = x0 + g.W * scale, y1 = y0 + g.H * scale;
                verts.AddRange([new(x0, y1, 0), new(x1, y1, 0), new(x1, y0, 0), new(x0, y1, 0), new(x1, y0, 0), new(x0, y0, 0)]);
                uvs.AddRange([g.Uv0, new(g.Uv1.X, g.Uv0.Y), g.Uv1, g.Uv0, g.Uv1, new(g.Uv0.X, g.Uv1.Y)]);
            }
            x += g.Advance * scale;
            maxX = Math.Max(maxX, x);
        }
        size = new Vector2(maxX, -y);
        if (verts.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    readonly Dictionary<string, FontFile> _fontFiles = [];

    /// <summary>
    /// A família como FontFile de bitmap da Godot (o código do caractere é o índice do glifo, como no BMFont), para Label e
    /// RichTextLabel: tamanho fixo = a linha da caixa do jogo (28 px NORMAL, 20 px MINIMUM) e escala livre para outros tamanhos.
    /// A base fica no pé da linha (ascent = linha, descent = 0), onde o caractere do jogo pousa (y do caractere, em geral 0).
    /// Os ícones (⊂ ⊃ ∪ ∩ elementos, ¶ Data, ≪ ≫ botões) são caracteres da própria fonte e saem coloridos.
    /// </summary>
    public FontFile? ToFontFile(string family)
    {
        if (_fontFiles.TryGetValue(family, out var cached)) return cached;
        if (!Families.TryGetValue(family, out var fam)) return null;
        int size = family == "MINIMUM" ? 20 : 28;
        float scale = size / fam.Line;
        var img = Texture.GetImage();
        var px = new Vector2(img.GetWidth(), img.GetHeight());
        var key = new Vector2I(size, 0);
        var ff = new FontFile { FixedSize = size, FixedSizeScaleMode = TextServer.FixedSizeScaleMode.Enabled, AllowSystemFallback = false, Antialiasing = TextServer.FontAntialiasing.Gray };
        ff.SetTextureImage(0, key, 0, img);
        ff.SetCacheAscent(0, size, size);
        ff.SetCacheDescent(0, size, 0);
        foreach (var (code, g) in fam.Glyphs)
        {
            ff.SetGlyphAdvance(0, size, code, new Vector2(g.Advance * scale, 0));
            ff.SetGlyphOffset(0, key, code, new Vector2(g.X * scale, -(g.Y + g.H) * scale));
            ff.SetGlyphSize(0, key, code, new Vector2(g.W * scale, g.H * scale));
            ff.SetGlyphUVRect(0, key, code, new Rect2(g.Uv0 * px, (g.Uv1 - g.Uv0) * px));
            ff.SetGlyphTextureIdx(0, key, code, 0);
        }
        // Caracteres que a fonte do jogo não tem (/, %, =, #, ▶…) saem na fonte padrão da Godot.
        ff.Fallbacks = [ThemeDB.FallbackFont];
        _fontFiles[family] = ff;
        return ff;
    }

    /// <summary>Material da fonte: sem luz, transparente, por cima de tudo; a cor do texto entra no albedo.</summary>
    public StandardMaterial3D Material(Color color, int priority) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        AlbedoTexture = Texture,
        AlbedoColor = color,
        NoDepthTest = true,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
        RenderPriority = priority,
    };
}
