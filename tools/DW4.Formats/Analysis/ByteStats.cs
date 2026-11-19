namespace DW4.Formats.Analysis;

public static class ByteStats
{
    /// <summary>Entropia de Shannon em bits por byte (0 a 8).</summary>
    public static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (byte b in data) counts[b]++;
        double e = 0;
        foreach (int c in counts)
        {
            if (c == 0) continue;
            double p = (double)c / data.Length;
            e -= p * Math.Log2(p);
        }
        return e;
    }

    /// <summary>Bytes em hexadecimal maiúsculo separados por espaço, como no inventário.</summary>
    public static string Hex(ReadOnlySpan<byte> data)
    {
        var parts = new string[data.Length];
        for (int i = 0; i < data.Length; i++) parts[i] = data[i].ToString("X2");
        return string.Join(' ', parts);
    }

    /// <summary>ASCII imprimível; demais bytes viram '.'.</summary>
    public static string Ascii(ReadOnlySpan<byte> data)
    {
        var chars = new char[data.Length];
        for (int i = 0; i < data.Length; i++)
            chars[i] = data[i] is >= 32 and < 127 ? (char)data[i] : '.';
        return new string(chars);
    }
}
