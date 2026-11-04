using System.Globalization;

namespace DW4.Script.Be;

/// <summary>
/// Argumento de comando do motor. FATO (analisador em 0x802ECA74): a string é separada por "/"; "letra&lt;a,b,c&gt;" é um
/// token tipado (V = vetor, I = inteiro); o resto vai como texto. Ex.: "V&lt;220,40,-200&gt;/V&lt;0,-90,0&gt;", "ALL/DISPON".
/// </summary>
public sealed record BeArg(char Kind, string Text, double[] Numbers)
{
    public bool IsText => Kind == '\0';

    public static IReadOnlyList<BeArg> Parse(string? s)
    {
        if (string.IsNullOrEmpty(s)) return [];
        var list = new List<BeArg>();
        foreach (var part in s.Split('/'))
        {
            if (part.Length >= 3 && char.IsAsciiLetterUpper(part[0]) && part[1] == '<' && part[^1] == '>')
            {
                var nums = part[2..^1].Split(',').Select(t => double.TryParse(t.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0).ToArray();
                list.Add(new BeArg(part[0], part, nums));
            }
            else list.Add(new BeArg('\0', part, []));
        }
        return list;
    }

    public override string ToString() => Text;
}

/// <summary>
/// Mensagem para um objeto do motor (beMessenger, 0x80305344): classe ("PLAYER", "MODELCTRL", "SOUND", "STAGECTL"…), nome
/// do objeto (pode ser nil), índice (−1 = todos/nenhum), comando ("GENERATE", "PLAY", "SQ_OPEN"…) e argumentos.
/// </summary>
public sealed record BeMessage(string Class, string? Name, int Index, string Command, IReadOnlyList<BeArg> Args, string? RawArgs)
{
    public BeArg? Arg(int i) => i < Args.Count ? Args[i] : null;
    public string? Text(int i) => Arg(i)?.Text;

    public override string ToString() =>
        $"{Class} {Name ?? "-"} {Index} {Command}{(RawArgs is { Length: > 0 } a ? " \"" + a + "\"" : "")}";
}

/// <summary>
/// beMessenger com a pilha de atraso. FATO (0x803061BC, 0x80306210..54): o número final do be:sd é um atraso em quadros
/// de 60 Hz (n × 16.666.666 ns somado ao relógio); a mensagem é entregue n quadros depois. Sem número: na hora.
/// </summary>
public sealed class BeMessenger(Action<BeMessage> deliver)
{
    readonly List<(long Due, long Order, BeMessage Message)> _delayed = [];
    long _order;

    public long Frame { get; private set; }

    public void Send(BeMessage m, long delayFrames = 0)
    {
        if (delayFrames <= 0) deliver(m);
        else _delayed.Add((Frame + delayFrames, _order++, m));
    }

    public int Pending => _delayed.Count;

    /// <summary>Avança um quadro de 60 Hz e entrega o que venceu, na ordem de vencimento e de envio.</summary>
    public void Tick()
    {
        Frame++;
        if (_delayed.Count == 0) return;
        var due = _delayed.Where(d => d.Due <= Frame).OrderBy(d => d.Due).ThenBy(d => d.Order).ToList();
        foreach (var d in due) _delayed.Remove(d);
        foreach (var d in due) deliver(d.Message);
    }
}
