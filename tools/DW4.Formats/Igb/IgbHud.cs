using System.Buffers.Binary;
using System.Text.Json.Nodes;

namespace DW4.Formats.Igb;

/// <summary>Uma imagem do HUD já em RGBA (linhas de cima para baixo), com o nome de arquivo usado no JSON.</summary>
public sealed record HudImage(string File, string Name, int Width, int Height, byte[] Rgba);

/// <summary>
/// Atores de interface de um IGB de fase (i_dmgnum, i_stpnl03…) com o grafo de nós, para a fatia desenhar o HUD como o jogo.
/// Levantamento de 2026-10-25 no #179 (docs/gameplay/fase4-inventario-interface.md):
///   - raiz do ator = igAttrSet com o nome do ator (FATO: i_dmgnum #25434 sob a camada "WN00", i_stpnl03 #31271 sob "WN01");
///   - igTransformSequence1_5: @2 translação (igVec3fList), @3 rotação (igQuaternionfList, x y z w), @4 escala, @11 tempos
///     (igLongList, ns), @15 máscara (bit 0 translação, bit 1 rotação, bit 3 escala; FATO pelas 7 combinações do #179),
///     @16 três bytes de interpolação, @18 duração em ns. @6, @7, @8 e @10 saem crus (significado DESCONHECIDO);
///   - texturas por nome: beTextureCtrlAttr(nome, igAttrSet) no IGB da fase → beTextureCtrlData(nome → igTextureAttr →
///     igImage) no pacote de idioma (usa_pack da fase, FATO: stpnlwn01.png do i_stpnl03 só existe no area #9);
///   - medidores: beMeterCtrlInfo → beMeterCtrlOneData [nome, nó, _maxValueF, _ratioTimeValueF, _addMaxValueF, _nowValueF,
///     _dspValueF, _nonZeroTimeL, _nonMaxTimeL, _ketaKazu, _isChange, _isZeroSupp, _isNotchMode] (nomes na ordem do DOL);
///   - números: beNumberCtrlInfo → beNumberCtrlInfoRam [_geometry, _offsetNext, _maxValue, _mode, _numList]; cada dígito é
///     um beNumVerData com beNumVerInsideData(_index = vértice, _kind = canto).
/// As matrizes saem como no Alchemy: em linhas, translação na última linha (v' = v · M). As UV saem cruas (origem embaixo,
/// como no IGB); as PNG saem com a primeira linha no topo, então quem desenha usa (u, 1 − v).
/// </summary>
public sealed class IgbHud
{
    readonly IgbFile _f;
    readonly byte[] _data;
    readonly bool _le;
    readonly IgbScene _scene;
    readonly Dictionary<int, JsonObject> _nodes = [];
    readonly Dictionary<int, string> _textureOf = [];
    readonly Dictionary<string, HudImage> _images = [];
    readonly IgbFile? _pack;
    readonly byte[]? _packData;
    readonly IgbScene? _packScene;
    readonly Dictionary<string, int> _packImage = [];

    public List<string> Notes { get; } = [];
    public IReadOnlyCollection<HudImage> Images => _images.Values;

    IgbHud(IgbFile f, byte[] data, IgbFile? pack, byte[]? packData)
    {
        _f = f;
        _data = data;
        _le = f.Header.LittleEndian;
        _scene = IgbScene.Read(f, data);
        _pack = pack;
        _packData = packData;
        if (pack != null && packData != null)
        {
            _packScene = IgbScene.Read(pack, packData);
            bool ple = pack.Header.LittleEndian;
            foreach (var o in pack.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beTextureCtrlData"))
            {
                var name = Text(o, 2);
                var r = o.Fields.FirstOrDefault(v => v.Desc.Slot == 3 && v.Desc.ShortName == "ObjectRef");
                if (name == null || r == null || pack.Slots.ElementAtOrDefault(r.AsInt32(ple)) is not IgbObject attr) continue;
                int img = _packScene.FindImage(attr);
                if (img >= 0) _packImage.TryAdd(name, img);
            }
        }
        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beTextureCtrlAttr"))
        {
            var name = Text(o, 2);
            int target = Ref(o, 3);
            if (name != null && target >= 0) _textureOf[target] = name;
        }
    }

    /// <summary>Exporta os atores pedidos; <paramref name="pack"/> é o pacote de idioma da fase (texturas por nome), se houver.</summary>
    public static (JsonObject Json, IgbHud Hud) Export(IgbFile f, byte[] data, IgbFile? pack, byte[]? packData, IEnumerable<string> actors)
    {
        var h = new IgbHud(f, data, pack, packData);
        var list = new JsonArray();
        foreach (var name in actors)
        {
            var root = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "igAttrSet") && Text(o, 2) == name);
            if (root == null) { h.Notes.Add($"ator {name}: igAttrSet não encontrado"); continue; }
            h.Visit(root);
            list.Add(new JsonObject { ["name"] = name, ["root"] = root.Index });
        }
        var json = new JsonObject
        {
            ["littleEndian"] = h._le,
            ["actors"] = list,
            ["nodes"] = new JsonObject(h._nodes.OrderBy(kv => kv.Key).Select(kv => KeyValuePair.Create(kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), (JsonNode?)kv.Value))),
            ["meters"] = h.Meters(),
            ["numbers"] = h.Numbers(),
            ["images"] = new JsonArray(h._images.Values.Select(i => (JsonNode?)new JsonObject { ["file"] = i.File, ["name"] = i.Name, ["width"] = i.Width, ["height"] = i.Height }).ToArray()),
            ["notes"] = new JsonArray(h.Notes.Select(n => (JsonNode?)n).ToArray()),
        };
        return (json, h);
    }

    void Visit(IgbObject node)
    {
        if (_nodes.ContainsKey(node.Index)) return;
        var j = new JsonObject { ["type"] = node.TypeName, ["name"] = Text(node, 2) ?? "" };
        _nodes[node.Index] = j;

        if (_f.Is(node, "igTransform"))
        {
            if (Matrix(node, 8) is { } m) j["matrix"] = Floats(m);
            if (Obj(Ref(node, 11)) is { } seq && _f.Is(seq, "igTransformSequence1_5")) j["sequence"] = Sequence(seq);
        }
        if (_f.Is(node, "igBillboard"))
            j["billboard"] = new JsonObject { ["mode"] = Int(node, 8), ["axis"] = Floats(Vec(node, 9, 3)), ["up"] = Floats(Vec(node, 10, 3)) };
        if (_f.Is(node, "igSwitch")) j["switchBits"] = Bits(Obj(Ref(node, 8)));
        if (node.TypeName == "beMeterCtrlNode")
            j["meter"] = new JsonObject { ["isMeter"] = Bool(node, 8), ["times"] = Longs(Obj(Ref(node, 9))), ["numberOffset"] = Floats(Vec(node, 10, 3)) };
        if (_f.Is(node, "igAttrSet"))
        {
            var attrs = new JsonArray();
            foreach (var a in _scene.AttrList(node)) attrs.Add(Attr(a));
            j["attrs"] = attrs;
            if (_textureOf.TryGetValue(node.Index, out var tex)) j["texture"] = PackTexture(tex);
        }
        if (_f.Is(node, "igGeometry"))
        {
            var meshes = new JsonArray();
            foreach (var a in _scene.AttrList(node))
            {
                if (_f.Is(a, "igGeometryAttr")) meshes.Add(Mesh(a));
                else meshes.Add(Attr(a));
            }
            j["meshes"] = meshes;
        }

        var children = new JsonArray();
        if (Obj(Ref(node, 7)) is { } list && _f.Is(list, "igObjectList"))
            foreach (var c in _scene.ObjectList(list).Where(c => _f.Is(c, "igNode")))
            {
                children.Add(c.Index);
                Visit(c);
            }
        j["children"] = children;
    }

    JsonObject Attr(IgbObject a)
    {
        var j = new JsonObject { ["type"] = a.TypeName };
        switch (a.TypeName)
        {
            case "igTextureBindAttr":
                int img = _scene.FindImage(a);
                j["texture"] = img >= 0 ? StageTexture(img) : null;
                break;
            case "igBlendFunctionAttr":
                // GL: 4 = SRC_ALPHA, 5 = ONE_MINUS_SRC_ALPHA (os HUD do #179 usam 4/5).
                j["src"] = Int(a, 4); j["dst"] = Int(a, 5); j["equation"] = Int(a, 6);
                break;
            case "igProjectionMatrixAttr":
                if (Matrix(a, 4) is { } p) j["matrix"] = Floats(p);
                break;
            case "igTextureMatrixStateAttr":
                j["enabled"] = Bool(a, 4); j["unit"] = Int(a, 5);
                break;
            case "igColorAttr":
                j["color"] = Floats(Vec(a, 4, 4));
                break;
        }
        return j;
    }

    JsonObject Mesh(IgbObject geom)
    {
        var part = _scene.ReadGeometry(geom);
        var j = new JsonObject { ["geometry"] = geom.Index, ["primitive"] = part.PrimitiveType };
        j["positions"] = Floats(part.Positions.SelectMany(p => new[] { p.X, p.Y, p.Z }));
        if (part.TexCoords.Count == part.Positions.Count) j["uvs"] = Floats(part.TexCoords.SelectMany(t => new[] { t.U, t.V }));
        if (part.Colors.Count == part.Positions.Count) j["colors"] = new JsonArray(part.Colors.Select(c => (JsonNode?)c).ToArray());
        j["triangles"] = new JsonArray(part.Triangles.Select(t => (JsonNode?)t).ToArray());
        if (part.Notes.Any(n => n.Contains("não decodificado") || n.Contains("não tratado"))) j["notes"] = new JsonArray(part.Notes.Select(n => (JsonNode?)n).ToArray());
        return j;
    }

    JsonObject Sequence(IgbObject s)
    {
        var times = LongList(Obj(Ref(s, 11))).Select(t => t / 1e9).ToArray();
        var j = new JsonObject
        {
            ["mask"] = Byte(s, 15),
            ["interpolation"] = new JsonArray((s.Fields.FirstOrDefault(v => v.Desc.Slot == 16)?.Raw ?? []).Select(b => (JsonNode?)(int)b).ToArray()),
            ["start"] = Long(s, 17) / 1e9,
            ["duration"] = Long(s, 18) / 1e9,
            ["times"] = Floats(times.Select(t => (float)t)),
            ["raw6"] = Double(s, 6),
            ["raw7"] = Bool(s, 7),
            ["raw8"] = Int(s, 8),
            ["raw10"] = Floats(Vec(s, 10, 3)),
        };
        if (Obj(Ref(s, 2)) is { } t) j["translation"] = Floats(FloatList(t));
        if (Obj(Ref(s, 3)) is { } r) j["rotation"] = Floats(FloatList(r));
        if (Obj(Ref(s, 4)) is { } sc) j["scale"] = Floats(FloatList(sc));
        foreach (int slot in new[] { 5, 12, 13, 14, 19 })
            if (Ref(s, slot) >= 0) Notes.Add($"sequência #{s.Index}: @{slot} preenchido ({Obj(Ref(s, slot))?.TypeName}), não exportado");
        return j;
    }

    JsonArray Meters()
    {
        var list = new JsonArray();
        foreach (var info in _f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beMeterCtrlInfo"))
        {
            var data = new JsonArray();
            if (Obj(Ref(info, 7)) is { } l && _f.Is(l, "igObjectList"))
                foreach (var d in _scene.ObjectList(l).Where(o => o.TypeName == "beMeterCtrlOneData"))
                    data.Add(new JsonObject
                    {
                        ["name"] = Text(d, 2), ["node"] = Ref(d, 3), ["maxValue"] = Float(d, 4), ["ratioTimeValue"] = Float(d, 5),
                        ["addMaxValue"] = Float(d, 6), ["nowValue"] = Float(d, 7), ["dspValue"] = Float(d, 8),
                        ["nonZeroTime"] = Long(d, 9) / 1e9, ["nonMaxTime"] = Long(d, 10) / 1e9, ["ketaKazu"] = Int(d, 11),
                        ["isChange"] = Bool(d, 12), ["isZeroSupp"] = Bool(d, 13), ["isNotchMode"] = Bool(d, 14),
                    });
            list.Add(new JsonObject { ["name"] = Text(info, 2), ["data"] = data });
        }
        return list;
    }

    JsonArray Numbers()
    {
        var list = new JsonArray();
        foreach (var info in _f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beNumberCtrlInfo"))
        {
            var rams = new JsonArray();
            if (Obj(Ref(info, 6)) is { } l && _f.Is(l, "igObjectList"))
                foreach (var r in _scene.ObjectList(l).Where(o => o.TypeName == "beNumberCtrlInfoRam"))
                {
                    var digits = new JsonArray();
                    if (Obj(Ref(r, 15)) is { } dl && _f.Is(dl, "igObjectList"))
                        foreach (var d in _scene.ObjectList(dl))
                        {
                            var verts = new JsonArray();
                            if (Obj(Ref(d, 3)) is { } vl && _f.Is(vl, "igObjectList"))
                                foreach (var v in _scene.ObjectList(vl)) verts.Add(new JsonArray(Int(v, 2), Int(v, 3)));
                            digits.Add(new JsonObject { ["disp"] = Int(d, 2), ["vertices"] = verts });
                        }
                    rams.Add(new JsonObject { ["geometry"] = Ref(r, 11), ["offsetNext"] = Float(r, 12), ["maxValue"] = Int(r, 13), ["mode"] = Int(r, 14), ["digits"] = digits });
                }
            list.Add(new JsonObject { ["name"] = Text(info, 2), ["rams"] = rams });
        }
        return list;
    }

    // Imagem do próprio IGB da fase (igTextureBindAttr com igImage): o nome se repete (dois "numdmg.png"), então vai o índice.
    string? StageTexture(int index)
    {
        if (!_scene.Images.TryGetValue(index, out var img)) return null;
        var file = $"img{index:D5}_{Safe(img.Name)}";
        if (!_images.ContainsKey(file) && IgbScene.ToRgba(img) is { } rgba) _images[file] = new HudImage(file, img.Name, img.Width, img.Height, rgba);
        return _images.ContainsKey(file) ? file : null;
    }

    // Textura por nome no pacote de idioma (beTextureCtrlData); sem pacote ou sem o nome, sai só o nome com a nota.
    string PackTexture(string name)
    {
        if (_packScene != null && _packImage.TryGetValue(name, out int idx) && _packScene.Images.TryGetValue(idx, out var img))
        {
            var file = $"pack_{Safe(name)}";
            if (!_images.ContainsKey(file) && IgbScene.ToRgba(img) is { } rgba) _images[file] = new HudImage(file, name, img.Width, img.Height, rgba);
            if (_images.ContainsKey(file)) return file;
        }
        if (!Notes.Contains($"textura {name}: não achada no pacote de idioma")) Notes.Add($"textura {name}: não achada no pacote de idioma");
        return name;
    }

    static string Safe(string name) => string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_'));

    // --- leitura de campos e listas ---

    static string? Text(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName == "String")?.Text;
    int Ref(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName is "ObjectRef" or "MemoryRef")?.AsInt32(_le) ?? -1;
    IgbObject? Obj(int index) => index >= 0 && index < _f.Slots.Count ? _f.Slots[index] as IgbObject : null;
    byte[] Raw(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Raw ?? [];
    int Int(IgbObject o, int slot) { var r = Raw(o, slot); return r.Length == 4 ? (_le ? BinaryPrimitives.ReadInt32LittleEndian(r) : BinaryPrimitives.ReadInt32BigEndian(r)) : 0; }
    static int Byte(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Raw is [var b] ? b : 0;
    static bool Bool(IgbObject o, int slot) => Byte(o, slot) != 0;
    float Float(IgbObject o, int slot) { var r = Raw(o, slot); return r.Length == 4 ? F32(r, 0) : 0; }
    long Long(IgbObject o, int slot) { var r = Raw(o, slot); return r.Length == 8 ? (_le ? BinaryPrimitives.ReadInt64LittleEndian(r) : BinaryPrimitives.ReadInt64BigEndian(r)) : 0; }
    double Double(IgbObject o, int slot) { var r = Raw(o, slot); return r.Length == 8 ? (_le ? BinaryPrimitives.ReadDoubleLittleEndian(r) : BinaryPrimitives.ReadDoubleBigEndian(r)) : 0; }
    float[] Vec(IgbObject o, int slot, int n) { var r = Raw(o, slot); return r.Length >= 4 * n ? Enumerable.Range(0, n).Select(i => F32(r, 4 * i)).ToArray() : []; }
    float[]? Matrix(IgbObject o, int slot) { var v = Vec(o, slot, 16); return v.Length == 16 ? v : null; }
    float F32(byte[] b, int at) => _le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));

    byte[] ListMemory(IgbObject list, out int count)
    {
        count = Int(list, 2);
        int mr = Ref(list, 4);
        return mr >= 0 && mr < _f.Slots.Count && _f.Slots[mr] is IgbMemoryBlock mb ? _data.AsSpan((int)mb.FileOffset, mb.Size).ToArray() : [];
    }

    // igVec3fList / igQuaternionfList: floats contíguos (3 ou 4 por item).
    float[] FloatList(IgbObject list)
    {
        var mem = ListMemory(list, out int count);
        int per = count > 0 ? mem.Length / 4 / count : 0;
        return Enumerable.Range(0, count * per).Select(i => F32(mem, 4 * i)).ToArray();
    }

    // igLongList: int64 contíguos (tempos em ns).
    long[] LongList(IgbObject? list)
    {
        if (list == null) return [];
        var mem = ListMemory(list, out int count);
        count = Math.Min(count, mem.Length / 8);
        return Enumerable.Range(0, count).Select(i => _le ? BinaryPrimitives.ReadInt64LittleEndian(mem.AsSpan(8 * i)) : BinaryPrimitives.ReadInt64BigEndian(mem.AsSpan(8 * i))).ToArray();
    }

    JsonArray Longs(IgbObject? list) => Floats(LongList(list).Select(t => (float)(t / 1e9)));

    // igBitMask: Int@2 = palavras de 32 bits, UnsignedInt@5 = quantidade de bits (FATO: 1 palavra e 11 bits no switch do
    // i_dmgnum, 4 no do i_stpnl03, todos desligados no arquivo; quem liga cada filho é o jogo).
    JsonArray Bits(IgbObject? mask)
    {
        if (mask == null) return [];
        var mem = ListMemory(mask, out _);
        int count = Int(mask, 5);
        var bits = new JsonArray();
        for (int i = 0; i < count && i / 32 * 4 + 4 <= mem.Length; i++)
        {
            uint w = _le ? BinaryPrimitives.ReadUInt32LittleEndian(mem.AsSpan(i / 32 * 4)) : BinaryPrimitives.ReadUInt32BigEndian(mem.AsSpan(i / 32 * 4));
            bits.Add(((w >> (i % 32)) & 1) != 0);
        }
        return bits;
    }

    static JsonArray Floats(IEnumerable<float> values) => new(values.Select(v => (JsonNode?)Math.Round((double)v, 6)).ToArray());
}
