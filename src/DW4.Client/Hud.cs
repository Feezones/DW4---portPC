using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace DW4.Client;

/// <summary>
/// HUD do jogo a partir de <c>dw4 igb-hud ndmw.afs 179 area.afs 9 &lt;fatia&gt;\hud</c> (atores i_* do IGB da fase com o
/// grafo de nós, sequências, medidores e números; texturas do usa_pack). Levantamento: docs/gameplay/fase4-inventario-interface.md.
/// Regras usadas (FATO salvo indicação):
///   - matrizes do Alchemy em linhas com a translação na última linha; quatérnios conjugados para a Godot (como nos personagens);
///   - JOINTSET "" a b das ações põe o tempo (quadros de 60 Hz) de todas as igTransformSequence do ator;
///   - nós sob igTextureMatrixStateAttr são matriz de TEXTURA: mexem na UV das geometrias abaixo, não na posição;
///   - número: o dígito k (0 = unidade) é um quad cuja UV anda U += dígito × _offsetNext na faixa de 10 dígitos;
///   - painéis de status do jogador n em V&lt;−260 + 150n, −160, 0&gt; numa projeção ortográfica 640 × 480 (Lua: GENERATE).
/// </summary>
public sealed class HudData
{
    public sealed record Seq(int Mask, float Duration, float[] Times, Vector3[] T, Quaternion[] R, Vector3[] S);
    public sealed record MeshData(Vector3[] Positions, Vector2[] Uvs, int[] Triangles);
    public sealed record NodeData(int Id, string Type, string Name, Transform3D? Matrix, Seq? Sequence, int[] Children,
        string? Texture, bool TextureMatrix, MeshData[] Meshes, bool Billboard);
    public sealed record MeterData(string Name, int Node, float MaxValue, float NonZeroTime, float NonMaxTime, bool Notch);
    public sealed record NumberRam(int Geometry, float OffsetNext, int MaxValue, int[][] DigitVertices, int[][] DigitKinds);

    public string Dir { get; }
    public Dictionary<int, NodeData> Nodes { get; } = [];
    public Dictionary<string, int> Actors { get; } = [];
    public Dictionary<string, List<MeterData>> Meters { get; } = [];
    public Dictionary<string, List<NumberRam>> Numbers { get; } = [];
    readonly Dictionary<string, Texture2D?> _textures = [];

    HudData(string dir) => Dir = dir;

    public static HudData? Load(string dir, List<string> problems, string pattern = "*_hud.json", string? missing = null)
    {
        var file = System.IO.Directory.Exists(dir) ? System.IO.Directory.EnumerateFiles(dir, pattern).FirstOrDefault() : null;
        if (file == null) { problems.Add(missing ?? $"falta o *_hud.json (dw4 igb-hud ndmw.afs 179 area.afs 9 <fatia>\\hud): HUD em texto"); return null; }
        var h = new HudData(dir);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
        var root = doc.RootElement;
        foreach (var a in root.GetProperty("actors").EnumerateArray()) h.Actors[a.GetProperty("name").GetString()!] = a.GetProperty("root").GetInt32();
        foreach (var p in root.GetProperty("nodes").EnumerateObject()) h.Nodes[int.Parse(p.Name)] = ReadNode(int.Parse(p.Name), p.Value);
        foreach (var m in root.GetProperty("meters").EnumerateArray())
            h.Meters[m.GetProperty("name").GetString()!] = m.GetProperty("data").EnumerateArray().Select(d => new MeterData(
                d.GetProperty("name").GetString()!, d.GetProperty("node").GetInt32(), d.GetProperty("maxValue").GetSingle(),
                d.GetProperty("nonZeroTime").GetSingle(), d.GetProperty("nonMaxTime").GetSingle(), d.GetProperty("isNotchMode").GetBoolean())).ToList();
        foreach (var n in root.GetProperty("numbers").EnumerateArray())
            h.Numbers[n.GetProperty("name").GetString()!] = n.GetProperty("rams").EnumerateArray().Select(r =>
            {
                var digits = r.GetProperty("digits").EnumerateArray().Select(d => d.GetProperty("vertices").EnumerateArray().Select(v => (v[0].GetInt32(), v[1].GetInt32())).ToArray()).ToArray();
                return new NumberRam(r.GetProperty("geometry").GetInt32(), r.GetProperty("offsetNext").GetSingle(), r.GetProperty("maxValue").GetInt32(),
                    digits.Select(d => d.Select(v => v.Item1).ToArray()).ToArray(), digits.Select(d => d.Select(v => v.Item2).ToArray()).ToArray());
            }).ToList();
        return h;
    }

    static NodeData ReadNode(int id, JsonElement e)
    {
        Transform3D? matrix = e.TryGetProperty("matrix", out var m) ? FromAlchemy(Floats(m)) : null;
        Seq? seq = null;
        if (e.TryGetProperty("sequence", out var s))
        {
            var t = s.TryGetProperty("translation", out var tt) ? Vec3s(Floats(tt)) : [];
            var r = s.TryGetProperty("rotation", out var rr) ? Quats(Floats(rr)) : [];
            var sc = s.TryGetProperty("scale", out var ss) ? Vec3s(Floats(ss)) : [];
            seq = new Seq(s.GetProperty("mask").GetInt32(), s.GetProperty("duration").GetSingle(), Floats(s.GetProperty("times")), t, r, sc);
        }
        string? texture = e.TryGetProperty("texture", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() : null;
        bool texMatrix = false;
        if (e.TryGetProperty("attrs", out var attrs))
            foreach (var a in attrs.EnumerateArray())
            {
                var type = a.GetProperty("type").GetString();
                if (type == "igTextureBindAttr" && texture == null && a.TryGetProperty("texture", out var bt) && bt.ValueKind == JsonValueKind.String) texture = bt.GetString();
                if (type == "igTextureMatrixStateAttr" && a.GetProperty("enabled").GetBoolean()) texMatrix = true;
            }
        var meshes = new List<MeshData>();
        if (e.TryGetProperty("meshes", out var ms))
            foreach (var mm in ms.EnumerateArray().Where(x => x.TryGetProperty("positions", out _)))
            {
                var pos = Vec3s(Floats(mm.GetProperty("positions")));
                var uv = mm.TryGetProperty("uvs", out var u) ? Floats(u) : new float[pos.Length * 2];
                // UV do IGB com origem embaixo; as PNG saem com a primeira linha no topo.
                var uvs = Enumerable.Range(0, pos.Length).Select(i => new Vector2(uv[2 * i], 1 - uv[2 * i + 1])).ToArray();
                meshes.Add(new MeshData(pos, uvs, mm.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray()));
            }
        return new NodeData(id, e.GetProperty("type").GetString()!, e.GetProperty("name").GetString() ?? "", matrix, seq,
            e.GetProperty("children").EnumerateArray().Select(c => c.GetInt32()).ToArray(), texture, texMatrix, meshes.ToArray(), e.TryGetProperty("billboard", out _));
    }

    static float[] Floats(JsonElement a) => a.EnumerateArray().Select(x => x.GetSingle()).ToArray();
    static Vector3[] Vec3s(float[] f) => Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
    // Conjugado: mesma convenção dos quatérnios dos personagens (docs/formatos/personagens-e-fases.md).
    static Quaternion[] Quats(float[] f) => Enumerable.Range(0, f.Length / 4).Select(i => new Quaternion(-f[4 * i], -f[4 * i + 1], -f[4 * i + 2], f[4 * i + 3]).Normalized()).ToArray();

    /// <summary>Matriz do Alchemy (linhas, v' = v · M) → Transform3D (colunas da base = linhas de M).</summary>
    public static Transform3D FromAlchemy(float[] m) => new(new Basis(new Vector3(m[0], m[1], m[2]), new Vector3(m[4], m[5], m[6]), new Vector3(m[8], m[9], m[10])), new Vector3(m[12], m[13], m[14]));

    /// <summary>Transformação de um nó no tempo t (s): a sequência substitui só os canais da máscara (bit 0 T, 1 R, 3 S).</summary>
    public static Transform3D Evaluate(NodeData n, float t)
    {
        var baseT = n.Matrix ?? Transform3D.Identity;
        if (n.Sequence is not { } s || s.Times.Length == 0) return baseT;
        var origin = baseT.Origin;
        var scale = baseT.Basis.Scale;
        var rot = scale.X * scale.Y * scale.Z != 0 ? baseT.Basis.Orthonormalized().GetRotationQuaternion() : Quaternion.Identity;
        int i = 0;
        while (i + 1 < s.Times.Length && s.Times[i + 1] <= t) i++;
        int j = Math.Min(i + 1, s.Times.Length - 1);
        float span = s.Times[j] - s.Times[i];
        float w = t <= s.Times[0] ? 0 : span > 0 ? Math.Clamp((t - s.Times[i]) / span, 0, 1) : 0;
        if ((s.Mask & 1) != 0 && s.T.Length > j) origin = s.T[i].Lerp(s.T[j], w);
        if ((s.Mask & 2) != 0 && s.R.Length > j) rot = s.R[i].Slerp(s.R[j], w);
        if ((s.Mask & 8) != 0 && s.S.Length > j) scale = s.S[i].Lerp(s.S[j], w);
        return new Transform3D(new Basis(rot) * Basis.FromScale(scale), origin);
    }

    public Texture2D? Texture(string? file)
    {
        if (file == null) return null;
        if (_textures.TryGetValue(file, out var t)) return t;
        var path = System.IO.Path.Combine(Dir, file);
        Texture2D? tex = null;
        if (System.IO.File.Exists(path)) { var img = Image.LoadFromFile(path); tex = ImageTexture.CreateFromImage(img); }
        return _textures[file] = tex;
    }
}

/// <summary>
/// Um ator de UI montado em nós da Godot. O tempo do ator (JOINTSET) anima os nós; um medidor (beMeterCtrlNode) tem tempo
/// próprio; números trocam a UV dos dígitos. Os igSwitch mostram só o filho <c>slot</c> (instância do jogador ou do número).
/// </summary>
public sealed class HudActor
{
    sealed record Animated(Node3D Node, HudData.NodeData Data, int Meter);
    sealed record TexMapped(StandardMaterial3D Material, HudData.NodeData[] Chain, int Meter);

    public Node3D Root { get; } = new();
    public float Time { get; set; }
    readonly HudData _h;
    readonly List<Animated> _animated = [];
    readonly List<TexMapped> _texMapped = [];
    readonly Dictionary<int, float> _meterTime = [];
    readonly Dictionary<int, (ArrayMesh Mesh, HudData.MeshData Data)> _geometry = [];
    readonly List<StandardMaterial3D> _materials = [];
    readonly bool _billboard;

    public HudActor(HudData h, int root, int slot, bool billboard = false)
    {
        _h = h;
        _billboard = billboard;
        Build(root, Root, null, false, [], -1, slot);
    }

    void Build(int id, Node3D parent, string? texture, bool texSpace, List<HudData.NodeData> texChain, int meter, int slot)
    {
        if (!_h.Nodes.TryGetValue(id, out var n)) return;
        texture = n.Texture ?? texture;
        if (n.TextureMatrix) { texSpace = true; texChain = []; }
        if (n.Type == "beMeterCtrlNode") meter = n.Id;
        var here = parent;
        if (n.Type == "igTransform")
        {
            if (texSpace) texChain = [.. texChain, n];
            else
            {
                var node = new Node3D { Name = n.Name.Length > 0 ? n.Name : $"n{n.Id}", Transform = n.Matrix ?? Transform3D.Identity };
                parent.AddChild(node);
                here = node;
                if (n.Sequence != null) _animated.Add(new Animated(node, n, meter));
            }
        }
        foreach (var m in n.Meshes)
        {
            var mesh = new ArrayMesh();
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = m.Positions;
            arrays[(int)Mesh.ArrayType.TexUV] = m.Uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = m.Triangles;
            if (m.Triangles.Length == 0) continue;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            // igBlendFunctionAttr 4/5 (SRC_ALPHA, ONE_MINUS_SRC_ALPHA) nos atores do #179: transparência normal.
            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoTexture = _h.Texture(texture), NoDepthTest = _billboard,
                BillboardMode = _billboard ? BaseMaterial3D.BillboardModeEnum.Enabled : BaseMaterial3D.BillboardModeEnum.Disabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.Linear,
            };
            if (texture == null) mat.AlbedoColor = new Color(1, 1, 1, 0.3f);
            _materials.Add(mat);
            here.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat });
            _geometry[n.Id] = (mesh, m);
            if (texSpace && texChain.Count > 0) _texMapped.Add(new TexMapped(mat, texChain.ToArray(), meter));
        }
        var children = n.Children;
        if (n.Type == "igSwitch" && children.Length > 0) children = [children[Math.Clamp(slot, 0, children.Length - 1)]];
        foreach (var c in children) Build(c, here, texture, texSpace, texChain, meter, slot);
    }

    public void SetMeterTime(int meterNode, float t) => _meterTime[meterNode] = t;

    public void SetAlpha(float a) { foreach (var m in _materials) m.AlbedoColor = new Color(m.AlbedoColor, a); }

    float TimeFor(int meter) => meter >= 0 && _meterTime.TryGetValue(meter, out var t) ? t : Time;

    public void Update()
    {
        foreach (var a in _animated) a.Node.Transform = HudData.Evaluate(a.Data, TimeFor(a.Meter));
        foreach (var tm in _texMapped)
        {
            // Matriz de textura no espaço de UV do IGB (origem embaixo): u' = a·u + b. Com a UV invertida (1 − v) da malha,
            // vira escala (a_u, a_v) e deslocamento (b_u, 1 − a_v − b_v).
            var t = Transform3D.Identity;
            foreach (var n in tm.Chain) t *= HudData.Evaluate(n, TimeFor(tm.Meter));
            float au = t.Basis.X.X, av = t.Basis.Y.Y, bu = t.Origin.X, bv = t.Origin.Y;
            tm.Material.Uv1Scale = new Vector3(au, av, 1);
            // −1e-4: com escala 0 a UV crua (0, 0) cai em v = 1, que com repetição daria a volta para a primeira linha da PNG;
            // no IGB (linhas de baixo para cima) esse texel é o da última linha.
            tm.Material.Uv1Offset = new Vector3(bu, 1 - av - bv - 1e-4f, 0);
        }
    }

    /// <summary>Número no beNumberCtrlInfoRam: dígito k (0 = unidade) com U += dígito × offsetNext; zeros à esquerda somem.</summary>
    public void SetNumber(HudData.NumberRam ram, int value)
    {
        if (!_geometry.TryGetValue(ram.Geometry, out var g)) return;
        value = Math.Clamp(value, 0, ram.MaxValue);
        var uvs = g.Data.Uvs.ToArray();
        var pos = g.Data.Positions.ToArray();
        int len = Math.Max(1, value.ToString().Length);
        for (int k = 0; k < ram.DigitVertices.Length; k++)
        {
            int d = value / (int)Math.Pow(10, k) % 10;
            for (int v = 0; v < ram.DigitVertices[k].Length; v++)
            {
                int vi = ram.DigitVertices[k][v];
                if (vi < 0 || vi >= uvs.Length) continue;
                uvs[vi] = new Vector2(g.Data.Uvs[vi].X + d * ram.OffsetNext, g.Data.Uvs[vi].Y);
                // HIPÓTESE (_mode 1): sem zeros à esquerda; o quad do dígito vazio é desmontado num ponto.
                if (k >= len) pos[vi] = g.Data.Positions[ram.DigitVertices[k][0]];
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = pos;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = g.Data.Triangles;
        var mat = g.Mesh.SurfaceGetMaterial(0);
        g.Mesh.ClearSurfaces();
        g.Mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (mat != null) g.Mesh.SurfaceSetMaterial(0, mat);
    }
}

/// <summary>
/// Painel de status do jogador 1 (i_stpnl01..04) numa SubViewport ortográfica de 640 × 480 desenhada por cima da tela.
/// </summary>
public sealed class HudOverlay
{
    // FATO (Lua, GENERATE "V<−260,−160,0>" no laço dos jogadores): posição do painel do jogador 1; os outros a +150 em x.
    static readonly Vector3 Panel1 = new(-260, -160, 0);
    readonly HudData _h;
    readonly List<HudActor> _panels = [];
    HudActor? _meters, _numbers, _icon, _ring;
    readonly HudData.MeterData? _hp, _mp;
    readonly HudData.NumberRam? _hpNum, _mpNum, _lvNum;
    float _ringTime;

    public HudOverlay(HudData h, Node owner)
    {
        _h = h;
        var vp = new SubViewport { Size = new Vector2I(640, 480), TransparentBg = true, OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        // Projeção do igProjectionMatrixAttr: diag(2/640, 2/480), z de −10 a −10010 (FATO no #179).
        vp.AddChild(new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = 480, KeepAspect = Camera3D.KeepAspectEnum.Height, Near = 1, Far = 20000, Current = true });
        var holder = new Node3D { Position = Panel1 };
        vp.AddChild(holder);
        // Ordem de desenho: a mesma do Lua (01, 02, 03, 04).
        foreach (var name in new[] { "i_stpnl01", "i_stpnl02", "i_stpnl03", "i_stpnl04" })
        {
            if (!h.Actors.TryGetValue(name, out int root)) continue;
            var a = new HudActor(h, root, slot: 0);
            holder.AddChild(a.Root);
            _panels.Add(a);
            if (name == "i_stpnl01") _ring = a;
            if (name == "i_stpnl02") _icon = a;
            if (name == "i_stpnl03") _meters = a;
            if (name == "i_stpnl04") _numbers = a;
        }
        // i_stpnl02: LABEL "girumon" → WAIT03 → JOINTSET 32 (FATO): o rosto do Guilmon.
        if (_icon != null) _icon.Time = 32 / 60f;
        _hp = h.Meters.GetValueOrDefault("hpmt")?.FirstOrDefault();
        _mp = h.Meters.GetValueOrDefault("mpmt")?.FirstOrDefault();
        _hpNum = h.Numbers.GetValueOrDefault("hpnm")?.FirstOrDefault();
        _mpNum = h.Numbers.GetValueOrDefault("mpnm")?.FirstOrDefault();
        _lvNum = h.Numbers.GetValueOrDefault("lvnm")?.FirstOrDefault();
        var layer = new CanvasLayer { Layer = 1 };
        // DECISÃO DA FATIA: a tela 640 × 480 é escalada para a altura da janela e centrada.
        var rect = new TextureRect { Texture = vp.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(vp);
        layer.AddChild(rect);
        owner.AddChild(layer);
    }

    /// <summary>
    /// HIPÓTESE (medidor): as chaves dos gomos do MP e do hpmetr cobrem 1 s → 3 s; tempo = 1 + 2 × valor/máximo, com o
    /// valor diferente de zero em pelo menos 1 + _nonZeroTime e o valor abaixo do máximo em no máximo 1 + _nonMaxTime.
    /// A fórmula do SETPERCENT no DOL ainda não foi lida.
    /// </summary>
    public static float MeterTime(HudData.MeterData m, float value, float max)
    {
        if (max <= 0 || value <= 0) return 1;
        if (value >= max) return 3;
        return 1 + Math.Clamp(2 * value / max, m.NonZeroTime, m.NonMaxTime);
    }

    public void Update(float dt, int hp, int maxHp, int mp, int maxMp, int level)
    {
        // i_stpnl01: WAIT01 = JOINTSET 0..360 "LP" (FATO).
        _ringTime = (_ringTime + dt) % 6f;
        if (_ring != null) _ring.Time = _ringTime;
        if (_meters != null)
        {
            if (_hp != null) _meters.SetMeterTime(_hp.Node, MeterTime(_hp, hp, maxHp));
            if (_mp != null) _meters.SetMeterTime(_mp.Node, MeterTime(_mp, mp, maxMp));
        }
        if (_numbers != null)
        {
            if (_hpNum != null) _numbers.SetNumber(_hpNum, hp);
            if (_mpNum != null) _numbers.SetNumber(_mpNum, mp);
            if (_lvNum != null) _numbers.SetNumber(_lvNum, level);
        }
        foreach (var p in _panels) p.Update();
    }
}

/// <summary>
/// Números de dano i_dmgnum (no inimigo, laranja) e i_pcdmgnum (no jogador, rosa) no mundo: um ator por número, billboard.
/// FATO (igb-seq): SQ_DMGNMnn → OPENnn = JOINTSET "" a..b "OS" (trechos 10–30, 40–60, 70–90, 100–120, 130–150 da
/// animação do dmgnmtrans) + FADESET IN 12 + espera 20 + FADESET OUT 20 + espera 20, depois SQ_ERAS.
/// </summary>
public sealed class DamageNumbers
{
    static readonly (int A, int B)[] Open = [(10, 30), (40, 60), (70, 90), (100, 120), (130, 150)];
    readonly HudData _h;
    readonly Node3D _owner;
    readonly List<(HudActor Actor, int Open, float Age)> _live = [];
    readonly Random _rng = new();

    public DamageNumbers(HudData h, Node3D owner) { _h = h; _owner = owner; }

    public bool Show(bool onPlayer, int value, Vector3 at)
    {
        string actor = onPlayer ? "i_pcdmgnum" : "i_dmgnum", number = onPlayer ? "dmgnm1" : "dmgnm0";
        if (!_h.Actors.TryGetValue(actor, out int root) || _h.Numbers.GetValueOrDefault(number)?.FirstOrDefault() is not { } ram) return false;
        var a = new HudActor(_h, root, slot: 0, billboard: true);
        _owner.AddChild(a.Root);
        a.Root.GlobalPosition = at;
        a.SetNumber(ram, value);
        // DECISÃO DA FATIA: qual OPEN01..05 o jogo usa não foi lido; aqui é sorteado.
        _live.Add((a, _rng.Next(Open.Length), 0));
        Update(0);
        return true;
    }

    public void Update(float dt)
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var (a, open, age) = _live[i];
            age += dt;
            float frame = age * 60;
            if (frame >= 72) { a.Root.QueueFree(); _live.RemoveAt(i); continue; } // 12 + 20 + 20 + 20 quadros
            var (from, to) = Open[open];
            a.Time = Math.Min(from + frame, to) / 60f;
            a.SetAlpha(frame < 12 ? frame / 12 : frame < 32 ? 1 : frame < 52 ? 1 - (frame - 32) / 20 : 0);
            a.Update();
            _live[i] = (a, open, age);
        }
    }
}
