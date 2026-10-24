using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace DW4.Formats.Lua;

/// <summary>Uma função (protótipo) de um chunk Lua 4.0.</summary>
public sealed class Lua40Function
{
    public string? Source { get; init; }
    public int LineDefined { get; init; }
    public int NumParams { get; init; }
    public bool IsVararg { get; init; }
    public int MaxStackSize { get; init; }
    public List<(string? Name, int StartPc, int EndPc)> Locals { get; } = [];
    public List<int> LineInfo { get; } = [];
    public List<string?> Strings { get; } = [];
    public List<double> Numbers { get; } = [];
    public List<Lua40Function> Functions { get; } = [];
    public List<uint> Code { get; } = [];
}

/// <summary>
/// Leitor de chunks binários do Lua 4.0, escrito a partir de lundump.c / lopcodes.h / llimits.h da
/// tag v4.0 do repositório oficial (github.com/lua/lua, licença MIT). Só lê; não executa nada.
/// Cabeçalho: ESC "Lua" 0x40, endianness, sizeof(int), sizeof(size_t), sizeof(Instruction),
/// SIZE_INSTRUCTION, SIZE_OP, SIZE_B, sizeof(Number), número de teste 3.14159265358979323846E8.
/// </summary>
public static class Lua40Chunk
{
    public static readonly string[] OpNames =
    [
        "END", "RETURN", "CALL", "TAILCALL", "PUSHNIL", "POP", "PUSHINT", "PUSHSTRING", "PUSHNUM", "PUSHNEGNUM",
        "PUSHUPVALUE", "GETLOCAL", "GETGLOBAL", "GETTABLE", "GETDOTTED", "GETINDEXED", "PUSHSELF", "CREATETABLE",
        "SETLOCAL", "SETGLOBAL", "SETTABLE", "SETLIST", "SETMAP", "ADD", "ADDI", "SUB", "MULT", "DIV", "POW",
        "CONCAT", "MINUS", "NOT", "JMPNE", "JMPEQ", "JMPLT", "JMPLE", "JMPGT", "JMPGE", "JMPT", "JMPF", "JMPONT",
        "JMPONF", "JMP", "PUSHNILJMP", "FORPREP", "FORLOOP", "LFORPREP", "LFORLOOP", "CLOSURE",
    ];

    const int SizeOp = 6, SizeB = 9, PosA = SizeOp + SizeB;
    const int MaxArgS = ((1 << (32 - SizeOp)) - 1) >> 1;

    public static bool LooksLikeChunk(ReadOnlySpan<byte> d) =>
        d.Length >= 5 && d[0] == 0x1B && d[1] == (byte)'L' && d[2] == (byte)'u' && d[3] == (byte)'a' && d[4] == 0x40;

    public static Lua40Function Read(byte[] data)
    {
        var r = new Reader(data);
        if (!LooksLikeChunk(data)) throw new InvalidDataException("não é chunk Lua 4.0");
        r.Pos = 5;
        r.LittleEndian = r.Byte() == 1;
        int[] expected = [4, 4, 4, 32, SizeOp, SizeB, 8];
        string[] what = ["sizeof(int)", "sizeof(size_t)", "sizeof(Instruction)", "SIZE_INSTRUCTION", "SIZE_OP", "SIZE_B", "sizeof(Number)"];
        for (int i = 0; i < expected.Length; i++)
        {
            int got = r.Byte();
            if (got != expected[i]) throw new InvalidDataException($"{what[i]} = {got}, esperado {expected[i]}");
        }
        double test = r.Number();
        if ((long)test != (long)3.14159265358979323846E8) throw new InvalidDataException($"número de teste {test}");
        var f = LoadFunction(r);
        if (r.Pos != data.Length) throw new InvalidDataException($"sobram {data.Length - r.Pos} bytes depois do chunk");
        return f;
    }

    static Lua40Function LoadFunction(Reader r)
    {
        var f = new Lua40Function
        {
            Source = r.String(),
            LineDefined = r.Int(),
            NumParams = r.Int(),
            IsVararg = r.Byte() != 0,
            MaxStackSize = r.Int(),
        };
        int n = r.Count();
        for (int i = 0; i < n; i++) f.Locals.Add((r.String(), r.Int(), r.Int()));
        n = r.Count();
        for (int i = 0; i < n; i++) f.LineInfo.Add(r.Int());
        n = r.Count();
        for (int i = 0; i < n; i++) f.Strings.Add(r.String());
        n = r.Count();
        for (int i = 0; i < n; i++) f.Numbers.Add(r.Number());
        n = r.Count();
        for (int i = 0; i < n; i++) f.Functions.Add(LoadFunction(r));
        n = r.Count();
        for (int i = 0; i < n; i++) f.Code.Add((uint)r.Int());
        if (f.Code.Count == 0 || (f.Code[^1] & 0x3F) != 0) throw new InvalidDataException("código não termina em END");
        return f;
    }

    public static int Op(uint i) => (int)(i & 0x3F);
    public static int ArgU(uint i) => (int)(i >> SizeOp);
    public static int ArgS(uint i) => ArgU(i) - MaxArgS;
    public static int ArgA(uint i) => (int)(i >> PosA);
    public static int ArgB(uint i) => (int)((i >> SizeOp) & ((1 << SizeB) - 1));

    /// <summary>Listagem no estilo de "luac -l".</summary>
    public static void List(Lua40Function f, TextWriter w, string path = "main")
    {
        w.WriteLine($"function {path} <{f.Source}:{f.LineDefined}> ({f.Code.Count} instruções, {f.NumParams}{(f.IsVararg ? "+" : "")} parâmetros, pilha {f.MaxStackSize}, {f.Locals.Count} locais, {f.Strings.Count} strings, {f.Numbers.Count} números, {f.Functions.Count} funções)");
        for (int pc = 0; pc < f.Code.Count; pc++)
        {
            uint ins = f.Code[pc];
            int op = Op(ins);
            string name = op < OpNames.Length ? OpNames[op] : $"OP{op}";
            string args = op switch
            {
                2 or 3 or 20 or 21 or 48 => $"{ArgA(ins)} {ArgB(ins)}",
                6 or 24 => ArgS(ins).ToString(CultureInfo.InvariantCulture),
                >= 32 and <= 42 or >= 44 and <= 47 => $"{ArgS(ins)}\t; to {pc + 1 + ArgS(ins)}",
                0 or 13 or 23 or 25 or 26 or 27 or 28 or 30 or 31 or 43 => "",
                _ => ArgU(ins).ToString(CultureInfo.InvariantCulture),
            };
            string note = op switch
            {
                7 or 12 or 14 or 16 or 19 when ArgU(ins) < f.Strings.Count => $"\t; \"{f.Strings[ArgU(ins)]}\"",
                8 or 9 when ArgU(ins) < f.Numbers.Count => $"\t; {f.Numbers[ArgU(ins)].ToString(CultureInfo.InvariantCulture)}",
                11 or 15 or 18 when ArgU(ins) < f.Locals.Count => $"\t; {f.Locals[ArgU(ins)].Name}",
                _ => "",
            };
            int line = pc < f.LineInfo.Count ? f.LineInfo[pc] : 0;
            w.WriteLine($"\t{pc + 1}\t[{line}]\t{name,-12}\t{args}{note}");
        }
        for (int i = 0; i < f.Functions.Count; i++) List(f.Functions[i], w, $"{path}.{i}");
    }

    /// <summary>Todas as funções do chunk, em profundidade.</summary>
    public static IEnumerable<Lua40Function> All(Lua40Function f)
    {
        yield return f;
        foreach (var c in f.Functions)
            foreach (var x in All(c)) yield return x;
    }

    sealed class Reader(byte[] data)
    {
        public int Pos;
        public bool LittleEndian = true;

        void Need(int n)
        {
            if (Pos + n > data.Length) throw new InvalidDataException($"fim inesperado em 0x{Pos:X}");
        }

        public int Byte() { Need(1); return data[Pos++]; }

        public int Int()
        {
            Need(4);
            var s = data.AsSpan(Pos, 4);
            Pos += 4;
            return LittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s);
        }

        public int Count()
        {
            int n = Int();
            if (n < 0 || n > data.Length) throw new InvalidDataException($"contagem inválida {n} em 0x{Pos - 4:X}");
            return n;
        }

        public double Number()
        {
            Need(8);
            var s = data.AsSpan(Pos, 8);
            Pos += 8;
            return LittleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(s) : BinaryPrimitives.ReadDoubleBigEndian(s);
        }

        public string? String()
        {
            int size = Count();
            if (size == 0) return null;
            Need(size);
            var s = Encoding.Latin1.GetString(data, Pos, size - 1); // tira o '\0' final
            Pos += size;
            return s;
        }
    }
}
