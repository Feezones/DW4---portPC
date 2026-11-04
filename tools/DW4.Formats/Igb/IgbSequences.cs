using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Um comando de sequência (beModelCtrlHITBOX, beModelCtrlACTION…), com os campos de valor em ordem de slot.</summary>
public sealed record IgbSeqCommand(int Index, string Kind, IReadOnlyList<object> Values);

/// <summary>
/// Mapa de IA de um ator (beModelCtrlAIMap): nome, os quatro floats @3..@6, as quatro listas de beModelCtrlAIData
/// (@7..@10, cada item = sequência, Float@3, Float@4, Int@5) e as strings @11/@12. Significado dos campos é HIPÓTESE.
/// </summary>
public sealed record IgbAiMap(int Index, string Name, float[] Values, IReadOnlyList<IReadOnlyList<IgbSeqCommand>> Lists, string[] Strings);

/// <summary>
/// Sequências de controle de um ator (beModelCtrlData String@2 = nome, ObjectRef → igNamedObjectList de comandos).
/// Estrutura conferida no Guilmon (#857) em 2026-10-24, ver docs/gameplay/fase3-levantamento.md. O significado de cada
/// comando é HIPÓTESE; aqui só se lê a ordem e os valores.
/// </summary>
public static class IgbSequences
{
    public static Dictionary<string, List<IgbSeqCommand>> Read(IgbFile f, byte[] data, string? actor, List<string> notes) =>
        ReadLists(f, data, actor, notes, "beModelCtrlInfo", "beModelCtrlData", "beModelCtrl");

    /// <summary>
    /// Ações de um ator (beAction2Data String@2 = nome da ACTION, ObjectRef → igNamedObjectList de comandos beAction2*,
    /// ex.: MOTIONSET com o nome da animação). É o elo entre a ACTION "VATCS1" e a animação "vatcS".
    /// </summary>
    public static Dictionary<string, List<IgbSeqCommand>> ReadActions(IgbFile f, byte[] data, string? actor, List<string> notes) =>
        ReadLists(f, data, actor, notes, "beAction2Info", "beAction2Data", "beAction2");

    /// <summary>
    /// Mapas de IA alcançáveis a partir do beModelCtrlInfo do ator (cadeia conferida no Numemon do #179 em 2026-10-24:
    /// beModelCtrlInfo → beModelCtrlInfoDataAIM → beModelCtrlAIMapList → beModelCtrlAIMap).
    /// </summary>
    public static List<IgbAiMap> ReadAiMaps(IgbFile f, byte[] data, string? actor, List<string> notes)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        HashSet<int>? only = null;
        if (actor != null)
        {
            var info = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "beModelCtrlInfo")
                && o.Fields.FirstOrDefault(v => v.Desc.ShortName == "String" && v.Desc.Slot == 2)?.Text == actor);
            if (info == null) { notes.Add($"ator \"{actor}\" sem beModelCtrlInfo"); return []; }
            only = scene.Reachable(info);
        }
        var result = new List<IgbAiMap>();
        foreach (var m in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "beModelCtrlAIMap") && !f.Is(o, "beModelCtrlAIMapList")
                     && (only == null || only.Contains(o.Index))))
        {
            var fields = m.Fields.OrderBy(v => v.Desc.Slot).ToList();
            var name = fields.FirstOrDefault(v => v.Desc.ShortName == "String" && v.Desc.Slot == 2)?.Text ?? $"#{m.Index}";
            var floats = Values(m, le).OfType<float>().ToArray();
            var strings = fields.Where(v => v.Desc.ShortName == "String" && v.Desc.Slot > 2).Select(v => v.Text ?? "").ToArray();
            var lists = new List<IReadOnlyList<IgbSeqCommand>>();
            foreach (var r in fields.Where(v => v.Desc.ShortName == "ObjectRef"))
            {
                var list = Slot(r.AsInt32(le)) as IgbObject;
                lists.Add(list == null ? [] : scene.ObjectList(list).Select(c => new IgbSeqCommand(c.Index, "AI", Values(c, le))).ToList());
            }
            result.Add(new IgbAiMap(m.Index, name, floats, lists, strings));
        }
        return result;
    }

    /// <summary>
    /// Caixas do corpo do ator (FATO, Guilmon #857 e Numemon #179: beModelCtrlInfo → beModelCtrlInfoDataHit "HIT" → igAABoxList →
    /// igAABox min/max, no espaço do ator). Não há osso: cada caixa é fixa no ator. Vazio se o ator não tem.
    /// </summary>
    public static List<(float[] Min, float[] Max)> ReadHitBody(IgbFile f, byte[] data, string? actor, List<string> notes)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        HashSet<int>? only = null;
        if (actor != null)
        {
            var info = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, "beModelCtrlInfo")
                && o.Fields.FirstOrDefault(v => v.Desc.ShortName == "String" && v.Desc.Slot == 2)?.Text == actor);
            if (info == null) return [];
            only = scene.Reachable(info);
        }
        var result = new List<(float[], float[])>();
        foreach (var hit in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "beModelCtrlInfoDataHit") && (only == null || only.Contains(o.Index))))
            foreach (var r in hit.Fields.Where(v => v.Desc.ShortName == "ObjectRef"))
                if (Slot(r.AsInt32(le)) is IgbObject list && f.Is(list, "igAABoxList"))
                    foreach (var box in scene.ObjectList(list))
                    {
                        var v = Values(box, le).OfType<float[]>().ToList();
                        if (v.Count >= 2) result.Add((v[0], v[1]));
                    }
        if (result.Count == 0 && actor != null) notes.Add($"ator \"{actor}\" sem caixa de corpo (beModelCtrlInfoDataHit)");
        return result;
    }

    static Dictionary<string, List<IgbSeqCommand>> ReadLists(IgbFile f, byte[] data, string? actor, List<string> notes,
        string infoClass, string dataClass, string prefix)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        HashSet<int>? only = null;
        if (actor != null)
        {
            var info = f.Slots.OfType<IgbObject>().FirstOrDefault(o => f.Is(o, infoClass)
                && o.Fields.FirstOrDefault(v => v.Desc.ShortName == "String" && v.Desc.Slot == 2)?.Text == actor);
            if (info == null) { notes.Add($"ator \"{actor}\" sem {infoClass}"); return []; }
            only = scene.Reachable(info);
        }
        var result = new Dictionary<string, List<IgbSeqCommand>>();
        foreach (var d in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, dataClass) && (only == null || only.Contains(o.Index))))
        {
            var name = d.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? $"#{d.Index}";
            var list = d.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>()
                .FirstOrDefault(r => f.Is(r, "igObjectList"));
            var cmds = new List<IgbSeqCommand>();
            if (list != null)
                foreach (var c in scene.ObjectList(list))
                {
                    var kind = c.TypeName.StartsWith(prefix, StringComparison.Ordinal) ? c.TypeName[prefix.Length..] : c.TypeName;
                    cmds.Add(new IgbSeqCommand(c.Index, kind, Values(c, le)));
                }
            if (result.ContainsKey(name)) notes.Add($"{dataClass} \"{name}\" repetido (#{d.Index}); mantida a primeira");
            else result[name] = cmds;
        }
        return result;
    }

    internal static List<object> Values(IgbObject o, bool le)
    {
        float F(byte[] b, int at) => le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
        var values = new List<object>();
        foreach (var v in o.Fields.OrderBy(v => v.Desc.Slot))
        {
            switch (v.Desc.ShortName)
            {
                case "String": values.Add(v.Text ?? ""); break;
                case "Float" when v.Raw.Length >= 4: values.Add(F(v.Raw, 0)); break;
                case "Vec3f" when v.Raw.Length >= 12: values.Add(new[] { F(v.Raw, 0), F(v.Raw, 4), F(v.Raw, 8) }); break;
                case "Int" or "Enum" or "UnsignedInt" when v.Raw.Length == 4: values.Add(v.AsInt32(le)); break;
                case "Bool" when v.Raw.Length == 1: values.Add(v.Raw[0] != 0); break;
            }
        }
        return values;
    }
}
