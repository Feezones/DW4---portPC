using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Um volume de câmera: caixa de ativação e as transformações A (recuo) e B (pivô e inclinação), em linhas.</summary>
public sealed record IgbCameraBox(int Index, float[] Min, float[] Max, float[] A, float[] B)
{
    /// <summary>Recuo ao longo do Z local (translação de A).</summary>
    public float Distance => A[14];
}

/// <summary>
/// Câmera por volumes de uma fase (ndmw #180/#181 na dng1). Campos conferidos em 2026-10-24, ver docs/gameplay/fase3-levantamento.md:
///   beCameraCtrlInfo → beCameraBoxData: R@2 igSwitch, R@3 igGeometryList (cada igGeometry só com igAABox),
///   R@4 igTransformList (1, comum), R@5 igTransformList "A", R@6 igTransformList "B"; o volume i usa A[i] e B[i].
/// </summary>
public sealed class IgbCameraSet
{
    /// <summary>Transformação comum a todos os volumes (R@4), em linhas; identidade se ausente.</summary>
    public float[] Common { get; private set; } = Identity();
    public List<IgbCameraBox> Boxes { get; } = [];
    public List<string> Notes { get; } = [];

    static float[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    public static IgbCameraSet Read(IgbFile f, byte[] data)
    {
        var c = new IgbCameraSet();
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        IgbObject? Ref(IgbObject o, int slot) =>
            o.Fields.FirstOrDefault(v => v.Desc.ShortName == "ObjectRef" && v.Desc.Slot == slot) is { } v ? Slot(v.AsInt32(le)) as IgbObject : null;
        IEnumerable<IgbObject> Refs(IgbObject o) => o.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>();
        float F32(byte[] b, int at) => le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
        float[] Matrix(IgbObject t)
        {
            var m = t.Fields.FirstOrDefault(v => v.Desc.ShortName == "Matrix44f" && v.Raw.Length >= 64);
            if (m == null) return Identity();
            var r = new float[16];
            for (int k = 0; k < 16; k++) r[k] = F32(m.Raw, 4 * k);
            return r;
        }
        float[] Vec(IgbFieldValue v) => [F32(v.Raw, 0), F32(v.Raw, 4), F32(v.Raw, 8)];

        var box = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "beCameraBoxData"));
        if (box == null) { c.Notes.Add("sem beCameraBoxData"); return c; }
        var geoms = Ref(box, 3) is { } gl ? scene.ObjectList(gl) : [];
        var common = Ref(box, 4) is { } cl ? scene.ObjectList(cl) : [];
        var a = Ref(box, 5) is { } al ? scene.ObjectList(al) : [];
        var b = Ref(box, 6) is { } bl ? scene.ObjectList(bl) : [];
        if (common.Count > 0) c.Common = Matrix(common[0]);
        if (a.Count != geoms.Count || b.Count != geoms.Count)
            c.Notes.Add($"listas de tamanhos diferentes: {geoms.Count} volumes, {a.Count} A, {b.Count} B");
        for (int i = 0; i < geoms.Count; i++)
        {
            var aabb = Refs(geoms[i]).FirstOrDefault(r => f.Is(r, "igAABox"));
            var vecs = aabb?.Fields.Where(v => v.Desc.ShortName == "Vec3f" && v.Raw.Length >= 12).OrderBy(v => v.Desc.Slot).ToList();
            if (vecs == null || vecs.Count < 2) { c.Notes.Add($"volume {i}: sem igAABox"); continue; }
            c.Boxes.Add(new IgbCameraBox(i, Vec(vecs[0]), Vec(vecs[1]),
                i < a.Count ? Matrix(a[i]) : Identity(), i < b.Count ? Matrix(b[i]) : Identity()));
        }
        return c;
    }
}
