namespace DW4.Script;

/// <summary>
/// Tabela do Lua 4.0, com a mesma organização de ltable.c (tag v4.0, MIT): só parte hash, tamanho potência de 2, posição
/// principal pelo hash da chave, encadeamento com "firstfree" descendo e rehash quando acaba o espaço. Reproduzir isso é o
/// que faz o next/foreach/for k,v percorrer as chaves na MESMA ordem do jogo. Números: hash = (long)n; strings: hash_s
/// de lstring.c (32 bits, como no GameCube). Tabelas e funções como chave usam um número de criação (no jogo era o
/// endereço de memória, que não dá para reproduzir).
/// </summary>
public sealed class LuaTable
{
    const int MinPower2 = 4;
    object?[] _keys = [];
    object?[] _vals = [];
    int[] _next = [];
    int _firstFree;
    static long _ids;

    public long Id { get; } = Interlocked.Increment(ref _ids);

    /// <summary>Chamada quando a chave não existe (como um tag method "index"); usada pelo objeto be do motor.</summary>
    public Func<LuaTable, object, object?>? Missing { get; set; }

    public LuaTable(int sizeHint = 0) => SetNodeVector(Power2(sizeHint));

    public int Size => _keys.Length;

    /// <summary>luaO_power2: a menor potência de 2 MAIOR que n, no mínimo 4.</summary>
    static int Power2(int n)
    {
        int p = MinPower2;
        while (p <= n) p <<= 1;
        return p;
    }

    void SetNodeVector(int size)
    {
        _keys = new object?[size];
        _vals = new object?[size];
        _next = new int[size];
        Array.Fill(_next, -1);
        _firstFree = size - 1;
    }

    public static uint StringHash(string s)
    {
        uint l = (uint)s.Length, h = l;
        uint step = (l >> 5) | 1;
        int i = 0;
        for (; l >= step; l -= step) h ^= (h << 5) + (h >> 2) + (byte)s[i++];
        return h;
    }

    int MainPosition(object key)
    {
        uint h = key switch
        {
            double d => unchecked((uint)(int)(long)d),
            string s => StringHash(s),
            LuaTable t => (uint)t.Id,
            LuaFunction f => (uint)f.Id,
            _ => (uint)key.GetHashCode(),
        };
        return (int)(h & (uint)(_keys.Length - 1));
    }

    static bool KeyEquals(object? a, object b) => a switch
    {
        double x => b is double y && x == y,
        string s => b is string t && string.Equals(s, t, StringComparison.Ordinal),
        null => false,
        _ => ReferenceEquals(a, b),
    };

    int Find(object key)
    {
        for (int n = MainPosition(key); n >= 0; n = _next[n])
            if (KeyEquals(_keys[n], key)) return n;
        return -1;
    }

    /// <summary>t[k] sem o "Missing".</summary>
    public object? RawGet(object? key)
    {
        if (key == null) return null;
        int n = Find(key);
        return n < 0 ? null : _vals[n];
    }

    public object? Get(object? key)
    {
        var v = RawGet(key);
        return v == null && key != null && Missing != null ? Missing(this, key) : v;
    }

    public object? this[object key]
    {
        get => Get(key);
        set => Set(key, value);
    }

    /// <summary>t[k] = v como luaH_set: a chave entra mesmo com valor nil (e ocupa o nó até o próximo rehash).</summary>
    public void Set(object? key, object? value)
    {
        if (key == null) throw new LuaException("table index is nil");
        if (key is double d && double.IsNaN(d)) throw new LuaException("table index is NaN");
        // O índice antes da escrita: Slot pode fazer rehash e trocar _vals (em C# a referência do vetor seria lida antes).
        int n = Slot(key);
        _vals[n] = value;
    }

    int Slot(object key)
    {
        while (true)
        {
            int mp = MainPosition(key);
            for (int n = mp; n >= 0; n = _next[n])
                if (KeyEquals(_keys[n], key)) return n;
            if (_keys[mp] != null)
            {
                int free = _firstFree;
                int other;
                if (mp > free && (other = MainPosition(_keys[mp]!)) != mp)
                {
                    // O nó que ocupa mp está fora da posição principal dele: vai para o espaço livre.
                    while (_next[other] != mp) other = _next[other];
                    _next[other] = free;
                    _keys[free] = _keys[mp];
                    _vals[free] = _vals[mp];
                    _next[free] = _next[mp];
                    _next[mp] = -1;
                    _vals[mp] = null;
                }
                else
                {
                    _next[free] = _next[mp];
                    _next[mp] = free;
                    mp = free;
                }
            }
            _keys[mp] = key;
            while (true)
            {
                if (_keys[_firstFree] == null) return mp;
                if (_firstFree == 0) break;
                _firstFree--;
            }
            Rehash();
        }
    }

    void Rehash()
    {
        var oldKeys = _keys;
        var oldVals = _vals;
        int oldSize = oldKeys.Length, used = 0;
        foreach (var v in oldVals) if (v != null) used++;
        if (used >= oldSize - oldSize / 4) SetNodeVector(oldSize * 2);
        else if (used <= oldSize / 4 && oldSize > MinPower2) SetNodeVector(oldSize / 2);
        else SetNodeVector(oldSize);
        for (int i = 0; i < oldSize; i++)
            if (oldVals[i] != null) { int n = Slot(oldKeys[i]!); _vals[n] = oldVals[i]; }
    }

    /// <summary>luaH_next: o próximo par depois de <paramref name="key"/> (nil = o primeiro), na ordem dos nós.</summary>
    public bool Next(object? key, out object? nextKey, out object? nextValue)
    {
        int i = key == null ? -1 : Find(key);
        if (key != null && i < 0) throw new LuaException("invalid key for `next'");
        for (i++; i < _keys.Length; i++)
            if (_vals[i] != null) { nextKey = _keys[i]; nextValue = _vals[i]; return true; }
        nextKey = nextValue = null;
        return false;
    }

    /// <summary>getn do Lua 4.0: o campo "n" se for número; senão a maior chave numérica com valor.</summary>
    public int GetN()
    {
        if (RawGet("n") is double n) return (int)n;
        double max = 0;
        for (int i = 0; i < _keys.Length; i++)
            if (_keys[i] is double k && _vals[i] != null && k > max) max = k;
        return (int)max;
    }

    public IEnumerable<(object Key, object Value)> Pairs()
    {
        for (int i = 0; i < _keys.Length; i++)
            if (_vals[i] != null) yield return (_keys[i]!, _vals[i]!);
    }
}
