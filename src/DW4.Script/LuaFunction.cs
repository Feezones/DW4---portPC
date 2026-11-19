namespace DW4.Script;

/// <summary>Função Lua: closure de script ou função do hospedeiro (a API do motor e a biblioteca padrão).</summary>
public abstract class LuaFunction
{
    static long _ids;
    public long Id { get; } = Interlocked.Increment(ref _ids);
    public abstract string Name { get; }
}

/// <summary>Closure do Lua 4.0: protótipo + upvalues copiados na criação (sintaxe %x).</summary>
public sealed class LuaClosure(LuaProto proto, object?[] upvalues) : LuaFunction
{
    public LuaProto Proto { get; } = proto;
    public object?[] Upvalues { get; } = upvalues;
    public override string Name => Proto.Source ?? "?";
}

/// <summary>Função do hospedeiro: recebe os argumentos (com o self no primeiro, em be:Metodo) e devolve os resultados.</summary>
public sealed class LuaHostFunction(string name, Func<object?[], object?[]> body) : LuaFunction
{
    public Func<object?[], object?[]> Body { get; } = body;
    public override string Name { get; } = name;
}

public sealed class LuaException(string message) : Exception(message);
