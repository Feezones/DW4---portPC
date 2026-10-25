namespace DW4.Formats.Igb;

/// <summary>Objeto de som de um IGB de fase com os valores em ordem de slot (beSoundData, beCriSfpData, beSeData…).</summary>
public sealed record IgbSoundObject(int Index, string Type, IReadOnlyList<object> Values);

/// <summary>Lista de arquivos de um beSoundInfo: textos dos objetos alcançáveis, na ordem do arquivo ("sound/sfp/GC/x.sfp", "[0.35]").</summary>
public sealed record IgbSoundInfo(int Index, IReadOnlyList<string> Strings);

/// <summary>Sons por ação de um ator (beSeInfoData "p_girumon" → beSeData(ação, som, atraso)).</summary>
public sealed record IgbSeInfo(int Index, string Name, IReadOnlyList<IgbSoundObject> Entries);

/// <summary>
/// Tabelas de som de um IGB de fase (levantamento de 2026-10-24): beSoundData (nome, tipo, índice na lista do
/// beSoundInfo, cue 0xA9GGCC00 ou −1 para stream ADX, canais, volume), beCriSfpData (bancos .sfp) e beSeInfoData.
/// Os valores saem crus, na ordem dos slots; o significado de cada um está em docs/gameplay/fase4-levantamento-audio.md.
/// </summary>
public static class IgbSounds
{
    public static (List<IgbSoundObject> Sounds, List<IgbSoundObject> Banks, List<IgbSeInfo> Se, List<IgbSoundInfo> Info) Read(IgbFile f, byte[] data)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        IgbSoundObject Obj(IgbObject o) => new(o.Index, o.TypeName, Values(o, le));
        var objects = f.Slots.OfType<IgbObject>().ToList();
        var sounds = objects.Where(o => o.TypeName == "beSoundData").Select(Obj).ToList();
        var banks = objects.Where(o => o.TypeName == "beCriSfpData").Select(Obj).ToList();
        var se = new List<IgbSeInfo>();
        foreach (var info in objects.Where(o => o.TypeName == "beSeInfoData"))
        {
            var name = info.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? $"#{info.Index}";
            var entries = info.Fields.Where(v => v.Desc.ShortName == "ObjectRef").Select(v => Slot(v.AsInt32(le))).OfType<IgbObject>()
                .Where(l => f.Is(l, "igObjectList")).SelectMany(l => scene.ObjectList(l)).Select(Obj).ToList();
            se.Add(new IgbSeInfo(info.Index, name, entries));
        }
        var info2 = objects.Where(o => o.TypeName == "beSoundInfo").Select(o => new IgbSoundInfo(o.Index,
            scene.Reachable(o).Order().Select(i => Slot(i)).OfType<IgbObject>()
                .SelectMany(x => x.Fields.Where(v => v.Desc.ShortName == "String" && v.Text != null).Select(v => v.Text!)).ToList())).ToList();
        return (sounds, banks, se, info2);
    }

    // Como IgbSequences.Values, mais Short/UnsignedShort/Char (S4..S9 do beSoundData, atraso do beSeData).
    static List<object> Values(IgbObject o, bool le)
    {
        var values = new List<object>();
        foreach (var v in o.Fields.OrderBy(v => v.Desc.Slot))
        {
            var r = v.Raw;
            switch (v.Desc.ShortName)
            {
                case "String": values.Add(v.Text ?? ""); break;
                case "Float" when r.Length >= 4: values.Add(le ? System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(r) : System.Buffers.Binary.BinaryPrimitives.ReadSingleBigEndian(r)); break;
                case "Int" or "Enum" or "UnsignedInt" when r.Length == 4: values.Add(v.AsInt32(le)); break;
                case "Short" when r.Length == 2: values.Add((int)(le ? System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(r) : System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(r))); break;
                case "UnsignedShort" when r.Length == 2: values.Add((int)(le ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(r) : System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(r))); break;
                case "Char" or "UnsignedChar" when r.Length == 1: values.Add((int)(v.Desc.ShortName == "Char" ? (sbyte)r[0] : r[0])); break;
                case "Bool" when r.Length == 1: values.Add(r[0] != 0); break;
            }
        }
        return values;
    }
}
