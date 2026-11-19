namespace DW4.Script;

/// <summary>
/// Máquina virtual do Lua 4.0 para os scripts originais do jogo (decisão do responsável em 2026-10-25: executar os
/// originais em vez de traduzir). Escrita a partir de lvm.c / ldo.c / lopcodes.h da tag v4.0 (github.com/lua/lua, MIT).
/// Isolada: não há io, os, dofile de disco nem rede; o script só enxerga as globais que o hospedeiro registrar (a API be:*
/// do motor e a biblioteca padrão mínima). Pilha única como no original: o CALL deixa os resultados no lugar da função.
/// </summary>
public sealed class LuaVM
{
    // lopcodes.h 4.0
    const int OpEnd = 0, OpReturn = 1, OpCall = 2, OpTailCall = 3, OpPushNil = 4, OpPop = 5, OpPushInt = 6, OpPushString = 7,
        OpPushNum = 8, OpPushNegNum = 9, OpPushUpvalue = 10, OpGetLocal = 11, OpGetGlobal = 12, OpGetTable = 13, OpGetDotted = 14,
        OpGetIndexed = 15, OpPushSelf = 16, OpCreateTable = 17, OpSetLocal = 18, OpSetGlobal = 19, OpSetTable = 20, OpSetList = 21,
        OpSetMap = 22, OpAdd = 23, OpAddI = 24, OpSub = 25, OpMult = 26, OpDiv = 27, OpPow = 28, OpConcat = 29, OpMinus = 30,
        OpNot = 31, OpJmpNe = 32, OpJmpEq = 33, OpJmpLt = 34, OpJmpLe = 35, OpJmpGt = 36, OpJmpGe = 37, OpJmpT = 38, OpJmpF = 39,
        OpJmpOnT = 40, OpJmpOnF = 41, OpJmp = 42, OpPushNilJmp = 43, OpForPrep = 44, OpForLoop = 45, OpLForPrep = 46,
        OpLForLoop = 47, OpClosure = 48;
    const int SizeOp = 6, SizeB = 9, PosA = SizeOp + SizeB, MaxArgS = ((1 << (32 - SizeOp)) - 1) >> 1;
    const int MultRet = 255, FieldsPerFlush = 64, MaxDepth = 200;

    public static int Op(uint i) => (int)(i & 0x3F);
    public static int ArgU(uint i) => (int)(i >> SizeOp);
    public static int ArgS(uint i) => ArgU(i) - MaxArgS;
    public static int ArgA(uint i) => (int)(i >> PosA);
    public static int ArgB(uint i) => (int)((i >> SizeOp) & ((1 << SizeB) - 1));

    object?[] _stack = new object?[1024];
    int _top, _depth;

    public LuaTable Globals { get; } = new();

    /// <summary>Saída do print do script (o hospedeiro decide; nada vai para o console por padrão).</summary>
    public Action<string>? Print { get; set; }

    /// <summary>Limite de instruções por chamada de fora (proteção contra laço infinito); 0 = sem limite.</summary>
    public long InstructionLimit { get; set; } = 50_000_000;
    long _budget;

    /// <summary>Instruções executadas desde a criação (para medir).</summary>
    public long Executed { get; private set; }

    public LuaVM() => LuaStdLib.Register(this);

    public void Register(string name, Func<object?[], object?[]> body) => Globals[name] = new LuaHostFunction(name, body);

    /// <summary>Carrega um chunk e devolve a função principal (sem executar).</summary>
    public static LuaClosure LoadChunk(byte[] chunk) => new(LuaProto.Load(chunk), []);

    /// <summary>Executa a função principal de um chunk.</summary>
    public object?[] DoChunk(byte[] chunk) => Call(LoadChunk(chunk));

    /// <summary>Chama uma função Lua ou do hospedeiro com os argumentos e devolve todos os resultados.</summary>
    public object?[] Call(object? f, params object?[] args)
    {
        bool outer = _depth == 0;
        if (outer) _budget = InstructionLimit;
        int func = _top;
        Ensure(func + args.Length + 1);
        _stack[func] = f;
        for (int i = 0; i < args.Length; i++) _stack[func + 1 + i] = args[i];
        _top = func + 1 + args.Length;
        try
        {
            CallAt(func, MultRet);
            var res = new object?[_top - func];
            Array.Copy(_stack, func, res, 0, res.Length);
            return res;
        }
        finally
        {
            Array.Clear(_stack, func, Math.Max(0, Math.Min(_stack.Length, _top) - func));
            _top = func;
        }
    }

    /// <summary>Chama a global <paramref name="name"/> (ex.: uma função definida pelo script).</summary>
    public object?[] CallGlobal(string name, params object?[] args) =>
        Call(Globals.RawGet(name) ?? throw new LuaException($"global '{name}' não existe"), args);

    void Ensure(int size)
    {
        if (size > _stack.Length) Array.Resize(ref _stack, Math.Max(size + 256, _stack.Length * 2));
    }

    // luaD_call: chama a função em _stack[func] com os argumentos até _top; os resultados vão para func..; nResults 255 = todos.
    void CallAt(int func, int nResults)
    {
        if (++_depth > MaxDepth) { _depth--; throw new LuaException("stack overflow (chamadas aninhadas demais)"); }
        try
        {
            int first;
            switch (_stack[func])
            {
                case LuaClosure c:
                    first = Execute(c, func + 1);
                    break;
                case LuaHostFunction h:
                {
                    var args = new object?[_top - func - 1];
                    Array.Copy(_stack, func + 1, args, 0, args.Length);
                    var res = h.Body(args) ?? [];
                    first = func + 1;
                    Ensure(first + res.Length + 1);
                    Array.Copy(res, 0, _stack, first, res.Length);
                    _top = first + res.Length;
                    break;
                }
                default:
                    throw new LuaException($"attempt to call a {Lua.TypeName(_stack[func])} value");
            }
            int dst = func;
            if (nResults == MultRet)
                while (first < _top) _stack[dst++] = _stack[first++];
            else
            {
                for (; nResults > 0 && first < _top; nResults--) _stack[dst++] = _stack[first++];
                for (; nResults > 0; nResults--) _stack[dst++] = null;
            }
            for (int i = dst; i < _top; i++) _stack[i] = null;
            _top = dst;
        }
        finally { _depth--; }
    }

    object? GetTable(object? t, object? k)
    {
        if (t is LuaTable table) return table.Get(k);
        throw new LuaException($"attempt to index a {Lua.TypeName(t)} value{(k is string s ? $" (campo '{s}')" : "")}");
    }

    static void SetTable(object? t, object? k, object? v)
    {
        if (t is LuaTable table) { table.Set(k, v); return; }
        throw new LuaException($"attempt to index a {Lua.TypeName(t)} value{(k is string s ? $" (campo '{s}')" : "")}");
    }

    static double Arith(int op, object? a, object? b)
    {
        if (!Lua.TryNumber(a, out double x) || !Lua.TryNumber(b, out double y))
            throw new LuaException($"attempt to perform arithmetic on a {Lua.TypeName(Lua.TryNumber(a, out _) ? b : a)} value");
        return op switch
        {
            OpAdd => x + y,
            OpSub => x - y,
            OpMult => x * y,
            OpDiv => x / y,
            _ => Math.Pow(x, y), // 4.0: POW só por tag method; a mathlib registra pow, que é isto
        };
    }

    // luaV_execute: devolve o índice do primeiro resultado; os resultados vão até _top.
    int Execute(LuaClosure cl, int bas)
    {
        var p = cl.Proto;
        var code = p.Code;
        var ks = p.Strings;
        var kn = p.Numbers;
        // Ajuste dos parâmetros (luaD_adjusttop) e vararg (adjust_varargs → tabela arg com n).
        int nargs = _top - bas;
        if (p.IsVararg)
        {
            var arg = new LuaTable();
            int extra = Math.Max(0, nargs - p.NumParams);
            for (int i = 0; i < extra; i++) arg.Set((double)(i + 1), _stack[bas + p.NumParams + i]);
            arg.Set("n", (double)extra);
            Ensure(bas + p.NumParams + p.MaxStackSize + 2);
            for (int i = nargs; i < p.NumParams; i++) _stack[bas + i] = null;
            for (int i = p.NumParams; i < nargs; i++) _stack[bas + i] = null;
            _stack[bas + p.NumParams] = arg;
            _top = bas + p.NumParams + 1;
        }
        else
        {
            Ensure(bas + Math.Max(nargs, p.NumParams) + p.MaxStackSize + 2);
            for (int i = nargs; i < p.NumParams; i++) _stack[bas + i] = null;
            for (int i = p.NumParams; i < nargs; i++) _stack[bas + i] = null;
            _top = bas + p.NumParams;
        }
        int top = _top;
        int pc = 0;
        while (true)
        {
            if (InstructionLimit > 0 && --_budget < 0) throw new LuaException("limite de instruções excedido (laço infinito?)");
            Executed++;
            uint i = code[pc++];
            switch (Op(i))
            {
                case OpEnd:
                    _top = top;
                    return top;
                case OpReturn:
                    _top = top;
                    return bas + ArgU(i);
                case OpCall:
                    _top = top;
                    CallAt(bas + ArgA(i), ArgB(i));
                    top = _top;
                    Ensure(top + p.MaxStackSize + 2);
                    break;
                case OpTailCall:
                    _top = top;
                    CallAt(bas + ArgA(i), MultRet);
                    return bas + ArgB(i);
                case OpPushNil:
                    for (int n = ArgU(i); n > 0; n--) _stack[top++] = null;
                    break;
                case OpPop:
                    for (int n = ArgU(i); n > 0; n--) _stack[--top] = null;
                    break;
                case OpPushInt:
                    _stack[top++] = (double)ArgS(i);
                    break;
                case OpPushString:
                    _stack[top++] = ks[ArgU(i)];
                    break;
                case OpPushNum:
                    _stack[top++] = kn[ArgU(i)];
                    break;
                case OpPushNegNum:
                    _stack[top++] = -kn[ArgU(i)];
                    break;
                case OpPushUpvalue:
                    _stack[top++] = cl.Upvalues[ArgU(i)];
                    break;
                case OpGetLocal:
                    _stack[top++] = _stack[bas + ArgU(i)];
                    break;
                case OpGetGlobal:
                    _top = top;
                    _stack[top++] = Globals.Get(ks[ArgU(i)]);
                    break;
                case OpGetTable:
                    _top = top;
                    top--;
                    _stack[top - 1] = GetTable(_stack[top - 1], _stack[top]);
                    _stack[top] = null;
                    break;
                case OpGetDotted:
                    _top = top;
                    _stack[top - 1] = GetTable(_stack[top - 1], ks[ArgU(i)]);
                    break;
                case OpGetIndexed:
                    _top = top;
                    _stack[top - 1] = GetTable(_stack[top - 1], _stack[bas + ArgU(i)]);
                    break;
                case OpPushSelf:
                {
                    _top = top; // o "Missing" do hospedeiro pode chamar a VM
                    var recv = _stack[top - 1];
                    _stack[top] = recv;
                    _stack[top - 1] = GetTable(recv, ks[ArgU(i)]);
                    top++;
                    break;
                }
                case OpCreateTable:
                    _stack[top++] = new LuaTable(ArgU(i));
                    break;
                case OpSetLocal:
                    _stack[bas + ArgU(i)] = _stack[--top];
                    _stack[top] = null;
                    break;
                case OpSetGlobal:
                    Globals.Set(ks[ArgU(i)], _stack[--top]);
                    _stack[top] = null;
                    break;
                case OpSetTable:
                {
                    int t = top - ArgA(i);
                    SetTable(_stack[t], _stack[t + 1], _stack[top - 1]);
                    for (int n = ArgB(i); n > 0; n--) _stack[--top] = null;
                    break;
                }
                case OpSetList:
                {
                    int aux = ArgA(i) * FieldsPerFlush;
                    int n = ArgB(i);
                    var arr = (LuaTable)_stack[top - n - 1]!;
                    for (; n > 0; n--) { arr.Set((double)(n + aux), _stack[--top]); _stack[top] = null; }
                    break;
                }
                case OpSetMap:
                {
                    int n = ArgU(i);
                    int final = top - 2 * n;
                    var arr = (LuaTable)_stack[final - 1]!;
                    for (; n > 0; n--)
                    {
                        top -= 2;
                        arr.Set(_stack[top], _stack[top + 1]);
                        _stack[top] = _stack[top + 1] = null;
                    }
                    top = final;
                    break;
                }
                case OpAdd or OpSub or OpMult or OpDiv or OpPow:
                    top--;
                    _stack[top - 1] = _stack[top - 1] is double x1 && _stack[top] is double y1 && Op(i) != OpPow
                        ? Op(i) switch { OpAdd => x1 + y1, OpSub => x1 - y1, OpMult => x1 * y1, _ => x1 / y1 }
                        : Arith(Op(i), _stack[top - 1], _stack[top]);
                    _stack[top] = null;
                    break;
                case OpAddI:
                    _stack[top - 1] = _stack[top - 1] is double xi ? xi + ArgS(i) : Arith(OpAdd, _stack[top - 1], (double)ArgS(i));
                    break;
                case OpConcat:
                {
                    int n = ArgU(i);
                    var sb = new System.Text.StringBuilder();
                    for (int k = top - n; k < top; k++)
                        sb.Append(Lua.ToStr(_stack[k]) ?? throw new LuaException($"attempt to concat a {Lua.TypeName(_stack[k])} value"));
                    for (int k = top - n + 1; k < top; k++) _stack[k] = null;
                    top = top - n + 1;
                    _stack[top - 1] = sb.ToString();
                    break;
                }
                case OpMinus:
                    _stack[top - 1] = Lua.TryNumber(_stack[top - 1], out double m)
                        ? -m : throw new LuaException($"attempt to perform arithmetic on a {Lua.TypeName(_stack[top - 1])} value");
                    break;
                case OpNot:
                    _stack[top - 1] = _stack[top - 1] == null ? Lua.True : null;
                    break;
                case OpJmpNe or OpJmpEq or OpJmpLt or OpJmpLe or OpJmpGt or OpJmpGe:
                {
                    top -= 2;
                    object? a = _stack[top], b = _stack[top + 1];
                    _stack[top] = _stack[top + 1] = null;
                    bool jump = Op(i) switch
                    {
                        OpJmpNe => !Lua.RawEquals(a, b),
                        OpJmpEq => Lua.RawEquals(a, b),
                        OpJmpLt => Lua.LessThan(a, b),
                        OpJmpLe => !Lua.LessThan(b, a),
                        OpJmpGt => Lua.LessThan(b, a),
                        _ => !Lua.LessThan(a, b),
                    };
                    if (jump) pc += ArgS(i);
                    break;
                }
                case OpJmpT:
                    if (_stack[--top] != null) pc += ArgS(i);
                    _stack[top] = null;
                    break;
                case OpJmpF:
                    if (_stack[--top] == null) pc += ArgS(i);
                    _stack[top] = null;
                    break;
                case OpJmpOnT:
                    if (_stack[top - 1] == null) top--;
                    else pc += ArgS(i);
                    break;
                case OpJmpOnF:
                    if (_stack[top - 1] != null) _stack[--top] = null;
                    else pc += ArgS(i);
                    break;
                case OpJmp:
                    pc += ArgS(i);
                    break;
                case OpPushNilJmp:
                    _stack[top++] = null;
                    pc++;
                    break;
                case OpForPrep:
                {
                    if (!Lua.TryNumber(_stack[top - 1], out double step)) throw new LuaException("`for' step must be a number");
                    if (!Lua.TryNumber(_stack[top - 2], out double limit)) throw new LuaException("`for' limit must be a number");
                    if (!Lua.TryNumber(_stack[top - 3], out double init)) throw new LuaException("`for' initial value must be a number");
                    _stack[top - 1] = step;
                    _stack[top - 2] = limit;
                    _stack[top - 3] = init;
                    if (step > 0 ? init > limit : init < limit)
                    {
                        for (int k = 0; k < 3; k++) _stack[--top] = null;
                        pc += ArgS(i);
                    }
                    break;
                }
                case OpForLoop:
                {
                    if (_stack[top - 3] is not double idx) throw new LuaException("`for' index must be a number");
                    double step = (double)_stack[top - 1]!, limit = (double)_stack[top - 2]!;
                    idx += step;
                    _stack[top - 3] = idx;
                    if (step > 0 ? idx > limit : idx < limit)
                        for (int k = 0; k < 3; k++) _stack[--top] = null;
                    else pc += ArgS(i);
                    break;
                }
                case OpLForPrep:
                {
                    if (_stack[top - 1] is not LuaTable t) throw new LuaException("`for' table must be a table");
                    if (!t.Next(null, out var k, out var v)) { _stack[--top] = null; pc += ArgS(i); }
                    else { _stack[top++] = k; _stack[top++] = v; }
                    break;
                }
                case OpLForLoop:
                {
                    var t = (LuaTable)_stack[top - 3]!;
                    if (!t.Next(_stack[top - 2], out var k, out var v))
                        for (int n = 0; n < 3; n++) _stack[--top] = null;
                    else { _stack[top - 2] = k; _stack[top - 1] = v; pc += ArgS(i); }
                    break;
                }
                case OpClosure:
                {
                    int nup = ArgB(i);
                    var ups = new object?[nup];
                    Array.Copy(_stack, top - nup, ups, 0, nup);
                    for (int k = 0; k < nup; k++) _stack[--top] = null;
                    _stack[top++] = new LuaClosure(p.Protos[ArgA(i)], ups);
                    break;
                }
                default:
                    throw new LuaException($"opcode {Op(i)} inválido em {p.Source}:{pc}");
            }
        }
    }
}
