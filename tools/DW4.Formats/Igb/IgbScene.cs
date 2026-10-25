using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Uma geometria (igGeometryAttr) já resolvida, em coordenadas do mundo quando havia igTransform acima dela.</summary>
public sealed class IgbMeshPart
{
    public int GeometryIndex { get; init; }
    public int ImageIndex { get; set; } = -1;
    /// <summary>Textura por nome (beTextureCtrlAttr sobre um igAttrSet acima da geometria), resolvida num pacote; null sem.</summary>
    public string? TextureName { get; set; }
    public int PrimitiveType { get; set; }
    public int VertexCount { get; set; }
    public uint VertexFormat { get; set; }
    public List<(float X, float Y, float Z)> Positions { get; } = [];
    public List<(float X, float Y, float Z)> Normals { get; } = [];
    public List<(float U, float V)> TexCoords { get; } = [];
    public List<uint> Colors { get; } = [];
    /// <summary>Raiz de cena (nó logo abaixo do igSceneInfo) de onde a geometria foi alcançada; -1 sem cena.</summary>
    public int SceneRoot { get; init; } = -1;
    /// <summary>Pesos de pele (um float por vértice no DW4) e índice local de matriz (um byte por vértice).</summary>
    public List<float> BlendWeights { get; } = [];
    public List<int> BlendIndices { get; } = [];
    public List<int> Triangles { get; } = [];
    /// <summary>Uma linha por fluxo de vértices: slot externo, bloco de memória, tamanho, passo por vértice e se foi decodificado.</summary>
    public List<string> Notes { get; } = [];
}

/// <summary>Um igImage com a paleta (igClut) quando houver.</summary>
public sealed class IgbImage
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public int PixelFormat { get; init; }
    public int BitsPerPixel { get; init; }
    public byte[] Pixels { get; init; } = [];
    public byte[] Palette { get; init; } = [];
    public int PaletteFormat { get; init; }
    public int PaletteCount { get; init; }
}

/// <summary>
/// Percorre o grafo de cena de um IGB a partir dos igSceneInfo e extrai geometria e imagens.
/// Os slots de campo seguem a descrição pública do igb-blender (scene_graph/sg_geometry.py, sg_materials.py, sg_classes.py)
/// e foram conferidos no DW4 com igb-dump (area.afs #3 e #598). Fluxos de vértices só são decodificados quando o passo
/// por vértice bate com o formato float esperado (posição/normal 12 B, UV 8 B, cor 4 B); os demais ficam anotados.
/// </summary>
public sealed class IgbScene
{
    // Slots do igExternalIndexedEntry segundo o igb-blender (perfil XML2 PC); conferir no DW4 pelos passos anotados.
    const int ExtPositions = 0, ExtNormals = 1, ExtColors = 2, ExtTexCoords = 11;

    readonly IgbFile _f;
    readonly byte[] _data;
    readonly bool _le;

    public List<IgbMeshPart> Parts { get; } = [];
    public Dictionary<int, IgbImage> Images { get; } = [];
    public List<string> Notes { get; } = [];
    public int TransformCount { get; private set; }
    int _root = -1;
    // beTextureCtrlAttr [@2 nome, @3 igAttrSet]: o igAttrSet e o que está abaixo dele usam a textura do pacote com esse nome
    // (FATO no campo stag0101: o cenário BG02 usa P10G00n, P11G00n… do lh_pack/ls_pack #618/#619).
    readonly Dictionary<int, string> _textureNameOf = [];
    string? _textureName;

    IgbScene(IgbFile f, byte[] data)
    {
        _f = f;
        _data = data;
        _le = f.Header.LittleEndian;
    }

    public static IgbScene Read(IgbFile f, byte[] data)
    {
        var s = new IgbScene(f, data);
        foreach (var tc in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beTextureCtrlAttr"))
        {
            var name = tc.Fields.FirstOrDefault(v => v.Desc.Slot == 2 && v.Desc.ShortName == "String")?.Text;
            int target = tc.Fields.FirstOrDefault(v => v.Desc.Slot == 3 && v.Desc.ShortName == "ObjectRef")?.AsInt32(s._le) ?? -1;
            if (name != null && target >= 0) s._textureNameOf.TryAdd(target, name);
        }
        var roots = new List<IgbObject>();
        foreach (var si in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igSceneInfo")))
            foreach (var r in s.Refs(si))
                if (f.Is(r, "igNode") && !roots.Contains((IgbObject)r!)) roots.Add((IgbObject)r!);

        if (roots.Count > 0)
        {
            s.Notes.Add($"{roots.Count} raiz(es) de cena a partir de igSceneInfo");
            foreach (var root in roots)
            {
                s._root = root.Index;
                s.Visit(root, null, -1, []);
            }
            s._root = -1;
        }
        else
        {
            s.Notes.Add("sem igSceneInfo: geometrias lidas soltas, sem transformações nem texturas associadas");
            foreach (var g in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igGeometryAttr")))
                s.AddGeometry(g, null, -1);
        }
        foreach (var img in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igImage")))
            s.ReadImage(img);
        return s;
    }

    void Visit(IgbObject node, float[]? parent, int image, HashSet<int> ancestors)
    {
        if (!ancestors.Add(node.Index)) return;
        var savedName = _textureName;
        if (_textureNameOf.TryGetValue(node.Index, out var tn)) _textureName = tn;
        var world = parent;
        if (_f.Is(node, "igTransform"))
        {
            TransformCount++;
            var m = node.Fields.FirstOrDefault(v => v.Desc.ShortName == "Matrix44f" && v.Raw.Length >= 64);
            if (m != null)
            {
                var local = new float[16];
                for (int i = 0; i < 16; i++) local[i] = F32(m.Raw, 4 * i);
                world = parent == null ? local : Multiply(local, parent);
            }
        }

        if (_f.Is(node, "igAttrSet"))
        {
            var attrs = AttrList(node);
            foreach (var a in attrs.Where(a => _f.Is(a, "igTextureBindAttr")))
            {
                int img = FindImage(a);
                if (img >= 0) image = img;
            }
            foreach (var a in attrs.Where(a => _f.Is(a, "igGeometryAttr")))
                AddGeometry(a, world, image);
        }

        foreach (var child in Children(node)) Visit(child, world, image, ancestors);
        ancestors.Remove(node.Index);
        _textureName = savedName;
    }

    // Filhos de um igGroup: igNodeList (igObjectList) ou referência direta a igNode.
    IEnumerable<IgbObject> Children(IgbObject node)
    {
        foreach (var r in Refs(node))
        {
            if (_f.Is(r, "igObjectList"))
                foreach (var item in ObjectList((IgbObject)r!).Where(i => _f.Is(i, "igNode"))) yield return item;
            else if (_f.Is(r, "igNode")) yield return (IgbObject)r!;
        }
    }

    public List<IgbObject> AttrList(IgbObject attrSet)
    {
        var list = new List<IgbObject>();
        foreach (var r in Refs(attrSet))
        {
            if (_f.Is(r, "igObjectList")) list.AddRange(ObjectList((IgbObject)r!).Where(i => _f.Is(i, "igAttr")));
            else if (_f.Is(r, "igAttr")) list.Add((IgbObject)r!);
        }
        return list;
    }

    // igObjectList: primeiro Int = quantidade; MemoryRef = vetor de i32 com índices de objetos.
    public List<IgbObject> ObjectList(IgbObject list)
    {
        var result = new List<IgbObject>();
        var countField = list.Fields.FirstOrDefault(v => v.Desc.ShortName == "Int");
        var mem = Memory(list.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef"));
        if (countField == null || mem.Length == 0) return result;
        int count = Math.Min(countField.AsInt32(_le), mem.Length / 4);
        for (int i = 0; i < count; i++)
            if (Slot(I32(mem, 4 * i)) is IgbObject o) result.Add(o);
        return result;
    }

    /// <summary>Índices de todos os objetos alcançáveis a partir de um objeto, por ObjectRef e pelo conteúdo de igObjectList.</summary>
    public HashSet<int> Reachable(IgbObject start)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<IgbObject>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var o = queue.Dequeue();
            if (!seen.Add(o.Index)) continue;
            foreach (var r in Refs(o)) if (r is IgbObject ro) queue.Enqueue(ro);
            if (_f.Is(o, "igObjectList")) foreach (var r in ObjectList(o)) queue.Enqueue(r);
        }
        return seen;
    }

    // igTextureBindAttr -> igTextureAttr -> igImage (até três níveis de ObjectRef).
    public int FindImage(IgbObject start)
    {
        var queue = new Queue<(IgbObject, int)>();
        queue.Enqueue((start, 0));
        while (queue.Count > 0)
        {
            var (o, depth) = queue.Dequeue();
            if (_f.Is(o, "igImage")) return o.Index;
            if (depth >= 3) continue;
            foreach (var r in Refs(o)) if (r is IgbObject ro) queue.Enqueue((ro, depth + 1));
        }
        return -1;
    }

    void AddGeometry(IgbObject geom, float[]? world, int image) => Parts.Add(BuildPart(geom, world, image));

    /// <summary>Lê um igGeometryAttr sem transformação nem textura (usado para malhas de pele).</summary>
    public IgbMeshPart ReadGeometry(IgbObject geom) => BuildPart(geom, null, -1);

    IgbMeshPart BuildPart(IgbObject geom, float[]? world, int image)
    {
        var part = new IgbMeshPart { GeometryIndex = geom.Index, ImageIndex = image, SceneRoot = _root, TextureName = image < 0 ? _textureName : null };
        IgbObject? va = null, ia = null, pla = null;
        foreach (var r in Refs(geom))
        {
            if (_f.Is(r, "igVertexArray")) va = (IgbObject)r!;
            else if (_f.Is(r, "igIndexArray")) ia = (IgbObject)r!;
            else if (_f.Is(r, "igPrimLengthArray")) pla = (IgbObject)r!;
        }
        part.PrimitiveType = geom.Fields.FirstOrDefault(v => v.Desc.ShortName == "Enum")?.AsInt32(_le) ?? -1;
        if (va == null) { part.Notes.Add("sem igVertexArray"); return part; }

        part.VertexCount = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "UnsignedInt")?.AsInt32(_le) ?? 0;
        var fmt = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "Struct");
        if (fmt != null && fmt.Raw.Length >= 4) part.VertexFormat = (uint)I32(fmt.Raw, 0);
        int n = part.VertexCount;

        // Pele (igb-blender: MemoryRef no slot 7 = pesos, slot 8 = índices). No DW4: 4 B (float) e 1 B por vértice.
        var weights = Memory(va.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef" && v.Desc.Slot == 7));
        var blendIdx = Memory(va.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef" && v.Desc.Slot == 8));
        if (n > 0 && weights.Length == 4 * n && blendIdx.Length == n)
        {
            for (int i = 0; i < n; i++) part.BlendWeights.Add(F32(weights, 4 * i));
            for (int i = 0; i < n; i++) part.BlendIndices.Add(blendIdx[i]);
        }
        else if (weights.Length > 0 || blendIdx.Length > 0)
            part.Notes.Add($"pele não reconhecida: pesos {weights.Length} B, índices {blendIdx.Length} B para {n} vértices");

        var extRef = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
        var ext = Memory(extRef);
        if (ext.Length is not (76 or 80))
        {
            part.Notes.Add($"vértices sem igExternalIndexedEntry (bloco de {ext.Length} B, passo {(n > 0 ? ext.Length / (double)n : 0):0.##}): não decodificado");
            return part;
        }
        for (int s = 0; s < ext.Length / 4; s++)
        {
            int idx = I32(ext, 4 * s);
            if (idx == -1) continue;
            if (Slot(idx) is not IgbMemoryBlock mb) { part.Notes.Add($"slot externo {s} -> #{idx} não é bloco de memória"); continue; }
            var mem = MemoryOf(mb);
            double stride = n > 0 ? mem.Length / (double)n : 0;
            string what = s switch { ExtPositions => "posição", ExtNormals => "normal", ExtColors => "cor", ExtTexCoords => "UV", _ => "?" };
            bool ok = (s, stride) switch
            {
                (ExtPositions, 12.0) or (ExtNormals, 12.0) or (ExtTexCoords, 8.0) or (ExtColors, 4.0) => true,
                _ => false,
            };
            part.Notes.Add($"slot externo {s,2} ({what}): #{idx} {mb.TypeName} {mem.Length} B, passo {stride:0.##} B/vértice{(ok ? "" : " — não decodificado")}; início {Hex(mem, 24)}");
            if (!ok) continue;
            for (int i = 0; i < n; i++)
            {
                switch (s)
                {
                    case ExtPositions:
                        part.Positions.Add(Apply(world, F32(mem, 12 * i), F32(mem, 12 * i + 4), F32(mem, 12 * i + 8), 1));
                        break;
                    case ExtNormals:
                        part.Normals.Add(Apply(world, F32(mem, 12 * i), F32(mem, 12 * i + 4), F32(mem, 12 * i + 8), 0));
                        break;
                    case ExtTexCoords:
                        part.TexCoords.Add((F32(mem, 8 * i), F32(mem, 8 * i + 4)));
                        break;
                    case ExtColors:
                        part.Colors.Add((uint)I32(mem, 4 * i));
                        break;
                }
            }
        }

        var indices = new List<int>();
        if (ia != null)
        {
            int count = ia.Fields.FirstOrDefault(v => v.Desc.ShortName == "UnsignedInt")?.AsInt32(_le) ?? 0;
            var mem = Memory(ia.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef"));
            int width = count > 0 ? mem.Length / count : 0;
            if (width == 2) for (int i = 0; i < count; i++) indices.Add(U16(mem, 2 * i));
            else if (width == 4) for (int i = 0; i < count; i++) indices.Add(I32(mem, 4 * i));
            else part.Notes.Add($"índices: {mem.Length} B para {count} índices, largura não reconhecida");
        }
        else for (int i = 0; i < n; i++) indices.Add(i);

        var lengths = new List<int>();
        if (pla != null)
        {
            int count = pla.Fields.FirstOrDefault(v => v.Desc.ShortName == "UnsignedInt")?.AsInt32(_le) ?? 0;
            var mem = Memory(pla.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef"));
            int width = count > 0 ? mem.Length / count : 0;
            for (int i = 0; i < count; i++)
                lengths.Add(width switch { 4 => I32(mem, 4 * i), 2 => U16(mem, 2 * i), 1 => mem[i], _ => 0 });
            if (width is not (1 or 2 or 4)) part.Notes.Add($"comprimentos de faixa: {mem.Length} B para {count}, largura não reconhecida");
        }

        // 3 = lista de triângulos, 4 = faixa de triângulos (IG_GFX_DRAW_*, segundo o igb-blender).
        if (part.PrimitiveType == 3) part.Triangles.AddRange(indices);
        else if (part.PrimitiveType == 4)
        {
            if (lengths.Count == 0) lengths.Add(indices.Count);
            int at = 0;
            foreach (int len in lengths)
            {
                for (int i = 0; i + 2 < len && at + i + 2 < indices.Count; i++)
                {
                    int a = indices[at + i], b = indices[at + i + 1], c = indices[at + i + 2];
                    if (a == b || b == c || a == c) continue;
                    if ((i & 1) == 0) part.Triangles.AddRange([a, b, c]);
                    else part.Triangles.AddRange([b, a, c]);
                }
                at += len;
            }
            if (at != indices.Count) part.Notes.Add($"faixas somam {at} índices, há {indices.Count}");
        }
        else part.Notes.Add($"tipo de primitiva {part.PrimitiveType} não tratado");
        if (part.Triangles.Count > 0 && part.Triangles.Max() >= part.Positions.Count && part.Positions.Count > 0)
            part.Notes.Add($"índice máximo {part.Triangles.Max()} >= {part.Positions.Count} posições");
        return part;
    }

    void ReadImage(IgbObject img)
    {
        int Int(int slot) => img.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Raw.Length == 4)?.AsInt32(_le) ?? 0;
        var clut = Slot(img.Fields.FirstOrDefault(v => v.Desc.Slot == 17 && v.Desc.ShortName == "ObjectRef")?.AsInt32(_le) ?? -1) as IgbObject;
        byte[] palette = [];
        int palFormat = 0, palCount = 0;
        if (clut != null && _f.Is(clut, "igClut"))
        {
            palFormat = clut.Fields.FirstOrDefault(v => v.Desc.Slot == 2)?.AsInt32(_le) ?? 0;
            palCount = clut.Fields.FirstOrDefault(v => v.Desc.Slot == 3)?.AsInt32(_le) ?? 0;
            palette = Memory(clut.Fields.FirstOrDefault(v => v.Desc.Slot == 5 && v.Desc.ShortName == "MemoryRef"));
        }
        Images[img.Index] = new IgbImage
        {
            Index = img.Index,
            Name = img.Fields.FirstOrDefault(v => v.Desc.Slot == 22 && v.Text != null)?.Text ?? "",
            Width = Int(2),
            Height = Int(3),
            PixelFormat = Int(11),
            BitsPerPixel = Int(18),
            Pixels = Memory(img.Fields.FirstOrDefault(v => v.Desc.Slot == 13 && v.Desc.ShortName == "MemoryRef")),
            Palette = palette,
            PaletteFormat = palFormat,
            PaletteCount = palCount,
        };
    }

    /// <summary>
    /// Converte uma imagem indexada de 8 bits com paleta RGBA8888 (igClut formato 7) para RGBA com a primeira linha no topo.
    /// No DW4 os índices estão em ordem linear (a leitura em blocos GX 8×4 saiu embaralhada em todas as amostras) e as linhas
    /// vêm de baixo para cima (a leitura direta mostrou os logotipos e textos invertidos na vertical).
    /// </summary>
    /// <summary>
    /// Decodifica uma imagem comprimida em blocos de 4×4 (formato 14 nos IGB do DW4; nomes .dds) com dois layouts candidatos:
    /// gameCube = false: DXT1 de PC (blocos em linhas, cores RGB565 little-endian, índices com o pixel 0 nos bits baixos);
    /// gameCube = true: CMPR da GX (blocos 4×4 agrupados em tiles 8×8, cores big-endian, pixel 0 nos bits altos).
    /// As linhas saem invertidas na vertical, como nas imagens indexadas. Qual layout é o certo se decide olhando.
    /// </summary>
    public static byte[]? DecodeBlockCompressed(IgbImage img, bool gameCube)
    {
        int w = img.Width, h = img.Height;
        if (w % 4 != 0 || h % 4 != 0 || img.Pixels.Length < w * h / 2) return null;
        var rgba = new byte[w * h * 4];
        int blocksX = w / 4, blocksY = h / 4, at = 0;
        var order = new List<(int bx, int by)>();
        if (gameCube)
        {
            for (int ty = 0; ty < blocksY; ty += 2)
                for (int tx = 0; tx < blocksX; tx += 2)
                    for (int sub = 0; sub < 4; sub++) order.Add((tx + (sub & 1), ty + (sub >> 1)));
        }
        else
            for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++) order.Add((bx, by));

        Span<byte> pal = stackalloc byte[16];
        foreach (var (bx, by) in order)
        {
            if (at + 8 > img.Pixels.Length) break;
            var blk = img.Pixels.AsSpan(at, 8);
            at += 8;
            if (bx >= blocksX || by >= blocksY) continue;
            int c0 = gameCube ? (blk[0] << 8) | blk[1] : blk[0] | (blk[1] << 8);
            int c1 = gameCube ? (blk[2] << 8) | blk[3] : blk[2] | (blk[3] << 8);
            Rgb565(c0, pal[..4]);
            Rgb565(c1, pal.Slice(4, 4));
            for (int k = 0; k < 3; k++)
            {
                if (c0 > c1)
                {
                    pal[8 + k] = (byte)((2 * pal[k] + pal[4 + k]) / 3);
                    pal[12 + k] = (byte)((pal[k] + 2 * pal[4 + k]) / 3);
                }
                else
                {
                    pal[8 + k] = (byte)((pal[k] + pal[4 + k]) / 2);
                    pal[12 + k] = 0;
                }
            }
            pal[11] = 255;
            pal[15] = (byte)(c0 > c1 ? 255 : 0);
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int code = gameCube
                        ? (blk[4 + y] >> (6 - 2 * x)) & 3
                        : (blk[4 + y] >> (2 * x)) & 3;
                    int px = 4 * bx + x, py = h - 1 - (4 * by + y);
                    pal.Slice(4 * code, 4).CopyTo(rgba.AsSpan(4 * (py * w + px)));
                }
        }
        return rgba;

        static void Rgb565(int c, Span<byte> o)
        {
            o[0] = (byte)(((c >> 11) & 31) * 255 / 31);
            o[1] = (byte)(((c >> 5) & 63) * 255 / 63);
            o[2] = (byte)((c & 31) * 255 / 31);
            o[3] = 255;
        }
    }

    /// <summary>Imagem para RGBA conforme o formato: indexada de 8 bits, ou formato 14 (blocos) com o layout escolhido.</summary>
    public static byte[]? ToRgba(IgbImage img, bool gameCubeBlocks = false) =>
        img.PixelFormat == 14 ? DecodeBlockCompressed(img, gameCubeBlocks) : IndexedToRgba(img);

    public static byte[]? IndexedToRgba(IgbImage img)
    {
        int w = img.Width, h = img.Height;
        if (img.BitsPerPixel != 8 || img.Pixels.Length < w * h || img.Palette.Length < 4 * 256) return null;
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                img.Palette.AsSpan(4 * img.Pixels[y * w + x], 4).CopyTo(rgba.AsSpan(4 * ((h - 1 - y) * w + x)));
        return rgba;
    }

    IEnumerable<IgbSlot?> Refs(IgbObject o) =>
        o.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(_le))).Where(s => s != null);

    IgbSlot? Slot(int index) => index >= 0 && index < _f.Slots.Count ? _f.Slots[index] : null;

    byte[] Memory(IgbFieldValue? field) =>
        field != null && Slot(field.AsInt32(_le)) is IgbMemoryBlock mb ? MemoryOf(mb) : [];

    byte[] MemoryOf(IgbMemoryBlock mb) => _data.AsSpan((int)mb.FileOffset, mb.Size).ToArray();

    int I32(byte[] b, int at) => _le ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at));
    int U16(byte[] b, int at) => _le ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at));
    float F32(byte[] b, int at) => _le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));

    static string Hex(byte[] b, int max) => Convert.ToHexString(b.AsSpan(0, Math.Min(max, b.Length)));

    // Matrizes do Alchemy em linhas, com a translação na última linha: v' = v · M (igb-blender, sg_classes.py).
    static (float, float, float) Apply(float[]? m, float x, float y, float z, float w) => m == null ? (x, y, z) : (
        x * m[0] + y * m[4] + z * m[8] + w * m[12],
        x * m[1] + y * m[5] + z * m[9] + w * m[13],
        x * m[2] + y * m[6] + z * m[10] + w * m[14]);

    static float[] Multiply(float[] a, float[] b)
    {
        var r = new float[16];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                for (int k = 0; k < 4; k++)
                    r[4 * i + j] += a[4 * i + k] * b[4 * k + j];
        return r;
    }
}
