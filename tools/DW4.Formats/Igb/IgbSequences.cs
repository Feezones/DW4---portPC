using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Um comando de sequência (beModelCtrlHITBOX, beModelCtrlACTION…), com os campos de valor em ordem de slot.</summary>
public sealed record IgbSeqCommand(int Index, string Kind, IReadOnlyList<object> Values);

/// <summary>
/// Sequências de controle de um ator (beModelCtrlData String@2 = nome, ObjectRef → igNamedObjectList de comandos).
/// Estrutura conferida no Guilmon (#857) em 2026-10-24, ver docs/gameplay/fase3-levantamento.md. O significado de cada
/// comando é HIPÓTESE; aqui só se lê a ordem e os valores.
/// </summary>
public static class IgbSequences
{
    public static Dictionary<string, List<IgbSeqCommand>> Read(IgbFile f, byte[] data, string? actor, List<string> notes)
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
        var result = new Dictionary<string, List<IgbSeqCommand>>();
        foreach (var d in f.Slots.OfType<IgbObject>().Where(o => f.Is(o, "beModelCtrlData") && (only == null || only.Contains(o.Index))))
        {
            var name = d.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? $"#{d.Index}";
            var list = d.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>()
                .FirstOrDefault(r => f.Is(r, "igObjectList"));
            var cmds = new List<IgbSeqCommand>();
            if (list != null)
                foreach (var c in scene.ObjectList(list))
                {
                    var kind = c.TypeName.StartsWith("beModelCtrl", StringComparison.Ordinal) ? c.TypeName["beModelCtrl".Length..] : c.TypeName;
                    cmds.Add(new IgbSeqCommand(c.Index, kind, Values(c, le)));
                }
            if (result.ContainsKey(name)) notes.Add($"sequência \"{name}\" repetida (#{d.Index}); mantida a primeira");
            else result[name] = cmds;
        }
        return result;
    }

    static List<object> Values(IgbObject o, bool le)
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
