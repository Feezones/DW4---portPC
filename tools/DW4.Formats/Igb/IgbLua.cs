using DW4.Formats.Lua;

namespace DW4.Formats.Igb;

/// <summary>
/// Scripts de um IGB de fase: cada beLuaData liga o nome do arquivo original ("boot.lua") a um beLuaDataObject, que aponta
/// para o bloco de memória com o chunk Lua 4.0 (docs/formatos/lua.md). Vários nomes podem apontar para o mesmo chunk.
/// </summary>
public static class IgbLua
{
    public static Dictionary<string, byte[]> Chunks(IgbFile f, byte[] data)
    {
        bool le = f.Header.LittleEndian;
        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var o in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "beLuaData"))
        {
            var name = o.Fields.FirstOrDefault(v => v.Text != null)?.Text;
            var rf = o.Fields.FirstOrDefault(v => v.Desc.ShortName == "ObjectRef");
            if (name == null || rf == null) continue;
            int t = rf.AsInt32(le);
            if (t < 0 || t >= f.Slots.Count || f.Slots[t] is not IgbObject lo) continue;
            foreach (var v in lo.Fields.Where(v => v.Desc.ShortName == "MemoryRef"))
            {
                int r = v.AsInt32(le);
                if (r < 0 || r >= f.Slots.Count || f.Slots[r] is not IgbMemoryBlock mb || mb.Size == 0) continue;
                var mem = data.AsSpan((int)mb.FileOffset, mb.Size).ToArray();
                if (Lua40Chunk.LooksLikeChunk(mem)) chunks[name] = mem;
            }
        }
        return chunks;
    }
}
