namespace DW4.Script.Be;

/// <summary>
/// Estado comum de jogo que os scripts leem e gravam (GETCOMMON "GAMERAM"). FATO (tabela de campos em 0x804EA930):
/// FLAG = 1024 bytes, COUNTER = 16 inteiros, mais NINZU, DIFFICULTY, CAMERAMODE e LIGHTMODE. No grupo, FLAG, COUNTER e
/// DIFFICULTY são compartilhados (docs/gameplay/fase4-levantamento-grupo.md).
/// </summary>
public sealed class GameRam
{
    public byte[] Flags { get; } = new byte[0x400];
    public int[] Counters { get; } = new int[16];
    public int Ninzu { get; set; } = 1;
    public int Difficulty { get; set; }
    public int CameraMode { get; set; }
    public int LightMode { get; set; }

    /// <summary>FLGisOn: FATO 0x802ED5C4 — o byte da flag como número 0/1; FLGisOn(1000) é especial: NINZU ≠ 0.</summary>
    public int FlagOn(int n) => n == 1000 ? (Ninzu != 0 ? 1 : 0) : n >= 0 && n < Flags.Length && Flags[n] != 0 ? 1 : 0;

    public void SetFlag(int n, bool on)
    {
        if (n >= 0 && n < Flags.Length) Flags[n] = on ? (byte)1 : (byte)0;
    }

    public int Counter(int n) => n >= 0 && n < Counters.Length ? Counters[n] : 0;

    public void SetCounter(int n, int v)
    {
        if (n >= 0 && n < Counters.Length) Counters[n] = v;
    }
}
