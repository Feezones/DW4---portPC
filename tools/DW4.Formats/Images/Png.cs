using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace DW4.Formats.Images;

/// <summary>Gravador mínimo de PNG RGBA 8 bits (filtro 0 em todas as linhas), sem dependências externas.</summary>
public static class Png
{
    static readonly uint[] CrcTable = BuildCrcTable();

    public static void WriteRgba(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != width * height * 4) throw new ArgumentException("tamanho RGBA não confere com largura × altura");
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bits por canal
        ihdr[9] = 6; // RGBA
        Chunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                z.Write(rgba.Slice(y * width * 4, width * 4));
            }
        }
        Chunk(output, "IDAT", raw.ToArray());
        Chunk(output, "IEND", []);
    }

    /// <summary>Lê de volta um PNG RGBA 8 bits sem entrelaçamento (os gravados aqui e os filtros 0..4 em geral).</summary>
    public static (int Width, int Height, byte[] Rgba) ReadRgba(byte[] png)
    {
        int pos = 8, w = 0, h = 0;
        using var idat = new MemoryStream();
        while (pos + 8 <= png.Length)
        {
            int len = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(pos));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            var body = png.AsSpan(pos + 8, len);
            if (type == "IHDR")
            {
                (w, h) = (BinaryPrimitives.ReadInt32BigEndian(body), BinaryPrimitives.ReadInt32BigEndian(body[4..]));
                if (body[8] != 8 || body[9] != 6 || body[12] != 0) throw new InvalidDataException("só PNG RGBA 8 bits sem entrelaçamento");
            }
            else if (type == "IDAT") idat.Write(body);
            else if (type == "IEND") break;
            pos += 12 + len;
        }
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        var raw = new byte[h * (w * 4 + 1)];
        z.ReadExactly(raw);
        var rgba = new byte[w * h * 4];
        int stride = w * 4;
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            for (int x = 0; x < stride; x++)
            {
                int a = x >= 4 ? rgba[y * stride + x - 4] : 0, b = y > 0 ? rgba[(y - 1) * stride + x] : 0, c = x >= 4 && y > 0 ? rgba[(y - 1) * stride + x - 4] : 0;
                int v = raw[y * (stride + 1) + 1 + x];
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                v += f switch { 1 => a, 2 => b, 3 => (a + b) / 2, 4 => pa <= pb && pa <= pc ? a : pb <= pc ? b : c, _ => 0 };
                rgba[y * stride + x] = (byte)v;
            }
        }
        return (w, h, rgba);
    }

    static void Chunk(Stream o, string type, byte[] body)
    {
        Span<byte> u32 = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(u32, body.Length);
        o.Write(u32);
        var t = Encoding.ASCII.GetBytes(type);
        o.Write(t);
        o.Write(body);
        uint crc = 0xFFFFFFFF;
        foreach (var b in t) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (var b in body) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        BinaryPrimitives.WriteUInt32BigEndian(u32, crc ^ 0xFFFFFFFF);
        o.Write(u32);
    }

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }
}
