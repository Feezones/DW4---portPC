using System.Numerics;
using System.Text.Json;
using DW4.Formats.Images;
using DW4.Gameplay;

/// <summary>
/// Prévia de atores de interface (o *_hud.json do igb-hud) desenhada em software numa PNG, sem janela: mesmas regras do HudActor
/// do cliente (textura herdada, igSwitch no filho 0, matriz de textura, sequência por máscara, ordem de desenho do grafo) e as
/// sequências do ObjectSequencer. Serve para conferir posições de telas montadas por código nativo sem abrir o jogo nem a Godot.
/// </summary>
static class HudPreview
{
    sealed record Seq(int Mask, float[] Times, Vector3[] T, Quaternion[] R, Vector3[] S);
    sealed record MeshData(Vector3[] Positions, Vector2[] Uvs, int[] Triangles);
    sealed record Node(int Id, string Type, Matrix4x4? Matrix, Seq? Sequence, int[] Children, string? Texture, bool TextureMatrix, MeshData[] Meshes, bool? Additive);

    /// <summary>Uma camada: ator, tempo (s) e deslocamento na tela. Com <paramref name="CameraZ"/> &gt; 0, vista em perspectiva por uma
    /// câmera em (0, 0, CameraZ) olhando para −z com o fov horizontal dado para a largura 640 (camadas de cenário, ex. BG02).</summary>
    public sealed record Layer(string Actor, float Time, float Alpha, Vector2 Offset, float CameraZ = 0, float FovH = 0, float Far = 0);

    public static byte[] Render(string dir, int width, IEnumerable<Layer> layers)
    {
        var file = Directory.EnumerateFiles(dir, "*_hud.json").First();
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var actors = doc.RootElement.GetProperty("actors").EnumerateArray().ToDictionary(a => a.GetProperty("name").GetString()!, a => a.GetProperty("root").GetInt32());
        var nodes = doc.RootElement.GetProperty("nodes").EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => ReadNode(int.Parse(p.Name), p.Value));
        var textures = new Dictionary<string, (int W, int H, byte[] Px)?>();
        (int W, int H, byte[] Px)? Tex(string? name)
        {
            if (name == null) return null;
            if (!textures.TryGetValue(name, out var t))
                textures[name] = t = File.Exists(Path.Combine(dir, name)) ? Png.ReadRgba(File.ReadAllBytes(Path.Combine(dir, name))) : null;
            return t;
        }
        const int height = 480;
        var img = new float[width * height * 4];
        for (int i = 0; i < width * height; i++) { img[4 * i] = 0.1f; img[4 * i + 1] = 0.1f; img[4 * i + 2] = 0.14f; img[4 * i + 3] = 1; }

        foreach (var layer in layers)
        {
            if (!actors.TryGetValue(layer.Actor, out int root)) { Console.Error.WriteLine($"ator {layer.Actor} não está no json"); continue; }
            void Walk(int id, Matrix4x4 world, string? texture, bool texSpace, List<Node> chain, bool additive)
            {
                if (!nodes.TryGetValue(id, out var n)) return;
                texture = n.Texture ?? texture;
                additive = n.Additive ?? additive;
                if (n.TextureMatrix) { texSpace = true; chain = []; }
                if (n.Type == "igTransform")
                {
                    static bool Whole(float v) => Math.Abs(v - MathF.Round(v)) < 1e-3f;
                    bool positional = texSpace && n.Sequence is { } sq && sq.T.Any(v => Math.Abs(v.X) > 1.5f || Math.Abs(v.Y) > 1.5f) && sq.T.All(v => Whole(v.X) && Whole(v.Y));
                    if (texSpace && !positional) chain = [.. chain, n];
                    else world = Evaluate(n, layer.Time) * world;
                }
                foreach (var m in n.Meshes)
                {
                    var tm = Matrix4x4.Identity;
                    if (texSpace) foreach (var c in chain) tm = Evaluate(c, layer.Time) * tm;
                    float au = tm.M11, av = tm.M22, bu = tm.M41, bv = tm.M42;
                    bool mapped = texSpace && chain.Count > 0;
                    var tex = Tex(texture);
                    var world3 = m.Positions.Select(p => Vector3.Transform(p, world)).ToArray();
                    float focal = layer.CameraZ > 0 ? 320 / MathF.Tan(layer.FovH * MathF.PI / 360) : 0;
                    var pts = world3.Select(p => layer.CameraZ > 0
                        ? new Vector2(width / 2f + p.X * focal / (layer.CameraZ - p.Z) + layer.Offset.X, height / 2f - p.Y * focal / (layer.CameraZ - p.Z) - layer.Offset.Y)
                        : new Vector2(p.X + width / 2f + layer.Offset.X, height / 2f - p.Y - layer.Offset.Y)).ToArray();
                    // Perspectiva: triângulo com vértice fora de near/far fica de fora (a câmera do jogo corta no far).
                    bool Clipped(int i) => layer.CameraZ > 0 && (layer.CameraZ - world3[i].Z < 1 || layer.CameraZ - world3[i].Z > layer.Far);
                    var uvs = m.Uvs.Select(uv => mapped ? new Vector2(uv.X * au + bu, uv.Y * av + (1 - av - bv - 1e-4f)) : uv).ToArray();
                    if (Environment.GetEnvironmentVariable("PREVIA_DEBUG") == "1")
                        Console.WriteLine($"  nó {n.Id} {texture} aditivo={additive} mapeado={mapped} uv×({au:0.##},{av:0.##})+({bu:0.##},{bv:0.##}) x {pts.Min(p => p.X):0}..{pts.Max(p => p.X):0} y {pts.Min(p => p.Y):0}..{pts.Max(p => p.Y):0}");
                    for (int k = 0; k + 2 < m.Triangles.Length; k += 3)
                        if (!Clipped(m.Triangles[k]) && !Clipped(m.Triangles[k + 1]) && !Clipped(m.Triangles[k + 2])) Triangle(img, width, height, pts, uvs, m.Triangles[k], m.Triangles[k + 1], m.Triangles[k + 2], tex, layer.Alpha, additive);
                }
                var kids = n.Children;
                if (n.Type == "igSwitch" && kids.Length > 0) kids = [kids[0]];
                foreach (var c in kids) Walk(c, world, texture, texSpace, chain, additive);
            }
            Walk(root, Matrix4x4.Identity, null, false, [], false);
        }
        var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Math.Clamp(img[i] * 255 + 0.5f, 0, 255);
        using var ms = new MemoryStream();
        Png.WriteRgba(ms, width, height, bytes);
        return ms.ToArray();
    }

    static void Triangle(float[] img, int w, int h, Vector2[] p, Vector2[] uv, int a, int b, int c, (int W, int H, byte[] Px)? tex, float alpha, bool additive)
    {
        if (a >= p.Length || b >= p.Length || c >= p.Length) return;
        var (p0, p1, p2) = (p[a], p[b], p[c]);
        float area = (p1.X - p0.X) * (p2.Y - p0.Y) - (p2.X - p0.X) * (p1.Y - p0.Y);
        if (Math.Abs(area) < 1e-6f) return;
        int x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X)))), x1 = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)))), y1 = Math.Min(h - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((p1.X - px) * (p2.Y - py) - (p2.X - px) * (p1.Y - py)) / area;
                float w1 = ((p2.X - px) * (p0.Y - py) - (p0.X - px) * (p2.Y - py)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                float r = 1, g = 1, bl = 1, al = 0.3f;
                if (tex is { } t)
                {
                    var u = uv[a] * w0 + uv[b] * w1 + uv[c] * w2;
                    float fu = u.X - MathF.Floor(u.X), fv = u.Y - MathF.Floor(u.Y);
                    int tx = Math.Clamp((int)(fu * t.W), 0, t.W - 1), ty = Math.Clamp((int)(fv * t.H), 0, t.H - 1);
                    int o = (ty * t.W + tx) * 4;
                    (r, g, bl, al) = (t.Px[o] / 255f, t.Px[o + 1] / 255f, t.Px[o + 2] / 255f, t.Px[o + 3] / 255f);
                }
                al *= alpha;
                int d = (y * w + x) * 4;
                float keep = additive ? 1 : 1 - al;
                img[d] = img[d] * keep + r * al;
                img[d + 1] = img[d + 1] * keep + g * al;
                img[d + 2] = img[d + 2] * keep + bl * al;
            }
    }

    // Transformação local no tempo t (s): a sequência troca só os canais da máscara (bit 0 T, 1 R, 3 S), como no HudData.Evaluate.
    static Matrix4x4 Evaluate(Node n, float t)
    {
        var baseM = n.Matrix ?? Matrix4x4.Identity;
        if (n.Sequence is not { } s || s.Times.Length == 0) return baseM;
        // Como no cliente: escala nula fica sem rotação (a decomposição falharia).
        if (!Matrix4x4.Decompose(baseM, out var scale, out var rot, out var origin) || Math.Abs(baseM.GetDeterminant()) < 1e-12f)
            (scale, rot, origin) = (new Vector3(new Vector3(baseM.M11, baseM.M12, baseM.M13).Length(), new Vector3(baseM.M21, baseM.M22, baseM.M23).Length(), new Vector3(baseM.M31, baseM.M32, baseM.M33).Length()), Quaternion.Identity, baseM.Translation);
        else if (baseM.GetDeterminant() < 0) { scale = -scale; rot = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateScale(-1) * baseM)); }
        int i = 0;
        while (i + 1 < s.Times.Length && s.Times[i + 1] <= t) i++;
        int j = Math.Min(i + 1, s.Times.Length - 1);
        float span = s.Times[j] - s.Times[i];
        float wgt = t <= s.Times[0] ? 0 : span > 0 ? Math.Clamp((t - s.Times[i]) / span, 0, 1) : 0;
        if ((s.Mask & 1) != 0 && s.T.Length > j) origin = Vector3.Lerp(s.T[i], s.T[j], wgt);
        if ((s.Mask & 2) != 0 && s.R.Length > j) rot = Quaternion.Slerp(s.R[i], s.R[j], wgt);
        if ((s.Mask & 8) != 0 && s.S.Length > j) scale = Vector3.Lerp(s.S[i], s.S[j], wgt);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin);
    }

    static Node ReadNode(int id, JsonElement e)
    {
        Matrix4x4? matrix = null;
        if (e.TryGetProperty("matrix", out var m))
        {
            var f = Floats(m);
            matrix = new Matrix4x4(f[0], f[1], f[2], f[3], f[4], f[5], f[6], f[7], f[8], f[9], f[10], f[11], f[12], f[13], f[14], f[15]);
        }
        Seq? seq = null;
        if (e.TryGetProperty("sequence", out var s))
        {
            var t = s.TryGetProperty("translation", out var tt) ? Vec3s(Floats(tt)) : [];
            var r = s.TryGetProperty("rotation", out var rr) ? Quats(Floats(rr)) : [];
            var sc = s.TryGetProperty("scale", out var ss) ? Vec3s(Floats(ss)) : [];
            seq = new Seq(s.GetProperty("mask").GetInt32(), Floats(s.GetProperty("times")), t, r, sc);
        }
        string? texture = e.TryGetProperty("texture", out var tx) && tx.ValueKind == JsonValueKind.String ? tx.GetString() : null;
        bool texMatrix = false;
        bool? additive = null;
        if (e.TryGetProperty("attrs", out var attrs))
            foreach (var a in attrs.EnumerateArray())
            {
                var type = a.GetProperty("type").GetString();
                if (type == "igBlendFunctionAttr" && a.TryGetProperty("dst", out var dst)) additive = dst.GetInt32() == 1;
                if (type == "igTextureBindAttr" && texture == null && a.TryGetProperty("texture", out var bt) && bt.ValueKind == JsonValueKind.String) texture = bt.GetString();
                if (type == "igTextureMatrixStateAttr" && a.GetProperty("enabled").GetBoolean()) texMatrix = true;
            }
        var meshes = new List<MeshData>();
        if (e.TryGetProperty("meshes", out var ms))
            foreach (var mm in ms.EnumerateArray().Where(x => x.TryGetProperty("positions", out _)))
            {
                var pos = Vec3s(Floats(mm.GetProperty("positions")));
                var uv = mm.TryGetProperty("uvs", out var u) ? Floats(u) : new float[pos.Length * 2];
                var uvs = Enumerable.Range(0, pos.Length).Select(i => new Vector2(uv[2 * i], 1 - uv[2 * i + 1])).ToArray();
                meshes.Add(new MeshData(pos, uvs, mm.GetProperty("triangles").EnumerateArray().Select(x => x.GetInt32()).ToArray()));
            }
        return new Node(id, e.GetProperty("type").GetString()!, matrix, seq, e.GetProperty("children").EnumerateArray().Select(c => c.GetInt32()).ToArray(), texture, texMatrix, meshes.ToArray(), additive);
    }

    static float[] Floats(JsonElement a) => a.EnumerateArray().Select(x => x.GetSingle()).ToArray();
    static Vector3[] Vec3s(float[] f) => Enumerable.Range(0, f.Length / 3).Select(i => new Vector3(f[3 * i], f[3 * i + 1], f[3 * i + 2])).ToArray();
    static Quaternion[] Quats(float[] f) => Enumerable.Range(0, f.Length / 4).Select(i => Quaternion.Normalize(new Quaternion(-f[4 * i], -f[4 * i + 1], -f[4 * i + 2], f[4 * i + 3]))).ToArray();

    /// <summary>Tempo e alfa do ator depois de rodar a sequência por <paramref name="seconds"/> (o ObjectSequencer do cliente).</summary>
    public static (float Time, float Alpha) Run(string dir, string actor, string sequence, float seconds)
    {
        var seqDir = Path.Combine(dir, actor);
        var file = Directory.Exists(seqDir) ? Directory.EnumerateFiles(seqDir, "*_seq.json").FirstOrDefault() : null;
        if (file == null) return (seconds, 1);
        var json = File.ReadAllText(file);
        var s = new ObjectSequencer(SeqCommand.LoadAll(json), SeqCommand.LoadActions(json));
        if (!s.Start(sequence)) Console.Error.WriteLine($"{actor}: sem a sequência {sequence}");
        for (float t = 0; t < seconds; t += 1 / 60f) s.Tick(1 / 60f);
        return (s.Frame / 60f, s.Alpha);
    }
}
