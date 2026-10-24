using System.Buffers.Binary;

namespace DW4.Formats.Igb;

public sealed record IgbBone(int Index, string Name, int Parent, int BlendMatrix, float Tx, float Ty, float Tz);

/// <summary>Uma malha de pele (igSkin → igBlendMatrixSelect → geometrias) com a tabela índice local → matriz.</summary>
public sealed class IgbSkinMesh
{
    public string Name { get; init; } = "";
    public List<int> MatrixPalette { get; init; } = [];
    public List<IgbMeshPart> Parts { get; } = [];
    public int ImageIndex { get; set; } = -1;
}

public sealed class IgbAnimTrack
{
    public string Bone { get; init; } = "";
    public int SourceIndex { get; init; } = -1;
    public int TrackInSource { get; init; } = -1;
}

public sealed class IgbAnimationInfo
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Duração em segundos (Long@9 em nanossegundos, conferido contra as fontes Enbaya).</summary>
    public double Duration { get; init; }
    public List<IgbAnimTrack> Tracks { get; } = [];
}

/// <summary>
/// Personagem com esqueleto: lê igSkeleton, igSkin/igBlendMatrixSelect e igAnimation de um IGB do ndmw.afs.
/// Campos conferidos no Guilmon (ndmw #857), ver docs/formatos/personagens-e-fases.md:
///   igSkeleton: String@2 nome, MemoryRef@3 translações locais (Vec3f por osso), ObjectRef@4 lista de ossos,
///               MemoryRef@5 inversas de bind (Matrix44f por matriz de pele), Int@6 nº de matrizes;
///   igSkeletonBoneInfo: String@2 nome, Int@3 pai, Int@4 matriz de pele (-1 = sem pele);
///   igBlendMatrixSelect: igIntList = índice local do vértice → matriz de pele;
///   igAnimation: String@2 nome, ObjectRef → igAnimationTrackList, Long@9 duração em ns;
///   igAnimationTrack: String@2 osso, ObjectRef@3 → igEnbayaTransformSource (Int@2 trilha, ObjectRef@3 fonte).
/// </summary>
public sealed class IgbCharacter
{
    public string SkeletonName { get; private set; } = "";
    public List<IgbBone> Bones { get; } = [];
    /// <summary>Inversas de bind por matriz de pele (16 floats, em linhas, translação na última linha).</summary>
    public List<float[]> InverseBind { get; } = [];
    public List<IgbSkinMesh> Skins { get; } = [];
    public List<IgbAnimationInfo> Animations { get; } = [];
    public Dictionary<int, byte[]> EnbayaBlocks { get; } = [];
    public IgbScene Scene { get; }
    public List<string> Notes { get; } = [];

    IgbCharacter(IgbScene scene) => Scene = scene;

    /// <summary>
    /// Lê o personagem do arquivo. Com <paramref name="actor"/> (ex.: "e_nume" numa fase), só considera o que é alcançável
    /// a partir do igAnimationDatabase ligado ao beAction2Info com esse nome; sem ele, o primeiro esqueleto do arquivo.
    /// </summary>
    public static IgbCharacter Read(IgbFile f, byte[] data, string? actor = null)
    {
        var scene = IgbScene.Read(f, data);
        var c = new IgbCharacter(scene);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        byte[] Mem(IgbFieldValue? v) => v != null && Slot(v.AsInt32(le)) is IgbMemoryBlock mb ? data.AsSpan((int)mb.FileOffset, mb.Size).ToArray() : [];
        float F32(byte[] b, int at) => le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
        int I32(byte[] b, int at) => le ? BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(at));
        IgbFieldValue? Field(IgbObject o, string type, int slot) => o.Fields.FirstOrDefault(v => v.Desc.ShortName == type && v.Desc.Slot == slot);
        IgbObject? Ref(IgbObject o, int slot) => Field(o, "ObjectRef", slot) is { } v ? Slot(v.AsInt32(le)) as IgbObject : null;
        IEnumerable<IgbObject> Refs(IgbObject o) => o.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>();

        HashSet<int>? only = null;
        if (actor != null)
        {
            var info = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "beAction2Info") && Field(o, "String", 2)?.Text == actor);
            var db = info == null ? null : Refs(info).FirstOrDefault(r => f.Is(r, "igAnimationDatabase"));
            if (db == null) { c.Notes.Add($"ator \"{actor}\" sem beAction2Info → igAnimationDatabase"); return c; }
            only = [];
            var queue = new Queue<IgbObject>([db]);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                if (!only.Add(o.Index)) continue;
                foreach (var r in Refs(o)) queue.Enqueue(r);
                if (f.Is(o, "igObjectList")) foreach (var r in scene.ObjectList(o)) queue.Enqueue(r);
            }
            c.Notes.Add($"ator \"{actor}\": igAnimationDatabase #{db.Index}, {only.Count} objetos alcançáveis");
        }
        bool Mine(IgbObject o) => only == null || only.Contains(o.Index);

        // Esqueleto
        var skel = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "igSkeleton") && Mine(o));
        if (skel == null) { c.Notes.Add("sem igSkeleton"); return c; }
        c.SkeletonName = Field(skel, "String", 2)?.Text ?? "";
        var trans = Mem(Field(skel, "MemoryRef", 3));
        var inv = Mem(Field(skel, "MemoryRef", 5));
        var boneList = Ref(skel, 4);
        if (boneList == null) { c.Notes.Add("igSkeleton sem lista de ossos"); return c; }
        var infos = scene.ObjectList(boneList);
        for (int i = 0; i < infos.Count; i++)
        {
            var b = infos[i];
            float tx = 0, ty = 0, tz = 0;
            if (trans.Length >= 12 * (i + 1)) { tx = F32(trans, 12 * i); ty = F32(trans, 12 * i + 4); tz = F32(trans, 12 * i + 8); }
            c.Bones.Add(new IgbBone(i, Field(b, "String", 2)?.Text ?? "", Field(b, "Int", 3)?.AsInt32(le) ?? -1,
                Field(b, "Int", 4)?.AsInt32(le) ?? -1, tx, ty, tz));
        }
        if (trans.Length != 12 * infos.Count) c.Notes.Add($"translações: {trans.Length} B para {infos.Count} ossos");
        for (int m = 0; m + 64 <= inv.Length; m += 64)
        {
            var mat = new float[16];
            for (int k = 0; k < 16; k++) mat[k] = F32(inv, m + 4 * k);
            c.InverseBind.Add(mat);
        }
        int jointCount = Field(skel, "Int", 6)?.AsInt32(le) ?? 0;
        if (jointCount != c.InverseBind.Count) c.Notes.Add($"igSkeleton diz {jointCount} matrizes, o bloco tem {c.InverseBind.Count}");

        // Peles
        foreach (var skin in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igSkin") && Mine(o)))
        {
            var bms = Refs(skin).FirstOrDefault(r => f.Is(r, "igBlendMatrixSelect"));
            if (bms == null) { c.Notes.Add($"igSkin #{skin.Index} sem igBlendMatrixSelect"); continue; }
            var palette = new List<int>();
            var intList = Refs(bms).FirstOrDefault(r => f.Is(r, "igIntList"));
            if (intList != null)
            {
                int count = intList.Fields.FirstOrDefault(v => v.Desc.ShortName == "Int")?.AsInt32(le) ?? 0;
                var mem = Mem(intList.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef"));
                for (int i = 0; i < count && 4 * i + 4 <= mem.Length; i++) palette.Add(I32(mem, 4 * i));
            }
            var mesh = new IgbSkinMesh { Name = Field(skin, "String", 2)?.Text ?? $"skin{skin.Index}", MatrixPalette = palette };
            // geometrias e textura abaixo do igBlendMatrixSelect
            var stack = new Stack<(IgbObject node, int image)>();
            var seen = new HashSet<int>();
            stack.Push((bms, -1));
            while (stack.Count > 0)
            {
                var (node, image) = stack.Pop();
                if (!seen.Add(node.Index)) continue;
                if (f.Is(node, "igTransform")) c.Notes.Add($"{mesh.Name}: igTransform #{node.Index} abaixo da pele foi ignorado");
                if (f.Is(node, "igAttrSet"))
                {
                    var attrs = scene.AttrList(node);
                    foreach (var a in attrs.Where(a => f.Is(a, "igTextureBindAttr")))
                    {
                        int img = scene.FindImage(a);
                        if (img >= 0) image = img;
                    }
                    foreach (var g in attrs.Where(a => f.Is(a, "igGeometryAttr")))
                    {
                        var part = scene.ReadGeometry(g);
                        part.ImageIndex = image;
                        mesh.Parts.Add(part);
                        if (image >= 0) mesh.ImageIndex = image;
                    }
                }
                foreach (var r in Refs(node))
                {
                    if (f.Is(r, "igNodeList")) foreach (var ch in scene.ObjectList(r).Where(x => f.Is(x, "igNode"))) stack.Push((ch, image));
                    else if (f.Is(r, "igNode") && r.Index != node.Index) stack.Push((r, image));
                }
            }
            c.Skins.Add(mesh);
        }

        // Animações
        foreach (var a in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "igAnimation") && Mine(o)))
        {
            var durField = a.Fields.FirstOrDefault(v => v.Desc.ShortName == "Long" && v.Desc.Slot == 9);
            long ns = durField != null && durField.Raw.Length >= 8
                ? (le ? BinaryPrimitives.ReadInt64LittleEndian(durField.Raw) : BinaryPrimitives.ReadInt64BigEndian(durField.Raw)) : 0;
            var info = new IgbAnimationInfo { Index = a.Index, Name = Field(a, "String", 2)?.Text ?? "", Duration = ns / 1e9 };
            var trackList = Refs(a).FirstOrDefault(r => f.Is(r, "igAnimationTrackList"));
            if (trackList != null)
                foreach (var t in scene.ObjectList(trackList).Where(x => f.Is(x, "igAnimationTrack")))
                {
                    var ts = Refs(t).FirstOrDefault(r => f.Is(r, "igEnbayaTransformSource"));
                    var src = ts == null ? null : Refs(ts).FirstOrDefault(r => f.Is(r, "igEnbayaAnimationSource"));
                    info.Tracks.Add(new IgbAnimTrack
                    {
                        Bone = Field(t, "String", 2)?.Text ?? "",
                        SourceIndex = src?.Index ?? -1,
                        TrackInSource = ts?.Fields.FirstOrDefault(v => v.Desc.ShortName == "Int")?.AsInt32(le) ?? -1,
                    });
                    if (src != null && !c.EnbayaBlocks.ContainsKey(src.Index))
                        c.EnbayaBlocks[src.Index] = Mem(src.Fields.FirstOrDefault(v => v.Desc.ShortName == "MemoryRef"));
                }
            c.Animations.Add(info);
        }
        return c;
    }
}
