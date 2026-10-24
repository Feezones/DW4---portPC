namespace DW4.Formats.Igb;

/// <summary>Uma entrada de drop (beGeneraterItemDataOne): tabela (nome do objeto), linha (_id, Int@3), _rank (Int@4), _rate (Int@5).</summary>
public sealed record IgbDropEntry(string Table, int Row, int Rank, int Rate);

/// <summary>
/// Listas de drop dos geradores (generatorN/H/V). Campos pelos metadados do main.dol (tabela em 0x804D1E58, lida em
/// 2026-10-24): beGeneraterItemDataOne = nome + _id + _rank + _rate. Cada objeto que aponta para uma igObjectList de
/// beGeneraterItemDataOne vira uma lista com o nome dele (ex.: "nZ1nume1"), na ordem do arquivo, que é a ordem do sorteio.
/// </summary>
public static class IgbDrops
{
    public static Dictionary<string, List<IgbDropEntry>> Read(IgbFile f, byte[] data, List<string> notes)
    {
        var scene = IgbScene.Read(f, data);
        bool le = f.Header.LittleEndian;
        IgbSlot? Slot(int i) => i >= 0 && i < f.Slots.Count ? f.Slots[i] : null;
        var result = new Dictionary<string, List<IgbDropEntry>>();
        foreach (var owner in f.Slots.OfType<IgbObject>())
        {
            foreach (var r in owner.Fields.Where(v => v.Desc.ShortName == "ObjectRef"))
            {
                if (Slot(r.AsInt32(le)) is not IgbObject list || !f.Is(list, "igObjectList")) continue;
                var items = scene.ObjectList(list).Where(o => f.Is(o, "beGeneraterItemDataOne")).ToList();
                if (items.Count == 0) continue;
                var name = owner.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? $"#{owner.Index}";
                var entries = items.Select(o =>
                {
                    int Int(int slot) => o.Fields.FirstOrDefault(v => v.Desc.Slot == slot && v.Desc.ShortName is "Int" or "Enum")?.AsInt32(le) ?? 0;
                    var table = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "String")?.Text ?? "";
                    return new IgbDropEntry(table, Int(3), Int(4), Int(5));
                }).ToList();
                if (result.ContainsKey(name)) notes.Add($"lista \"{name}\" repetida (#{owner.Index}); mantida a primeira");
                else result[name] = entries;
            }
        }
        return result;
    }
}
