using System.Buffers.Binary;

namespace DW4.Formats.Igb;

/// <summary>Uma vaga de entrada: posição, giro em Y (graus, frente = +Z local girada) e a matriz em linhas.</summary>
public sealed record IgbEntrySlot(int Player, (float X, float Y, float Z) Position, float RotYDeg, float[] Matrix);

/// <summary>Um ponto de entrada (beGroupKeep): caixa e as vagas, na ordem da lista.</summary>
public sealed record IgbEntryPoint(int Index, (float X, float Y, float Z) Min, (float X, float Y, float Z) Max, List<IgbEntrySlot> Slots);

/// <summary>
/// Pontos de entrada do jogador (IGB principal da fase). Levantamento de 2026-10-25 no #179:
///   bePoint01Info [@7 igSwitch, @8 beGroupKeepList]; beGroupKeep [@3 igAABox, @7 igNodeList de igTransform].
///   FATO: na dng1 são 2 grupos de 4 igTransform: o 0 em (−300, 40, 431..452) virado 180° (junto do matz01) e o 1 em
///   (324..345, 24..16, −440) virado −90° (junto do matz02). O enum do bePoint01Info no main.dol (0x804D0010) tem
///   SHOWPOLYGON, HIDEPOLYGON, GETMATRIX, GETMATRIXDIRECT, GETPOINTNAME, SETGENERATEPOS, GETGENERATEPOS.
///   HIPÓTESE: SETGENERATEPOS "I&lt;n&gt;" escolhe o grupo n e a vaga k é a do jogador k.
/// </summary>
public static class IgbEntryPoints
{
    public static List<IgbEntryPoint> Read(IgbFile f, byte[] data)
    {
        bool le = f.Header.LittleEndian;
        var scene = IgbScene.Read(f, data);
        float F32(byte[] b, int at) => le ? BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(at));
        IgbObject? Obj(IgbObject o, int slot)
        {
            var v = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot && x.Desc.ShortName == "ObjectRef");
            int i = v?.AsInt32(le) ?? -1;
            return i >= 0 && i < f.Slots.Count ? f.Slots[i] as IgbObject : null;
        }
        (float, float, float) Vec(IgbObject o, int slot)
        {
            var b = o.Fields.FirstOrDefault(x => x.Desc.Slot == slot)?.Raw ?? [];
            return b.Length >= 12 ? (F32(b, 0), F32(b, 4), F32(b, 8)) : (0, 0, 0);
        }

        var list = new List<IgbEntryPoint>();
        foreach (var info in f.Slots.OfType<IgbObject>().Where(o => o.TypeName == "bePoint01Info"))
        {
            if (Obj(info, 8) is not { } groups) continue;
            foreach (var g in scene.ObjectList(groups).Where(o => o.TypeName == "beGroupKeep"))
            {
                var box = Obj(g, 3);
                var slots = new List<IgbEntrySlot>();
                var nodes = Obj(g, 7) is { } nl ? scene.ObjectList(nl) : [];
                foreach (var t in nodes.Where(n => f.Is(n, "igTransform")))
                {
                    var raw = t.Fields.FirstOrDefault(x => x.Desc.ShortName == "Matrix44f" && x.Raw.Length >= 64)?.Raw;
                    if (raw == null) continue;
                    var m = Enumerable.Range(0, 16).Select(k => F32(raw, 4 * k)).ToArray();
                    // Em linhas: a linha 2 é a imagem do +Z local; giro Y = atan2(z.x, z.z).
                    float rot = MathF.Round(MathF.Atan2(m[8], m[10]) * 180 / MathF.PI, 3);
                    slots.Add(new IgbEntrySlot(slots.Count, (m[12], m[13], m[14]), rot == -180 ? 180 : rot, m));
                }
                list.Add(new IgbEntryPoint(list.Count, box != null ? Vec(box, 2) : (0, 0, 0), box != null ? Vec(box, 3) : (0, 0, 0), slots));
            }
        }
        return list;
    }
}
