using System.Globalization;

namespace DW4.Script;

/// <summary>Valores do Lua 4.0: nil (null), número (double), string, LuaTable e LuaFunction. Sem booleanos: verdadeiro = 1.</summary>
public static class Lua
{
    public static readonly object True = 1.0;

    public static string TypeName(object? v) => v switch
    {
        null => "nil",
        double => "number",
        string => "string",
        LuaTable => "table",
        LuaFunction => "function",
        _ => "userdata",
    };

    /// <summary>luaO_str2d: número decimal com espaços em volta; também aceita expoente.</summary>
    public static bool TryNumber(object? v, out double d)
    {
        switch (v)
        {
            case double x: d = x; return true;
            case string s:
                var t = s.Trim(' ', '\t', '\n', '\r', '\f', '\v');
                if (t.Length > 0 && !t.Contains(',') && double.TryParse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out d)) return true;
                break;
        }
        d = 0;
        return false;
    }

    /// <summary>Número em texto como o LUA_NUMBER_FMT "%.16g".</summary>
    public static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        if (d == Math.Floor(d) && Math.Abs(d) < 1e16) return ((long)d).ToString(CultureInfo.InvariantCulture);
        string r = d.ToString("G16", CultureInfo.InvariantCulture);
        int e = r.IndexOf('E');
        if (e < 0) return r;
        // printf: e+XX com pelo menos 2 dígitos no expoente.
        string mant = r[..e], exp = r[(e + 1)..];
        char sign = exp[0] == '-' ? '-' : '+';
        exp = exp.TrimStart('+', '-').TrimStart('0').PadLeft(2, '0');
        return $"{mant}e{sign}{exp}";
    }

    public static string? ToStr(object? v) => v switch
    {
        string s => s,
        double d => NumberToString(d),
        _ => null,
    };

    public static bool RawEquals(object? a, object? b) => a switch
    {
        null => b == null,
        double x => b is double y && x == y,
        string s => b is string t && string.Equals(s, t, StringComparison.Ordinal),
        _ => ReferenceEquals(a, b),
    };

    /// <summary>luaV_lessthan: números ou strings (strcoll; aqui ordem de bytes, igual ao locale "C").</summary>
    public static bool LessThan(object? a, object? b)
    {
        if (a is double x && b is double y) return x < y;
        if (a is string s && b is string t) return string.CompareOrdinal(s, t) < 0;
        throw new LuaException($"attempt to compare {TypeName(a)} with {TypeName(b)}");
    }

    public static bool Truthy(object? v) => v != null;
}
