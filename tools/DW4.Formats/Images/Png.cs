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
