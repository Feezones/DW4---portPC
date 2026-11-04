using System.Buffers.Binary;
using System.Text;

namespace DW4.Formats.Dolphin;

/// <summary>Uma leitura do controle de GameCube gravada pelo Dolphin (8 bytes, ControllerState em Source/Core/Core/Movie.h).</summary>
public readonly record struct GcPadInput(ushort Buttons, byte TriggerL, byte TriggerR, byte StickX, byte StickY, byte CStickX, byte CStickY)
{
    // Bits do primeiro par de bytes, na ordem dos campos de bits do Dolphin (bit 0 = Start).
    public const ushort Start = 1 << 0, A = 1 << 1, B = 1 << 2, X = 1 << 3, Y = 1 << 4, Z = 1 << 5,
        Up = 1 << 6, Down = 1 << 7, Left = 1 << 8, Right = 1 << 9, L = 1 << 10, R = 1 << 11;

    public bool Has(ushort button) => (Buttons & button) != 0;

    /// <summary>Analógico de -1 a 1 (128 = centro).</summary>
    public (float X, float Y) Stick => ((StickX - 128) / 127f, (StickY - 128) / 127f);

    public static string Names(ushort b)
    {
        var parts = new List<string>();
        (ushort bit, string name)[] all = [(Start, "Start"), (A, "A"), (B, "B"), (X, "X"), (Y, "Y"), (Z, "Z"), (Up, "Cima"), (Down, "Baixo"), (Left, "Esq"), (Right, "Dir"), (L, "L"), (R, "R")];
        foreach (var (bit, name) in all) if ((b & bit) != 0) parts.Add(name);
        return string.Join("+", parts);
    }
}

/// <summary>
/// Gravação de entradas do Dolphin (.dtm). Cabeçalho de 256 bytes (DTMHeader em Source/Core/Core/Movie.h) seguido das
/// leituras do controle. Só o necessário para medir: jogo, quadros, leituras e se parte de um save state.
/// </summary>
public sealed class DtmFile
{
    public string GameId { get; init; } = "";
    public byte Controllers { get; init; }
    public bool FromSaveState { get; init; }
    public ulong FrameCount { get; init; }
    public ulong InputCount { get; init; }
    public ulong LagCount { get; init; }
    public List<GcPadInput> Inputs { get; } = [];

    public const int HeaderSize = 256;

    public static DtmFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || !data[..4].SequenceEqual("DTM\x1A"u8)) throw new InvalidDataException("não é um .dtm do Dolphin");
        var dtm = new DtmFile
        {
            GameId = Encoding.ASCII.GetString(data.Slice(4, 6)).TrimEnd('\0'),
            Controllers = data[11],
            FromSaveState = data[12] != 0,
            FrameCount = BinaryPrimitives.ReadUInt64LittleEndian(data[13..]),
            InputCount = BinaryPrimitives.ReadUInt64LittleEndian(data[21..]),
            LagCount = BinaryPrimitives.ReadUInt64LittleEndian(data[29..]),
        };
        if ((dtm.Controllers & 0x0F) != 1) throw new NotSupportedException($"só um controle de GC na porta 1 é suportado (controladores 0x{dtm.Controllers:X2})");
        for (int at = HeaderSize; at + 8 <= data.Length; at += 8)
            dtm.Inputs.Add(new GcPadInput(BinaryPrimitives.ReadUInt16LittleEndian(data[at..]), data[at + 2], data[at + 3],
                data[at + 4], data[at + 5], data[at + 6], data[at + 7]));
        return dtm;
    }
}
