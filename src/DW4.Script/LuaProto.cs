using System.Buffers.Binary;
using System.Text;

namespace DW4.Script;

/// <summary>Protótipo de função Lua 4.0 (o que o CLOSURE instancia).</summary>
public sealed class LuaProto
{
    public string? Source { get; init; }
    public int NumParams { get; init; }
    public bool IsVararg { get; init; }
    public int MaxStackSize { get; init; }
    public string?[] Strings { get; init; } = [];
    public double[] Numbers { get; init; } = [];
    public LuaProto[] Protos { get; init; } = [];
    public uint[] Code { get; init; } = [];

    /// <summary>
    /// Carrega um chunk binário do Lua 4.0 (formato de lundump.c da tag v4.0, MIT; o mesmo do leitor de ferramentas
    /// DW4.Formats/Lua/Lua40Chunk.cs). Só aceita o perfil dos scripts do jogo: little-endian, int/size_t/Instruction de
    /// 4 bytes, instrução de 32 bits (OP 6, B 9) e double de 8 bytes.
    /// </summary>
    public static LuaProto Load(byte[] data)
    {
        if (data.Length < 5 || data[0] != 0x1B || data[1] != 'L' || data[2] != 'u' || data[3] != 'a' || data[4] != 0x40)
            throw new InvalidDataException("não é chunk Lua 4.0");
        var r = new Reader(data) { Pos = 5 };
        if (r.Byte() != 1) throw new InvalidDataException("chunk big-endian não suportado");
        foreach (var (want, what) in new[] { (4, "sizeof(int)"), (4, "sizeof(size_t)"), (4, "sizeof(Instruction)"), (32, "SIZE_INSTRUCTION"), (6, "SIZE_OP"), (9, "SIZE_B"), (8, "sizeof(Number)") })
            if (r.Byte() != want) throw new InvalidDataException($"{what} diferente de {want}");
        r.Number(); // número de teste
        var f = Function(r);
        if (r.Pos != data.Length) throw new InvalidDataException("sobram bytes depois do chunk");
        return f;
    }

    static LuaProto Function(Reader r)
    {
        string? source = r.String();
        r.Int(); // linha
        int numParams = r.Int();
        bool vararg = r.Byte() != 0;
        int maxStack = r.Int();
        int n = r.Count();
        for (int i = 0; i < n; i++) { r.String(); r.Int(); r.Int(); } // locais (debug)
        n = r.Count();
        for (int i = 0; i < n; i++) r.Int(); // linhas (debug)
        var strings = new string?[r.Count()];
        for (int i = 0; i < strings.Length; i++) strings[i] = r.String();
        var numbers = new double[r.Count()];
        for (int i = 0; i < numbers.Length; i++) numbers[i] = r.Number();
        var protos = new LuaProto[r.Count()];
        for (int i = 0; i < protos.Length; i++) protos[i] = Function(r);
        var code = new uint[r.Count()];
        for (int i = 0; i < code.Length; i++) code[i] = (uint)r.Int();
        return new LuaProto { Source = source, NumParams = numParams, IsVararg = vararg, MaxStackSize = maxStack, Strings = strings, Numbers = numbers, Protos = protos, Code = code };
    }

    sealed class Reader(byte[] d)
    {
        public int Pos;
        void Need(int n) { if (Pos + n > d.Length) throw new InvalidDataException($"fim inesperado em 0x{Pos:X}"); }
        public int Byte() { Need(1); return d[Pos++]; }
        public int Int() { Need(4); int v = BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(Pos)); Pos += 4; return v; }
        public int Count() { int n = Int(); if (n < 0 || n > d.Length) throw new InvalidDataException($"contagem inválida {n}"); return n; }
        public double Number() { Need(8); double v = BinaryPrimitives.ReadDoubleLittleEndian(d.AsSpan(Pos)); Pos += 8; return v; }
        public string? String()
        {
            int size = Count();
            if (size == 0) return null;
            Need(size);
            var s = Encoding.Latin1.GetString(d, Pos, size - 1);
            Pos += size;
            return s;
        }
    }
}
