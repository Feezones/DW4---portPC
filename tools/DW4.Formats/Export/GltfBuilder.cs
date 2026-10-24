using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DW4.Formats.Export;

/// <summary>
/// Montador mínimo de glTF 2.0 (JSON + .bin externo), sem dependências. Só o necessário para malhas com pele,
/// esqueleto, materiais com textura e animações por amostras. Especificação: https://registry.khronos.org/glTF/specs/2.0/
/// </summary>
public sealed class GltfBuilder
{
    public const int Float = 5126, UnsignedByte = 5121, UnsignedShort = 5123, UnsignedInt = 5125;
    const int ArrayBuffer = 34962, ElementArrayBuffer = 34963;

    readonly MemoryStream _bin = new();
    readonly JsonArray _bufferViews = [], _accessors = [], _nodes = [], _meshes = [], _skins = [], _materials = [],
        _textures = [], _images = [], _samplers = [], _animations = [];
    readonly JsonArray _sceneNodes = [];

    public int AddNode(JsonObject node)
    {
        _nodes.Add(node);
        return _nodes.Count - 1;
    }

    public JsonObject Node(int index) => (JsonObject)_nodes[index]!;

    public void AddRootNode(int index) => _sceneNodes.Add(index);

    public int AddMesh(JsonObject mesh)
    {
        _meshes.Add(mesh);
        return _meshes.Count - 1;
    }

    public int AddSkin(JsonObject skin)
    {
        _skins.Add(skin);
        return _skins.Count - 1;
    }

    public int AddAnimation(JsonObject anim)
    {
        _animations.Add(anim);
        return _animations.Count - 1;
    }

    /// <summary>Material com textura PNG externa (ou sem textura quando uri é nulo).</summary>
    public int AddMaterial(string name, string? pngUri, bool alphaMask)
    {
        var mat = new JsonObject
        {
            ["name"] = name,
            ["pbrMetallicRoughness"] = new JsonObject { ["metallicFactor"] = 0, ["roughnessFactor"] = 1 },
            ["doubleSided"] = false,
        };
        if (pngUri != null)
        {
            if (_samplers.Count == 0) _samplers.Add(new JsonObject { ["magFilter"] = 9729, ["minFilter"] = 9729, ["wrapS"] = 10497, ["wrapT"] = 10497 });
            _images.Add(new JsonObject { ["uri"] = pngUri });
            _textures.Add(new JsonObject { ["sampler"] = 0, ["source"] = _images.Count - 1 });
            ((JsonObject)mat["pbrMetallicRoughness"]!)["baseColorTexture"] = new JsonObject { ["index"] = _textures.Count - 1 };
        }
        if (alphaMask)
        {
            mat["alphaMode"] = "MASK";
            mat["alphaCutoff"] = 0.5;
        }
        _materials.Add(mat);
        return _materials.Count - 1;
    }

    public int AddFloats(IReadOnlyList<float> values, int components, string type, bool vertexAttribute, bool minMax = false)
    {
        int view = AddView(w => { foreach (var v in values) w.Write(v); }, vertexAttribute ? ArrayBuffer : null);
        var acc = new JsonObject
        {
            ["bufferView"] = view,
            ["componentType"] = Float,
            ["count"] = values.Count / components,
            ["type"] = type,
        };
        if (minMax && values.Count > 0)
        {
            var min = new JsonArray();
            var max = new JsonArray();
            for (int c = 0; c < components; c++)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = c; i < values.Count; i += components) { lo = Math.Min(lo, values[i]); hi = Math.Max(hi, values[i]); }
                min.Add(lo);
                max.Add(hi);
            }
            acc["min"] = min;
            acc["max"] = max;
        }
        _accessors.Add(acc);
        return _accessors.Count - 1;
    }

    public int AddJoints(IReadOnlyList<int> joints4)
    {
        int view = AddView(w => { foreach (var j in joints4) w.Write((ushort)j); }, ArrayBuffer);
        _accessors.Add(new JsonObject { ["bufferView"] = view, ["componentType"] = UnsignedShort, ["count"] = joints4.Count / 4, ["type"] = "VEC4" });
        return _accessors.Count - 1;
    }

    public int AddIndices(IReadOnlyList<int> indices)
    {
        bool wide = indices.Count > 0 && indices.Max() > ushort.MaxValue;
        int view = AddView(w => { foreach (var i in indices) { if (wide) w.Write((uint)i); else w.Write((ushort)i); } }, ElementArrayBuffer);
        _accessors.Add(new JsonObject { ["bufferView"] = view, ["componentType"] = wide ? UnsignedInt : UnsignedShort, ["count"] = indices.Count, ["type"] = "SCALAR" });
        return _accessors.Count - 1;
    }

    int AddView(Action<BinaryWriter> write, int? target)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        long start = _bin.Length;
        using (var w = new BinaryWriter(_bin, Encoding.UTF8, leaveOpen: true)) write(w);
        var view = new JsonObject { ["buffer"] = 0, ["byteOffset"] = start, ["byteLength"] = _bin.Length - start };
        if (target != null) view["target"] = target;
        _bufferViews.Add(view);
        return _bufferViews.Count - 1;
    }

    /// <summary>Grava nome.gltf e nome.bin na pasta indicada.</summary>
    public void Save(string dir, string name)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        File.WriteAllBytes(Path.Combine(dir, name + ".bin"), _bin.ToArray());
        var root = new JsonObject
        {
            ["asset"] = new JsonObject { ["version"] = "2.0", ["generator"] = "dw4 igb-gltf" },
            ["scene"] = 0,
            ["scenes"] = new JsonArray(new JsonObject { ["nodes"] = _sceneNodes }),
            ["nodes"] = _nodes,
            ["buffers"] = new JsonArray(new JsonObject { ["uri"] = name + ".bin", ["byteLength"] = _bin.Length }),
            ["bufferViews"] = _bufferViews,
            ["accessors"] = _accessors,
        };
        if (_meshes.Count > 0) root["meshes"] = _meshes;
        if (_skins.Count > 0) root["skins"] = _skins;
        if (_materials.Count > 0) root["materials"] = _materials;
        if (_textures.Count > 0) { root["textures"] = _textures; root["images"] = _images; root["samplers"] = _samplers; }
        if (_animations.Count > 0) root["animations"] = _animations;
        File.WriteAllText(Path.Combine(dir, name + ".gltf"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
