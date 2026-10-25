using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Canais de um igTransformSequence1_5 (tempos em segundos; listas planas, 3 ou 4 floats por chave).</summary>
public sealed record IgbDemoSequence(int Mask, byte[] Interpolation, double Duration, double[] Times, float[] Translation,
    float[] Rotation, float[] Scale, float[] Raw12, float[] Raw13);

/// <summary>Um nó do caminho da câmera (da raiz até o igCamera), com a matriz estática em linhas e a sequência, se houver.</summary>
public sealed record IgbDemoNode(int Index, string Name, float[] Matrix, IgbDemoSequence? Sequence);

/// <summary>Um quadro amostrado: matriz do mundo da câmera em linhas (v' = v · M), posição, frente (−Z local) e cima (+Y local).</summary>
public sealed record IgbDemoFrame(double Time, float[] World, float[] Position, float[] Forward, float[] Up);

/// <summary>
/// Câmera de demo de uma fase (acmr_pack, ndmw #182 na dng101b1). Levantamento de 2026-10-25:
///   beCameraCtrlInfo [@2 nome ("entry"), @7 beCameraDemoData]; beCameraDemoData [@2 igGraphPath, @3 duração em ns, @4 duração
///   em s]; igGraphPath [@2 igNodeList] = nós da raiz até a folha (igTransform … igCamera). igCamera [@8 ângulo de visão em
///   graus, @9 perto, @10 longe] (FATO: 54,4322 = 2·atan(18/35), o ângulo HORIZONTAL padrão do Maya para 35 mm).
///   Máscara do igTransformSequence1_5: bit 0 translação, bit 1 rotação em quatérnio, bit 3 escala (IgbHud.cs);
///   bit 2 = rotação em ângulos de Euler em graus (x, y, z, ignorado) — FATO no #182: o igTransform "cameraanim_group" tem a
///   matriz estática de −30° em X e a lista de rotação dele começa em (−30, 0, 0). Ordem dos eixos: HIPÓTESE X, depois Y, depois Z
///   (no #182 cada nó gira num eixo só: "cameraanim_root" em Y, "cameraanim_group" em X, então a ordem não muda o resultado).
///   Interpolação entre chaves: linear. Nas sequências de 181 chaves há uma por quadro de 60 Hz, então não importa; o recuo do
///   "cameraanim01" tem 3 chaves (0, 1,5 e 3 s) com interpolação 2 e pontos de controle em @12/@13 (2 por chave), HIPÓTESE Bézier
///   não reproduzida: a diferença para o linear é de poucas unidades no recuo de 120 → 160 entre 1,5 e 3 s.
/// </summary>
public sealed class IgbCameraDemo
{
    public required string Name { get; init; }
    public double Duration { get; init; }
    public float Fov { get; init; }
    public float Near { get; init; }
    public float Far { get; init; }
    public List<IgbDemoNode> Path { get; } = [];
    public List<string> Notes { get; } = [];

    public static List<IgbCameraDemo> Read(IgbFile f, byte[] data)
    {
        var r = new Reader(f, data);
        var list = new List<IgbCameraDemo>();
        foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beCameraCtrlInfo"))
        {
            if (r.Obj(info, 7) is not { } demo || demo.TypeName != "beCameraDemoData") continue;
            var nodes = r.Obj(demo, 2) is { } gp && r.Obj(gp, 2) is { } nl ? r.ObjectList(nl) : [];
            var cam = nodes.LastOrDefault(n => f.Is(n, "igCamera"));
            var d = new IgbCameraDemo
            {
                Name = r.Text(info, 2) ?? $"#{info.Index}",
                Duration = r.Long(demo, 3) / 1e9,
                Fov = cam != null ? r.Float(cam, 8) : 0,
                Near = cam != null ? r.Float(cam, 9) : 0,
                Far = cam != null ? r.Float(cam, 10) : 0,
            };
            if (cam == null) d.Notes.Add("caminho sem igCamera");
            foreach (var n in nodes.Where(n => f.Is(n, "igTransform")))
            {
                IgbDemoSequence? seq = null;
                if (r.Obj(n, 11) is { } s && f.Is(s, "igTransformSequence1_5"))
                    seq = new IgbDemoSequence(r.Byte(s, 15), r.Raw(s, 16), r.Long(s, 18) / 1e9,
                        r.LongList(r.Obj(s, 11)).Select(t => t / 1e9).ToArray(), r.FloatList(r.Obj(s, 2)), r.FloatList(r.Obj(s, 3)),
                        r.FloatList(r.Obj(s, 4)), r.FloatList(r.Obj(s, 12)), r.FloatList(r.Obj(s, 13)));
                d.Path.Add(new IgbDemoNode(n.Index, r.Text(n, 2) ?? "", r.Matrix(n), seq));
                if (seq != null && (seq.Mask & ~0b1111) != 0) d.Notes.Add($"{r.Text(n, 2)}: máscara {seq.Mask} com bits desconhecidos");
            }
            list.Add(d);
        }
        return list;
    }

    /// <summary>Amostra a câmera de 0 até a duração, a <paramref name="rate"/> quadros por segundo (o último quadro cai na duração).</summary>
    public List<IgbDemoFrame> Sample(double rate = 60)
    {
        var frames = new List<IgbDemoFrame>();
        int count = (int)Math.Floor(Duration * rate + 1e-6);
        for (int k = 0; k <= count; k++)
        {
            double t = Math.Min(k / rate, Duration);
            // Alchemy em linhas: mundo = local(folha) · … · local(raiz).
            var world = Identity();
            for (int i = Path.Count - 1; i >= 0; i--) world = Mul(world, Local(Path[i], t));
            float[] pos = [world[12], world[13], world[14]];
            float[] fwd = Normalize([-world[8], -world[9], -world[10]]);
            float[] up = Normalize([world[4], world[5], world[6]]);
            frames.Add(new IgbDemoFrame(t, world, pos, fwd, up));
        }
        return frames;
    }

    static float[] Local(IgbDemoNode n, double t)
    {
        var m = n.Matrix;
        if (n.Sequence is not { } s) return m;
        // Partes não animadas saem da matriz estática: escala pelo comprimento das linhas, rotação pelas linhas normalizadas.
        float[] scale = [Len(m, 0), Len(m, 4), Len(m, 8)];
        float[] rot = [m[0] / scale[0], m[1] / scale[0], m[2] / scale[0], m[4] / scale[1], m[5] / scale[1], m[6] / scale[1],
            m[8] / scale[2], m[9] / scale[2], m[10] / scale[2]];
        float[] tr = [m[12], m[13], m[14]];
        if ((s.Mask & 1) != 0 && Key(s, s.Translation, 3, t) is { } kt) tr = kt;
        if ((s.Mask & 8) != 0 && Key(s, s.Scale, 3, t) is { } ks) scale = ks;
        if ((s.Mask & 2) != 0 && Key(s, s.Rotation, 4, t) is { } q) rot = FromQuaternion(q);
        else if ((s.Mask & 4) != 0 && Key(s, s.Rotation, 4, t) is { } e) rot = FromEuler(e[0], e[1], e[2]);
        return
        [
            rot[0] * scale[0], rot[1] * scale[0], rot[2] * scale[0], 0,
            rot[3] * scale[1], rot[4] * scale[1], rot[5] * scale[1], 0,
            rot[6] * scale[2], rot[7] * scale[2], rot[8] * scale[2], 0,
            tr[0], tr[1], tr[2], 1,
        ];
    }

    // Interpolação linear entre as chaves vizinhas (antes da primeira e depois da última, segura a ponta).
    static float[]? Key(IgbDemoSequence s, float[] values, int per, double t)
    {
        int n = Math.Min(s.Times.Length, values.Length / per);
        if (n == 0) return null;
        int i = 0;
        while (i + 1 < n && s.Times[i + 1] <= t) i++;
        var a = values.AsSpan(per * i, per).ToArray();
        if (i + 1 >= n || t <= s.Times[i]) return a;
        float u = (float)((t - s.Times[i]) / (s.Times[i + 1] - s.Times[i]));
        var b = values.AsSpan(per * (i + 1), per);
        for (int k = 0; k < per; k++) a[k] += (b[k] - a[k]) * u;
        return a;
    }

    // Quatérnio (x, y, z, w) em matriz de linhas.
    static float[] FromQuaternion(float[] q)
    {
        float len = MathF.Sqrt(q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3]);
        if (len == 0) return [1, 0, 0, 0, 1, 0, 0, 0, 1];
        float x = q[0] / len, y = q[1] / len, z = q[2] / len, w = q[3] / len;
        return
        [
            1 - 2 * (y * y + z * z), 2 * (x * y + z * w), 2 * (x * z - y * w),
            2 * (x * y - z * w), 1 - 2 * (x * x + z * z), 2 * (y * z + x * w),
            2 * (x * z + y * w), 2 * (y * z - x * w), 1 - 2 * (x * x + y * y),
        ];
    }

    // Euler em graus, em linhas: primeiro X, depois Y, depois Z (v' = v · Rx · Ry · Rz).
    static float[] FromEuler(float xd, float yd, float zd)
    {
        float[] Rx(float a) { float c = MathF.Cos(a), s = MathF.Sin(a); return [1, 0, 0, 0, c, s, 0, -s, c]; }
        float[] Ry(float a) { float c = MathF.Cos(a), s = MathF.Sin(a); return [c, 0, -s, 0, 1, 0, s, 0, c]; }
        float[] Rz(float a) { float c = MathF.Cos(a), s = MathF.Sin(a); return [c, s, 0, -s, c, 0, 0, 0, 1]; }
        const float k = MathF.PI / 180;
        return Mul3(Mul3(Rx(xd * k), Ry(yd * k)), Rz(zd * k));
    }

    static float[] Mul3(float[] a, float[] b)
    {
        var r = new float[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                r[3 * i + j] = a[3 * i] * b[j] + a[3 * i + 1] * b[3 + j] + a[3 * i + 2] * b[6 + j];
        return r;
    }

    static float[] Mul(float[] a, float[] b)
    {
        var r = new float[16];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                r[4 * i + j] = a[4 * i] * b[j] + a[4 * i + 1] * b[4 + j] + a[4 * i + 2] * b[8 + j] + a[4 * i + 3] * b[12 + j];
        return r;
    }

    static float Len(float[] m, int at) => MathF.Sqrt(m[at] * m[at] + m[at + 1] * m[at + 1] + m[at + 2] * m[at + 2]) is var l && l > 0 ? l : 1;
    static float[] Normalize(float[] v) { float l = Len(v, 0); return [v[0] / l, v[1] / l, v[2] / l]; }
    static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    sealed class Reader(IgbFile f, byte[] data)
    {
        readonly bool _le = f.Header.LittleEndian;
        readonly IgbScene _scene = IgbScene.Read(f, data);

        public byte[] Raw(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Raw ?? [];
        public string? Text(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName == "String")?.Text;
        public int Byte(IgbObject o, int slot) => Raw(o, slot) is [var b] ? b : 0;
        public float Float(IgbObject o, int slot) { var b = Raw(o, slot); return b.Length == 4 ? F32(b, 0) : 0; }
        public long Long(IgbObject o, int slot) { var b = Raw(o, slot); return b.Length == 8 ? I64(b, 0) : 0; }
        public float[] Matrix(IgbObject o)
        {
            var b = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "Matrix44f" && v.Raw.Length >= 64)?.Raw;
            return b == null ? Identity() : Enumerable.Range(0, 16).Select(k => F32(b, 4 * k)).ToArray();
        }
        public IgbObject? Obj(IgbObject o, int slot)
        {
            var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef");
            int i = v?.AsInt32(_le) ?? -1;
            return i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
        }
        public List<IgbObject> ObjectList(IgbObject list) => _scene.ObjectList(list);

        // igVec3fList / igQuaternionfList / igLongList: Int@2 = quantidade, MemoryRef@4 = itens contíguos.
        byte[] Memory(IgbObject list, out int count)
        {
            var c = Raw(list, 2);
            count = c.Length == 4 ? (_le ? BinaryPrimitives.ReadInt32LittleEndian(c) : BinaryPrimitives.ReadInt32BigEndian(c)) : 0;
            var v = list.Fields.FirstOrDefault(x => x.Desc.Slot == 4 && x.Desc.ShortName == "MemoryRef");
            int i = v?.AsInt32(_le) ?? -1;
            return i >= 0 && i < f.Slots.Count && f.Slots[i] is IgbMemoryBlock mb ? data.AsSpan((int)mb.FileOffset, mb.Size).ToArray() : [];
        }
        public float[] FloatList(IgbObject? list)
        {
            if (list == null) return [];
            var m = Memory(list, out int n);
            int per = n > 0 ? m.Length / 4 / n : 0;
            return Enumerable.Range(0, n * per).Select(i => F32(m, 4 * i)).ToArray();
        }
        public long[] LongList(IgbObject? list)
        {
            if (list == null) return [];
            var m = Memory(list, out int n);
            return Enumerable.Range(0, Math.Min(n, m.Length / 8)).Select(i => I64(m, 8 * i)).ToArray();
        }
        float F32(byte[] b, int at) => _le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
        long I64(byte[] b, int at) => _le ? BinaryPrimitives.ReadInt64LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadInt64BigEndian(b.AsSpan(at));
    }
}
