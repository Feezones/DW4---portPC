using System.Text.RegularExpressions;

namespace DW4.Formats.Igb;

/// <summary>Um arquivo do índice: nome, AFS (0 = ndmw.afs, 1 = area.afs), entrada e os nomes da lista (fases e "separate").</summary>
public sealed record IgbAfsFile(string Name, int Afs, int Index, IReadOnlyList<string> Groups);

/// <summary>Uma referência "[afs.entrada]" (com "/s" = banco de som) de um beFileListInfo.</summary>
public sealed record IgbFileRef(int Afs, int Index, bool Sound);

/// <summary>
/// Índice de arquivos do jogo (ndmw.igb) e os blocos de uma fase. Levantamento de 2026-10-25:
///   igAdxAfsFile [@2 nome, @3 igNamedObjectList (igNamedObject: nome da fase e "separate"), @4 AFS (0 ndmw, 1 area), @5 entrada];
///   2080 arquivos em igAdxAfsFileList. FATO: "&lt;fase&gt;.igb" aponta para um esboço de 3270 B (dng101b1 → ndmw #80) com um
///   beFileListInfo [@2 nome, @5 pasta, @6 igNamedObjectList de "[afs.entrada]" e "[afs.entrada]/s"]: dng101b1 = "[0.178]/s"
///   (banco de som) e "[0.179]" (IGB principal). Os demais arquivos da fase ficam no índice com a fase na lista ("dng101b1
///   separate"): aaacamera, aaacameral, acmr_pack, generatorh/n/v, eng/fra/ger/ita/jpn/spn/usa_pack (area), lh/ls_pack.
/// </summary>
public sealed class IgbStageIndex
{
    public List<IgbAfsFile> Files { get; } = [];

    public static IgbStageIndex Read(IgbFile f, byte[] data)
    {
        bool le = f.Header.LittleEndian;
        var scene = IgbScene.Read(f, data);
        var idx = new IgbStageIndex();
        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "igAdxAfsFile"))
        {
            string name = Text(o, 2) ?? "";
            var groups = Obj(f, o, 3, le) is { } gl ? scene.ObjectList(gl).Select(n => Text(n, 2) ?? "").ToList() : [];
            idx.Files.Add(new IgbAfsFile(name, Short(o, 4, le), Short(o, 5, le), groups));
        }
        return idx;
    }

    /// <summary>Arquivos ligados à fase pela lista de grupos ("dng101b1 separate").</summary>
    public IEnumerable<IgbAfsFile> Separate(string stage) => Files.Where(x => x.Groups.Contains(stage, StringComparer.OrdinalIgnoreCase));

    /// <summary>O esboço "&lt;fase&gt;.igb".</summary>
    public IgbAfsFile? Stub(string stage) =>
        Files.FirstOrDefault(x => string.Equals(x.Name, stage + ".igb", StringComparison.OrdinalIgnoreCase));

    /// <summary>As referências do beFileListInfo de um esboço.</summary>
    public static List<IgbFileRef> StubRefs(IgbFile f, byte[] data)
    {
        bool le = f.Header.LittleEndian;
        var scene = IgbScene.Read(f, data);
        var refs = new List<IgbFileRef>();
        foreach (var fl in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beFileListInfo"))
        {
            if (Obj(f, fl, 6, le) is not { } list) continue;
            foreach (var n in scene.ObjectList(list))
                if (Text(n, 2) is { } s && Regex.Match(s, @"^\[(\d+)\.(\d+)\](/s)?$") is { Success: true } m)
                    refs.Add(new IgbFileRef(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success));
        }
        return refs;
    }

    static string? Text(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName == "String")?.Text;
    static int Short(IgbObject o, int slot, bool le)
    {
        var b = o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Raw ?? [];
        return b.Length >= 2 ? (le ? b[0] | b[1] << 8 : b[0] << 8 | b[1]) : -1;
    }
    static IgbObject? Obj(IgbFile f, IgbObject o, int slot, bool le)
    {
        var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef");
        int i = v?.AsInt32(le) ?? -1;
        return i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
    }
}
