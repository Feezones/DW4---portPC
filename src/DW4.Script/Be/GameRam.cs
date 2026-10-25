using DW4.Gameplay;

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

    /// <summary>
    /// GATESTAGENAME / GATESTAGENO (SYSsetGateStage, 0x802EBCFC): para onde o portão da sala leva. FATO: o NO vale −1 de
    /// início (tabela de campos). HIPÓTESE: o nome começa vazio e o be devolve nil (o start.lua do lobby testa "not nome").
    /// </summary>
    public string? GateStageName { get; set; }
    public int GateStageNo { get; set; } = -1;

    /// <summary>Registro de cada jogador (o 0x802EB9C4 pega o PLAYERDATA p+1 da GAMERAM): LEVEL, BIT, formas, pastas.</summary>
    public BePlayer[] Players { get; } = Enumerable.Range(0, 4).Select(_ => new BePlayer()).ToArray();

    public int Counter(int n) => n >= 0 && n < Counters.Length ? Counters[n] : 0;

    public void SetCounter(int n, int v)
    {
        if (n >= 0 && n < Counters.Length) Counters[n] = v;
    }
}

/// <summary>
/// Registro de um jogador na GAMERAM que os SYS* dos scripts usam. FATO (tabela de campos em 0x804EA930): LEVEL e BIT
/// inteiros; CARDLIST com 50 bytes; BASE_ISUSE (16 bytes), BASE_LEVEL e BASE_EXP (16 inteiros) são as formas, que o port
/// guarda no <see cref="Evolution"/>; DEVICEFOLDER, DISKFOLDER e FLAGFOLDER ficam no <see cref="Inventory"/> (BIT também).
/// </summary>
public sealed class BePlayer
{
    public const int CardCount = 50;

    /// <summary>Nível da forma atual (LEVEL). O cliente liga ao nível do jogador.</summary>
    public Func<int> Level { get; set; } = () => 1;
    public Evolution Forms { get; set; } = new();
    public Inventory Items { get; set; } = new();
    /// <summary>CARDLIST: 1 = carta obtida (SYSctlCardFolder "SET"/"CLR"/"KAZU").</summary>
    public byte[] Cards { get; } = new byte[CardCount];
    /// <summary>HIPÓTESE: SYSctlDeviceFolder "ISEQUIP" pergunta se o item está equipado; o cliente responde.</summary>
    public Func<ItemRef, bool> IsEquipped { get; set; } = _ => false;
}
