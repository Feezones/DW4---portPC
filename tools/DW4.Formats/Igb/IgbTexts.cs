using System.Text;

namespace DW4.Formats.Igb;

/// <summary>Um texto do pacote de idioma: posição na beUnicodeObjList, chave (mswkNNNNN) e o texto.</summary>
public sealed record IgbText(int Index, string Key, string Text);

/// <summary>
/// Textos de um *_pack.igb (usa_pack = inglês). Levantamento de 2026-10-25 no area #9 (usa_pack da dng1):
///   beFontInfo [@7 beUnicodeObjList]; beUnicodeObj [@2 chave, @3 memória UTF-16LE terminada em 0].
///   FATO: os scripts usam mswkNNNNN = "TALKFONTxx/fontyy/I&lt;k&gt;" e k é a posição na lista (mswk65000 = I&lt;9&gt; é o
///   item 9 da lista do area #9, "Open Map" / "Humid Cave B1").
/// </summary>
public static class IgbTexts
{
    public static List<IgbText> Read(IgbFile f, byte[] data)
    {
        bool le = f.Header.LittleEndian;
        var scene = IgbScene.Read(f, data);
        var list = new List<IgbText>();
        foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beFontInfo"))
        {
            var v = info.Fields.FirstOrDefault(x => x.Desc.Slot == 7 && x.Desc.ShortName == "ObjectRef");
            int li = v?.AsInt32(le) ?? -1;
            if (li < 0 || li >= f.Slots.Count || f.Slots[li] is not IgbObject ul) continue;
            var items = scene.ObjectList(ul);
            for (int i = 0; i < items.Count; i++)
            {
                var u = items[i];
                string key = u.Fields.FirstOrDefault(x => x.Desc.Slot == 2 && x.Desc.ShortName == "String")?.Text ?? $"#{u.Index}";
                var m = u.Fields.FirstOrDefault(x => x.Desc.Slot == 3 && x.Desc.ShortName == "MemoryRef");
                int mi = m?.AsInt32(le) ?? -1;
                string text = mi >= 0 && mi < f.Slots.Count && f.Slots[mi] is IgbMemoryBlock mb
                    ? Decode(data.AsSpan((int)mb.FileOffset, mb.Size)) : "";
                list.Add(new IgbText(i, key, text));
            }
        }
        return list;
    }

    /// <summary>UTF-16LE até o primeiro 0.</summary>
    public static string Decode(ReadOnlySpan<byte> b)
    {
        int n = 0;
        while (n + 1 < b.Length && (b[n] | b[n + 1]) != 0) n += 2;
        return Encoding.Unicode.GetString(b[..n]);
    }
}
