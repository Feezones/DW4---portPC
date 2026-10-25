namespace DW4.Formats.Igb;

/// <summary>Objeto de som de um IGB de fase com os valores em ordem de slot (beSoundData, beCriSfpData, beSeData…).</summary>
public sealed record IgbSoundObject(int Index, string Type, IReadOnlyList<object> Values);

/// <summary>Sons por ação de um ator (beSeInfoData "p_girumon" → beSeData(ação, som, atraso)).</summary>
public sealed record IgbSeInfo(int Index, string Name, IReadOnlyList<IgbSoundObject> Entries);

/// <summary>
/// Tabelas de som de um IGB de fase (levantamento de 2026-10-24): beSoundData (nome, tipo, índice na lista do
/// beSoundInfo, cue 0xA9GGCC00 ou −1 para stream ADX, canais, volume), beCriSfpData (bancos .sfp) e beSeInfoData.
/// Os valores saem crus, na ordem dos slots; o significado de cada um está em docs/gameplay/fase4-levantamento-audio.md.
/// </summary>
public static class IgbSounds
{
    public static (List<IgbSoundObject> Sounds, List<IgbSoundObject> Banks, List<IgbSeInfo> Se) Read(IgbFile f, byte[] data)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        IgbSoundObject Obj(IgbObject o) => new(o.Index, o.TypeName, IgbSequences.Values(o, le));
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
        return (sounds, banks, se);
    }
}
