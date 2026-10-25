using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Um gerador (beGeneraterData) com as posições já absolutas, uma por vaga, e a contagem por nº de jogadores 1..4.</summary>
public sealed record IgbGenerator(string Name, IReadOnlyList<(float X, float Y, float Z)> Positions, IReadOnlyList<float> RotYDeg,
    IReadOnlyList<int> Count, IReadOnlyList<int> MaxCount, int EnemyRow, string StartMode, float StartData, string FillMode,
    float FillData, string CallLua, bool Global);

/// <summary>Um volume de gatilho de área (beMatCtrlData): nome do script sem ".lua" e a caixa.</summary>
public sealed record IgbTriggerVolume(string Name, (float X, float Y, float Z) Min, (float X, float Y, float Z) Max);

/// <summary>
/// Geradores de onda (generatorN/H/V) e gatilhos de área (IGB principal da fase). Levantamento de 2026-10-25
/// (docs/gameplay/fase4-objetos-dng1.md); nomes dos campos pela ordem dos metadados do main.dol:
///   beGeneraterData = [@2 nome, @3 _genePoint, @4 _genePosIsGlobal, @5 _genePosList (igVec3fList), @6 _geneRotYRadianList
///   (igFloatList), @7 _geneEnemyId (linha da enemy), @8 _geneEnemySeq, @9 _startModeStr, @10 _startData, @11 _fillModeStr,
///   @12 _fillData, @13 _callLua, @14 _playerDataList]; beGeneraterPlayerData = [@2 _geneCount, @3 _geneMaxCount, …], uma por
///   nº de jogadores. Com _genePosIsGlobal = 0 as posições são deslocamentos do ponto (FATO: GENE-03 da dng1 em (−180, 40, 60)
///   com 4 deslocamentos); com 1 já são globais (GBOX-01..05, ponto 0). A lista de rotações pode ter menos itens que a de
///   posições (GENE-01 e os GBOX têm um só): as vagas sem rotação própria usam a última.
///   beMatCtrlInfo = [@8 beMatCtrlDataList, …]; beMatCtrlData = [@2 nome, @3 igAABox(@2 mínimo, @3 máximo)].
/// </summary>
public static class IgbGenerators
{
    public static List<IgbGenerator> ReadGenerators(IgbFile f, byte[] data)
    {
        var r = new Reader(f, data);
        var list = new List<IgbGenerator>();
        foreach (var g in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beGeneraterData"))
        {
            var point = r.Vec3(g, 3);
            bool global = r.Byte(g, 4) != 0;
            var offsets = r.Vec3List(r.Obj(g, 5));
            var rots = r.FloatList(r.Obj(g, 6));
            var positions = offsets.Select(o => global ? o : (o.X + point.X, o.Y + point.Y, o.Z + point.Z)).ToList();
            var rotDeg = Enumerable.Range(0, positions.Count)
                .Select(i => rots.Count == 0 ? 0f : rots[Math.Min(i, rots.Count - 1)] * 180f / MathF.PI).ToList();
            var counts = new List<int>();
            var maxCounts = new List<int>();
            if (r.Obj(g, 14) is { } pl)
                foreach (var p in r.ObjectList(pl).Where(o => o.TypeName == "beGeneraterPlayerData"))
                {
                    counts.Add(r.Int(p, 2));
                    maxCounts.Add(r.Int(p, 3));
                }
            list.Add(new IgbGenerator(r.Text(g, 2) ?? $"#{g.Index}", positions, rotDeg, counts, maxCounts, r.Int(g, 7),
                r.Text(g, 9) ?? "", r.Float(g, 10), r.Text(g, 11) ?? "", r.Float(g, 12), r.Text(g, 13) ?? "", global));
        }
        return list;
    }

    public static List<IgbTriggerVolume> ReadTriggers(IgbFile f, byte[] data)
    {
        var r = new Reader(f, data);
        var list = new List<IgbTriggerVolume>();
        foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beMatCtrlInfo"))
        {
            if (r.Obj(info, 8) is not { } dl) continue;
            foreach (var d in r.ObjectList(dl).Where(o => o.TypeName == "beMatCtrlData"))
            {
                if (r.Obj(d, 3) is not { } box || box.TypeName != "igAABox") continue;
                list.Add(new IgbTriggerVolume(r.Text(d, 2) ?? $"#{d.Index}", r.Vec3(box, 2), r.Vec3(box, 3)));
            }
        }
        return list;
    }

    sealed class Reader(IgbFile f, byte[] data)
    {
        readonly bool _le = f.Header.LittleEndian;
        readonly IgbScene _scene = IgbScene.Read(f, data);

        byte[] Raw(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot)?.Raw ?? [];
        public string? Text(IgbObject o, int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName == "String")?.Text;
        public int Int(IgbObject o, int slot) { var b = Raw(o, slot); return b.Length == 4 ? (_le ? BinaryPrimitives.ReadInt32LittleEndian(b) : BinaryPrimitives.ReadInt32BigEndian(b)) : 0; }
        public int Byte(IgbObject o, int slot) => Raw(o, slot) is [var b] ? b : 0;
        public float Float(IgbObject o, int slot) { var b = Raw(o, slot); return b.Length == 4 ? F32(b, 0) : 0; }
        public (float X, float Y, float Z) Vec3(IgbObject o, int slot) { var b = Raw(o, slot); return b.Length >= 12 ? (F32(b, 0), F32(b, 4), F32(b, 8)) : (0, 0, 0); }
        public IgbObject? Obj(IgbObject o, int slot)
        {
            var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef");
            int i = v?.AsInt32(_le) ?? -1;
            return i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
        }
        public List<IgbObject> ObjectList(IgbObject list) => f.Is(list, "igObjectList") ? _scene.ObjectList(list) : [];

        // igVec3fList / igFloatList: Int@2 = quantidade, MemoryRef@4 = floats contíguos.
        byte[] Memory(IgbObject list, out int count)
        {
            count = Int(list, 2);
            var v = list.Fields.FirstOrDefault(x => x.Desc.Slot == 4 && x.Desc.ShortName == "MemoryRef");
            int i = v?.AsInt32(_le) ?? -1;
            return i >= 0 && i < f.Slots.Count && f.Slots[i] is IgbMemoryBlock mb ? data.AsSpan((int)mb.FileOffset, mb.Size).ToArray() : [];
        }
        public List<(float X, float Y, float Z)> Vec3List(IgbObject? list)
        {
            if (list == null) return [];
            var m = Memory(list, out int n);
            return Enumerable.Range(0, Math.Min(n, m.Length / 12)).Select(i => (F32(m, 12 * i), F32(m, 12 * i + 4), F32(m, 12 * i + 8))).ToList();
        }
        public List<float> FloatList(IgbObject? list)
        {
            if (list == null) return [];
            var m = Memory(list, out int n);
            return Enumerable.Range(0, Math.Min(n, m.Length / 4)).Select(i => F32(m, 4 * i)).ToList();
        }
        float F32(byte[] b, int at) => _le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
    }
}
