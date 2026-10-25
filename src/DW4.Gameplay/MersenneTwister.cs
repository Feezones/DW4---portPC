namespace DW4.Gameplay;

/// <summary>
/// Sorteio do combate: igMersenneTwisterRandomNumber do Alchemy, MT19937 (FATO, main.dol 0x8021B338: N = 624, M = 397,
/// mag01 {0, 0x9908B0DF}, têmpera 11/7/15/18). A semeadura é a do mt19937.c original de 1998 (sgenrand, 69069·s + 1 em
/// duas metades de 16 bits, 0x8021B2A0) e o construtor usa 5150 (0x8021B2F4). HIPÓTESE: o objeto do combate é dessa classe
/// e não é ressemeado. Determinístico e sem estado global, para rodar igual num servidor.
/// </summary>
public sealed class MersenneTwister : IGameRandom
{
    const int N = 624, M = 397;
    const uint MatrixA = 0x9908B0DF, Upper = 0x80000000, Lower = 0x7FFFFFFF;
    readonly uint[] _mt = new uint[N];
    int _i = N + 1;

    public const uint GameSeed = 5150;

    public MersenneTwister(uint seed = GameSeed) => SeedLegacy(seed);

    /// <summary>sgenrand (1998), a usada pelo jogo.</summary>
    public void SeedLegacy(uint seed)
    {
        for (int i = 0; i < N; i++)
        {
            _mt[i] = seed & 0xFFFF0000;
            seed = 69069 * seed + 1;
            _mt[i] |= (seed & 0xFFFF0000) >> 16;
            seed = 69069 * seed + 1;
        }
        _i = N;
    }

    /// <summary>init_genrand (2002), só para conferir o núcleo contra os valores de referência publicados.</summary>
    public void SeedReference(uint seed)
    {
        _mt[0] = seed;
        for (int i = 1; i < N; i++) _mt[i] = 1812433253 * (_mt[i - 1] ^ (_mt[i - 1] >> 30)) + (uint)i;
        _i = N;
    }

    public uint Next()
    {
        if (_i >= N)
        {
            int k;
            for (k = 0; k < N - M; k++) Twist(k, k + 1, k + M);
            for (; k < N - 1; k++) Twist(k, k + 1, k + (M - N));
            Twist(N - 1, 0, M - 1);
            _i = 0;
        }
        uint y = _mt[_i++];
        y ^= y >> 11;
        y ^= (y << 7) & 0x9D2C5680;
        y ^= (y << 15) & 0xEFC60000;
        y ^= y >> 18;
        return y;
    }

    void Twist(int k, int next, int far)
    {
        uint y = (_mt[k] & Upper) | (_mt[next] & Lower);
        _mt[k] = _mt[far] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0);
    }
}
