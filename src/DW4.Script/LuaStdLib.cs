using System.Globalization;
using System.Text;

namespace DW4.Script;

/// <summary>
/// Biblioteca padrão mínima do Lua 4.0 (lbaselib/lstrlib/lmathlib da tag v4.0, MIT) para os scripts do jogo. Sem io, os,
/// dofile, dostring, loadfile nem nada que toque o PC. As funções seguem a semântica do 4.0 (getn pelo campo n,
/// tinsert/tremove, foreach/foreachi, str* e as matemáticas em graus).
/// </summary>
public static class LuaStdLib
{
    static object?[] One(object? v) => [v];
    static readonly object?[] None = [];

    static double Num(object?[] a, int i, string fn) =>
        i < a.Length && Lua.TryNumber(a[i], out double d) ? d : throw new LuaException($"bad argument #{i + 1} to `{fn}' (number expected)");

    static string Str(object?[] a, int i, string fn) =>
        i < a.Length && Lua.ToStr(a[i]) is { } s ? s : throw new LuaException($"bad argument #{i + 1} to `{fn}' (string expected)");

    static LuaTable Tab(object?[] a, int i, string fn) =>
        i < a.Length && a[i] is LuaTable t ? t : throw new LuaException($"bad argument #{i + 1} to `{fn}' (table expected)");

    static object? Arg(object?[] a, int i) => i < a.Length ? a[i] : null;

    /// <summary>rand() do MSL (CodeWarrior): HIPÓTESE pelo SDK usado no jogo (RAND_MAX = 32767). Semente 1 como no C.</summary>
    public sealed class MslRand
    {
        uint _next = 1;
        public void Seed(uint s) => _next = s;
        public int Next()
        {
            _next = unchecked(_next * 1103515245 + 12345);
            return (int)((_next >> 16) & 0x7FFF);
        }
    }

    public static void Register(LuaVM vm)
    {
        var rand = new MslRand();
        var g = vm.Globals;
        void R(string name, Func<object?[], object?[]> f) => g[name] = new LuaHostFunction(name, f);

        // lbaselib
        R("print", a => { vm.Print?.Invoke(string.Join("\t", a.Select(v => Lua.ToStr(v) ?? (v == null ? "nil" : $"{Lua.TypeName(v)}")))); return None; });
        R("type", a => One(Lua.TypeName(Arg(a, 0))));
        R("tostring", a => One(Lua.ToStr(Arg(a, 0)) ?? (Arg(a, 0) == null ? "nil" : $"{Lua.TypeName(Arg(a, 0))}: {(Arg(a, 0) is LuaTable t ? t.Id : Arg(a, 0) is LuaFunction f ? f.Id : 0):X8}")));
        R("tonumber", a =>
        {
            if (a.Length > 1 && Arg(a, 1) != null)
            {
                int b = (int)Num(a, 1, "tonumber");
                try { return One((double)Convert.ToInt64(Str(a, 0, "tonumber").Trim(), b)); } catch (Exception e) when (e is FormatException or ArgumentException or OverflowException) { return One(null); }
            }
            return One(Lua.TryNumber(Arg(a, 0), out double d) ? d : null);
        });
        R("getn", a => One((double)Tab(a, 0, "getn").GetN()));
        R("tinsert", a =>
        {
            // lbaselib 4.0: grava t.n = n+1 sempre (mesmo sem campo n antes) e o valor é o ÚLTIMO argumento.
            var t = Tab(a, 0, "tinsert");
            int n = t.GetN();
            int pos = a.Length == 2 ? n + 1 : (int)Num(a, 1, "tinsert");
            object? v = a[^1];
            t.Set("n", (double)(n + 1));
            for (; n >= pos; n--) t.Set((double)(n + 1), t.RawGet((double)n));
            t.Set((double)pos, v);
            return None;
        });
        R("tremove", a =>
        {
            var t = Tab(a, 0, "tremove");
            int n = t.GetN();
            int pos = a.Length > 1 && a[1] != null ? (int)Num(a, 1, "tremove") : n;
            if (n <= 0) return None;
            var v = t.RawGet((double)pos);
            for (; pos < n; pos++) t.Set((double)pos, t.RawGet((double)(pos + 1)));
            t.Set("n", (double)(n - 1));
            t.Set((double)n, null);
            return One(v);
        });
        R("sort", a =>
        {
            var t = Tab(a, 0, "sort");
            int n = t.GetN();
            var f = Arg(a, 1);
            var arr = new object?[n + 2]; // +1 de folga: o auxsort lê a[u+1] antes de acusar função de ordem inválida
            for (int i = 1; i <= n; i++) arr[i] = t.RawGet((double)i);
            var orig = (object?[])arr.Clone();
            bool Lt(object? x, object? y) => f != null ? (vm.Call(f, x, y) is { Length: > 0 } r && r[0] != null) : Lua.LessThan(x, y);
            AuxSort(arr, 1, n, Lt);
            for (int i = 1; i <= n; i++) if (!ReferenceEquals(arr[i], orig[i]) && !Lua.RawEquals(arr[i], orig[i])) t.Set((double)i, arr[i]);
            return None;
        });
        R("getglobal", a => One(g.Get(Str(a, 0, "getglobal"))));
        R("setglobal", a => { g.Set(Str(a, 0, "setglobal"), Arg(a, 1)); return None; });
        R("globals", a => One(g));
        R("foreach", a =>
        {
            var t = Tab(a, 0, "foreach");
            object? k = null;
            while (t.Next(k, out k, out var v))
            {
                var r = vm.Call(Arg(a, 1), k, v);
                if (r.Length > 0 && r[0] != null) return [r[0]];
            }
            return None;
        });
        R("foreachi", a =>
        {
            var t = Tab(a, 0, "foreachi");
            int n = t.GetN();
            for (int i = 1; i <= n; i++)
            {
                var r = vm.Call(Arg(a, 1), (double)i, t.RawGet((double)i));
                if (r.Length > 0 && r[0] != null) return [r[0]];
            }
            return None;
        });
        R("next", a => Tab(a, 0, "next").Next(Arg(a, 1), out var k, out var v) ? [k, v] : One(null));
        R("rawget", a => One(Tab(a, 0, "rawget").RawGet(Arg(a, 1))));
        R("rawset", a => { Tab(a, 0, "rawset").Set(Arg(a, 1), Arg(a, 2)); return One(a[0]); });
        R("error", a => throw new LuaException(Lua.ToStr(Arg(a, 0)) ?? "error"));
        R("assert", a => Arg(a, 0) != null ? None : throw new LuaException($"assertion failed! {Lua.ToStr(Arg(a, 1))}"));
        R("call", a =>
        {
            var args = Tab(a, 1, "call");
            int n = args.GetN();
            var list = new object?[n];
            for (int i = 0; i < n; i++) list[i] = args.RawGet((double)(i + 1));
            return vm.Call(Arg(a, 0), list);
        });

        // lstrlib (índices de 1; negativos contam do fim)
        R("strlen", a => One((double)Str(a, 0, "strlen").Length));
        R("strsub", a =>
        {
            var s = Str(a, 0, "strsub");
            int l = s.Length;
            int i = a.Length > 1 ? (int)Num(a, 1, "strsub") : 1, j = a.Length > 2 && a[2] != null ? (int)Num(a, 2, "strsub") : -1;
            if (i < 0) i = l + i + 1;
            if (j < 0) j = l + j + 1;
            if (i < 1) i = 1;
            if (j > l) j = l;
            return One(i <= j ? s.Substring(i - 1, j - i + 1) : "");
        });
        R("strlower", a => One(Str(a, 0, "strlower").ToLowerInvariant()));
        R("strupper", a => One(Str(a, 0, "strupper").ToUpperInvariant()));
        R("strrep", a => One(string.Concat(Enumerable.Repeat(Str(a, 0, "strrep"), Math.Max(0, (int)Num(a, 1, "strrep"))))));
        R("strbyte", a =>
        {
            var s = Str(a, 0, "strbyte");
            int i = a.Length > 1 ? (int)Num(a, 1, "strbyte") : 1;
            if (i < 0) i = s.Length + i + 1;
            return One(i >= 1 && i <= s.Length ? (double)(byte)s[i - 1] : null);
        });
        R("strchar", a => One(new string(a.Select((_, i) => (char)(byte)Num(a, i, "strchar")).ToArray())));
        R("format", a => One(Format(Str(a, 0, "format"), a)));
        R("strfind", a =>
        {
            // Só busca simples (sem padrões); padrões do Lua 4.0 ainda não implementados.
            var s = Str(a, 0, "strfind");
            var pat = Str(a, 1, "strfind");
            int init = a.Length > 2 && a[2] != null ? (int)Num(a, 2, "strfind") : 1;
            if (init < 0) init = s.Length + init + 1;
            int at = s.IndexOf(pat, Math.Clamp(init - 1, 0, s.Length), StringComparison.Ordinal);
            return at < 0 ? One(null) : [(double)(at + 1), (double)(at + pat.Length)];
        });

        // lmathlib (graus, como no 4.0)
        const double Rad = Math.PI / 180;
        R("abs", a => One(Math.Abs(Num(a, 0, "abs"))));
        R("floor", a => One(Math.Floor(Num(a, 0, "floor"))));
        R("ceil", a => One(Math.Ceiling(Num(a, 0, "ceil"))));
        R("sqrt", a => One(Math.Sqrt(Num(a, 0, "sqrt"))));
        R("sin", a => One(Math.Sin(Num(a, 0, "sin") * Rad)));
        R("cos", a => One(Math.Cos(Num(a, 0, "cos") * Rad)));
        R("tan", a => One(Math.Tan(Num(a, 0, "tan") * Rad)));
        R("asin", a => One(Math.Asin(Num(a, 0, "asin")) / Rad));
        R("acos", a => One(Math.Acos(Num(a, 0, "acos")) / Rad));
        R("atan", a => One(Math.Atan(Num(a, 0, "atan")) / Rad));
        R("atan2", a => One(Math.Atan2(Num(a, 0, "atan2"), Num(a, 1, "atan2")) / Rad));
        R("mod", a => One(Num(a, 0, "mod") % Num(a, 1, "mod"))); // fmod do C
        R("min", a => One(a.Select((_, i) => Num(a, i, "min")).Min()));
        R("max", a => One(a.Select((_, i) => Num(a, i, "max")).Max()));
        R("pow", a => One(Math.Pow(Num(a, 0, "pow"), Num(a, 1, "pow"))));
        R("exp", a => One(Math.Exp(Num(a, 0, "exp"))));
        R("log", a => One(Math.Log(Num(a, 0, "log"))));
        R("log10", a => One(Math.Log10(Num(a, 0, "log10"))));
        R("random", a =>
        {
            double r = (double)(rand.Next() % 32767) / 32767; // lmathlib 4.0: (rand() % RAND_MAX) / RAND_MAX
            return a.Length switch
            {
                0 => One(r),
                1 => One(Math.Floor(r * Num(a, 0, "random")) + 1),
                _ => One(Math.Floor(r * (Num(a, 1, "random") - Num(a, 0, "random") + 1)) + Num(a, 0, "random")),
            };
        });
        R("randomseed", a => { rand.Seed((uint)(int)Num(a, 0, "randomseed")); return None; });
        g["PI"] = Math.PI;
    }

    // format do Lua 4.0: %d %i %u %c %x %X %o %e %E %f %g %G %q %s %%, com flags, largura e precisão.
    static string Format(string fmt, object?[] a)
    {
        var sb = new StringBuilder();
        int arg = 1;
        for (int i = 0; i < fmt.Length; i++)
        {
            char c = fmt[i];
            if (c != '%') { sb.Append(c); continue; }
            if (++i >= fmt.Length) break;
            if (fmt[i] == '%') { sb.Append('%'); continue; }
            int start = i;
            while (i < fmt.Length && "-+ #0".Contains(fmt[i])) i++;
            string flags = fmt[start..i];
            while (i < fmt.Length && char.IsDigit(fmt[i])) i++;
            if (i < fmt.Length && fmt[i] == '.') { i++; while (i < fmt.Length && char.IsDigit(fmt[i])) i++; }
            if (i >= fmt.Length) break;
            string spec = fmt[start..i];
            char conv = fmt[i];
            bool left = flags.Contains('-'), zero = flags.Contains('0') && !left, plus = flags.Contains('+');
            var parts = spec[flags.Length..].Split('.');
            int width = parts[0].Length > 0 ? int.Parse(parts[0], CultureInfo.InvariantCulture) : 0;
            int? prec = parts.Length > 1 ? (parts[1].Length > 0 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0) : null;
            string body = conv switch
            {
                'd' or 'i' => ((int)Num(a, arg++, "format")).ToString(CultureInfo.InvariantCulture),
                'u' => ((uint)(int)Num(a, arg++, "format")).ToString(CultureInfo.InvariantCulture),
                'c' => ((char)(byte)Num(a, arg++, "format")).ToString(),
                'x' => ((uint)(int)Num(a, arg++, "format")).ToString("x", CultureInfo.InvariantCulture),
                'X' => ((uint)(int)Num(a, arg++, "format")).ToString("X", CultureInfo.InvariantCulture),
                'o' => Convert.ToString((int)Num(a, arg++, "format"), 8),
                'f' => Num(a, arg++, "format").ToString("F" + (prec ?? 6), CultureInfo.InvariantCulture),
                'e' or 'E' => CExp(Num(a, arg++, "format"), prec ?? 6, conv),
                'g' or 'G' => CGeneral(Num(a, arg++, "format"), prec ?? 6, conv, flags.Contains('#')),
                's' => prec is int p ? Str(a, arg++, "format")[..Math.Min(p, Str(a, arg - 1, "format").Length)] : Str(a, arg++, "format"),
                'q' => "\"" + Str(a, arg++, "format").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\\n") + "\"",
                _ => throw new LuaException($"invalid option `%{conv}' in `format'"),
            };
            if (plus && conv is 'd' or 'i' or 'f' or 'e' or 'E' or 'g' or 'G' && !body.StartsWith('-')) body = "+" + body;
            if (body.Length < width)
                body = left ? body.PadRight(width) : zero && conv is not 's' and not 'c' and not 'q'
                    ? (body.StartsWith('-') || body.StartsWith('+') ? body[0] + body[1..].PadLeft(width - 1, '0') : body.PadLeft(width, '0'))
                    : body.PadLeft(width);
            sb.Append(body);
        }
        return sb.ToString();
    }

    // auxsort do lbaselib 4.0 (quicksort com mediana de três), mesma sequência de comparações e trocas do original.
    static void AuxSort(object?[] a, int l, int u, Func<object?, object?, bool> lt)
    {
        void Swap(int x, int y) => (a[x], a[y]) = (a[y], a[x]);
        while (l < u)
        {
            if (lt(a[u], a[l])) Swap(l, u);
            if (u - l == 1) break;
            int i = (l + u) / 2;
            if (lt(a[i], a[l])) Swap(i, l);
            else if (lt(a[u], a[i])) Swap(i, u);
            if (u - l == 2) break;
            var p = a[i];
            Swap(i, u - 1);
            i = l;
            int j = u - 1;
            while (true)
            {
                while (lt(a[++i], p)) if (i > u) throw new LuaException("invalid order function for sorting");
                while (lt(p, a[--j])) if (j < l) throw new LuaException("invalid order function for sorting");
                if (j < i) break;
                Swap(i, j);
            }
            Swap(u - 1, i);
            if (i - l < u - i) { j = l; i--; l = i + 2; }
            else { j = i + 1; i = u; u = j - 2; }
            AuxSort(a, j, i, lt);
        }
    }

    // %g do printf: precisão P (0 vira 1); expoente X; se P > X >= -4 usa %f com P-1-X casas, senão %e com P-1; tira zeros.
    static string CGeneral(double d, int prec, char conv, bool alt)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsInfinity(d)) return d > 0 ? "inf" : "-inf";
        int p = prec == 0 ? 1 : prec;
        string e = CExp(d, p - 1, 'e');
        int x = int.Parse(e[(e.IndexOf('e') + 1)..], CultureInfo.InvariantCulture);
        string r = p > x && x >= -4 ? d.ToString("F" + (p - 1 - x), CultureInfo.InvariantCulture) : e;
        if (!alt)
        {
            int ei = r.IndexOf('e');
            string mant = ei < 0 ? r : r[..ei], tail = ei < 0 ? "" : r[ei..];
            if (mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
            r = mant + tail;
        }
        return conv == 'G' ? r.ToUpperInvariant() : r;
    }

    static string CExp(double d, int prec, char conv)
    {
        string r = d.ToString((conv == 'e' ? "e" : "E") + prec, CultureInfo.InvariantCulture); // ex.: 1.5e+003
        int e = r.IndexOfAny(['e', 'E']);
        string exp = r[(e + 2)..].TrimStart('0').PadLeft(2, '0');
        return r[..(e + 2)] + exp;
    }
}
