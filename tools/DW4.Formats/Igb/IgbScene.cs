using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Uma geometria (igGeometryAttr) já resolvida, em coordenadas do mundo quando havia igTransform acima dela.</summary>
public sealed class IgbMeshPart
{
    public int GeometryIndex { get; init; }
    public int ImageIndex { get; set; } = -1;
    public int PrimitiveType { get; set; }
    public int VertexCount { get; set; }
    public uint VertexFormat { get; set; }
    public List<(float X, float Y, float Z)> Positions { get; } = [];
    public List<(float X, float Y, float Z)> Normals { get; } = [];
    public List<(float U, float V)> TexCoords { get; } = [];
    public List<uint> Colors { get; } = [];
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

    IgbScene(IgbFile f, byte[] data)
    {
        _f = f;
        _data = data;
        _le = f.Header.LittleEndian;
    }

    public static IgbScene Read(IgbFile f, byte[] data)
    {
        var s = new IgbScene(f, data);
        var roots = new List<IgbObject>();
        foreach (var si in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igSceneInfo")))
            foreach (var r in s.Refs(si))
                if (f.Is(r, "igNode") && !roots.Contains((IgbObject)r!)) roots.Add((IgbObject)r!);

        if (roots.Count > 0)
        {
            s.Notes.Add($"{roots.Count} raiz(es) de cena a partir de igSceneInfo");
            foreach (var root in roots) s.Visit(root, null, -1, []);
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

    List<IgbObject> AttrList(IgbObject attrSet)
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
    List<IgbObject> ObjectList(IgbObject list)
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

    // igTextureBindAttr -> igTextureAttr -> igImage (até três níveis de ObjectRef).
    int FindImage(IgbObject start)
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

    void AddGeometry(IgbObject geom, float[]? world, int image)
    {
        var part = new IgbMeshPart { GeometryIndex = geom.Index, ImageIndex = image };
        IgbObject? va = null, ia = null, pla = null;
        foreach (var r in Refs(geom))
        {
            if (_f.Is(r, "igVertexArray")) va = (IgbObject)r!;
            else if (_f.Is(r, "igIndexArray")) ia = (IgbObject)r!;
            else if (_f.Is(r, "igPrimLengthArray")) pla = (IgbObject)r!;
        }
        part.PrimitiveType = geom.Fields.FirstOrDefault(v => v.Desc.ShortName == "Enum")?.AsInt32(_le) ?? -1;
        if (va == null) { part.Notes.Add("sem igVertexArray"); Parts.Add(part); return; }

        part.VertexCount = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "UnsignedInt")?.AsInt32(_le) ?? 0;
        var fmt = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "Struct");
        if (fmt != null && fmt.Raw.Length >= 4) part.VertexFormat = (uint)I32(fmt.Raw, 0);
        int n = part.VertexCount;

        var extRef = va.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef");
        var ext = Memory(extRef);
        if (ext.Length is not (76 or 80))
        {
            part.Notes.Add($"vértices sem igExternalIndexedEntry (bloco de {ext.Length} B, passo {(n > 0 ? ext.Length / (double)n : 0):0.##}): não decodificado");
            Parts.Add(part);
            return;
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
        Parts.Add(part);
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
    /// Converte uma imagem indexada de 8 bits com paleta de 4 bytes para RGBA. Com tiled = true, lê os índices em
    /// blocos de 8×4 (layout C8 da GX do GameCube); com false, em linhas. Qual dos dois é o certo se decide olhando.
    /// </summary>
    public static byte[]? IndexedToRgba(IgbImage img, bool tiled)
    {
        int w = img.Width, h = img.Height;
        if (img.BitsPerPixel != 8 || img.Pixels.Length < w * h || img.Palette.Length < 4 * 256) return null;
        var rgba = new byte[w * h * 4];
        int src = 0;
        if (tiled)
        {
            for (int by = 0; by < h; by += 4)
                for (int bx = 0; bx < w; bx += 8)
                    for (int y = by; y < by + 4; y++)
                        for (int x = bx; x < bx + 8; x++, src++)
                            if (x < w && y < h) Put(x, y, img.Pixels[src]);
        }
        else for (int i = 0; i < w * h; i++) Put(i % w, i / w, img.Pixels[i]);
        return rgba;

        void Put(int x, int y, byte index) => img.Palette.AsSpan(4 * index, 4).CopyTo(rgba.AsSpan(4 * (y * w + x)));
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
