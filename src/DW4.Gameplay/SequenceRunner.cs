using System.Numerics;
using System.Text.Json;

namespace DW4.Gameplay;

/// <summary>Comando de sequência lido do jogo (beModelCtrlHITBOX → "HITBOX", beModelCtrlACTION → "ACTION"…), valores em ordem de slot.</summary>
public sealed record SeqCommand(string Kind, IReadOnlyList<object> Values)
{
    public string S(int i) => i < Values.Count && Values[i] is string s ? s : "";
    public float F(int i) => i < Values.Count ? Values[i] switch { float f => f, int n => n, _ => 0 } : 0;
    public int I(int i) => i < Values.Count && Values[i] is int n ? n : 0;
    public Vector3 V(int i) => i < Values.Count && Values[i] is float[] { Length: 3 } a ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;

    /// <summary>Lê as sequências ("sequences") do JSON gravado por <c>dw4 igb-seq</c>.</summary>
    public static Dictionary<string, List<SeqCommand>> LoadAll(string json) => Load(json, "sequences");

    /// <summary>Lê as ações ("actions", beAction2Data) do JSON gravado por <c>dw4 igb-seq</c>.</summary>
    public static Dictionary<string, List<SeqCommand>> LoadActions(string json) => Load(json, "actions");

    static Dictionary<string, List<SeqCommand>> Load(string json, string section)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<string, List<SeqCommand>>();
        if (!doc.RootElement.TryGetProperty(section, out var root)) return result;
        foreach (var seq in root.EnumerateObject())
            result[seq.Name] = seq.Value.EnumerateArray().Select(c => new SeqCommand(c.GetProperty("k").GetString() ?? "",
                c.GetProperty("v").EnumerateArray().Select(Value).ToList())).ToList();
        return result;
    }

    static object Value(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => e.EnumerateArray().Select(x => x.GetSingle()).ToArray(),
        JsonValueKind.Number when e.TryGetInt32(out int n) && !e.GetRawText().Contains('.') => n,
        JsonValueKind.Number => e.GetSingle(),
        _ => "",
    };
}

/// <summary>
/// Animação de uma ACTION: o primeiro MOTIONSET da beAction2Data de mesmo nome (FATO: "VATCS1" → beAction2Data "VATCS1";
/// o nome da animação no MOTIONSET é o elo, já que "vatcS1" não existe e "vatcS" sim).
/// </summary>
public static class ActionAnimations
{
    public static string? Motion(IReadOnlyDictionary<string, List<SeqCommand>> actions, string action) =>
        actions.TryGetValue(action, out var cmds) ? cmds.FirstOrDefault(c => c.Kind == "MOTIONSET")?.S(0) : null;

    /// <summary>
    /// Primeira ACTION do bloco do estilo numa sequência (ex.: SQ_WAIT, bloco "slash" → "WAITS1"), ou da sequência
    /// inteira quando ela não tem LABEL. Serve para escolher as animações de parado e andando conforme a arma.
    /// </summary>
    public static string? FirstAction(IReadOnlyDictionary<string, List<SeqCommand>> sequences, string sequence, string? style) =>
        BlockActions(sequences, sequence, style).FirstOrDefault();

    /// <summary>Todas as ACTION do bloco do estilo, em ordem.</summary>
    public static IEnumerable<string> BlockActions(IReadOnlyDictionary<string, List<SeqCommand>> sequences, string sequence, string? style)
    {
        if (!sequences.TryGetValue(sequence, out var cmds)) yield break;
        int start = 0;
        if (style != null && cmds.Any(c => c.Kind == "LABEL"))
        {
            start = cmds.FindIndex(c => c.Kind == "LABEL" && c.S(0) == style) + 1;
            if (start == 0) yield break;
        }
        for (int i = start; i < cmds.Count && cmds[i].Kind != "LABEL"; i++)
            if (cmds[i].Kind == "ACTION") yield return cmds[i].S(0);
    }

    /// <summary>Quadros de mistura de entrada (MOTIONSET Float@4: 3, 6, 8…; HIPÓTESE).</summary>
    public static float Blend(IReadOnlyDictionary<string, List<SeqCommand>> actions, string action) =>
        actions.TryGetValue(action, out var cmds) && cmds.FirstOrDefault(c => c.Kind == "MOTIONSET") is { } m ? m.F(2) : 0;

    /// <summary>A animação repete ("LP") em vez de tocar uma vez ("OS").</summary>
    public static bool Loops(IReadOnlyDictionary<string, List<SeqCommand>> actions, string action) =>
        actions.TryGetValue(action, out var cmds) && cmds.FirstOrDefault(c => c.Kind == "MOTIONSET")?.S(3) == "LP";

    /// <summary>
    /// Duração da ação em quadros. HIPÓTESE: WAITCHECK "Frame" N espera N quadros; "Motion" (ou nenhum) espera o fim da
    /// animação, cujo tamanho vem de <paramref name="motionFrames"/>.
    /// </summary>
    public static float? Length(IReadOnlyDictionary<string, List<SeqCommand>> actions, string action, Func<string, float?> motionFrames)
    {
        if (!actions.TryGetValue(action, out var cmds)) return null;
        if (cmds.FirstOrDefault(c => c.Kind == "WAITCHECK" && c.S(0) == "Frame") is { } w) return w.F(1);
        return Motion(actions, action) is { } m ? motionFrames(m) : null;
    }
}

/// <summary>Caixa de dano agendada por um HITBOX: tipo, quadro inicial e duração relativos à ação, posição local e tamanho.</summary>
public sealed record HitBox(int Type, float StartFrame, float Frames, Vector3 Position, Vector3 Size);

/// <summary>
/// Executa um bloco de uma sequência do jogo (ex.: SQ_ATCK1, bloco do estilo "slash").
/// Ordem e valores dos comandos são FATO (dados do jogo). A semântica abaixo é HIPÓTESE, a confirmar medindo o jogo
/// (docs/gameplay/fase3-levantamento.md):
///  · LABEL marca o bloco de um estilo de arma; executa-se do LABEL do estilo até o próximo LABEL ou o fim.
///  · HITBOX "Set" agenda uma caixa para a próxima ACTION; "AllClear" descarta as agendadas.
///  · ACTION toca a animação; as caixas agendadas valem nos quadros [início, início + duração) dessa ação.
///  · WCHECK "Action" espera a animação acabar; "Atck0N" também espera, e só continua se o ataque foi apertado
///    durante a ação (janela de combo); senão a sequência termina. Outros WCHECK não bloqueiam aqui.
///  · Uma ACTION precedida de HITBOX tipo 60000 (sonda de conversa, seguida de "TALKWF") só acontece se a sonda achar
///    alguém para conversar; sem NPC por perto, a ação e o WCHECK "Action" seguinte são pulados.
///  · MOVE guarda o último comando de movimento; JUMP termina indicando a próxima sequência.
///  · Quadros a 30 Hz (taxa das animações Enbaya).
/// </summary>
public sealed class SequenceRunner
{
    public const float FramesPerSecond = 30f;
    /// <summary>Tipo de HITBOX da sonda de conversa (HIPÓTESE).</summary>
    public const int TalkProbe = 60000;

    readonly IReadOnlyDictionary<string, List<SeqCommand>> _seqs;
    readonly Func<string, float?> _animFrames;
    List<SeqCommand> _cmds = [];
    int _pc, _end;
    readonly List<HitBox> _pending = [];
    bool _attackBuffered, _skipWait;

    public SequenceRunner(IReadOnlyDictionary<string, List<SeqCommand>> sequences, Func<string, float?> animationFrames)
    {
        _seqs = sequences;
        _animFrames = animationFrames;
    }

    public bool Running { get; private set; }
    public string Sequence { get; private set; } = "";
    public string? Action { get; private set; }
    public float ActionFrame { get; private set; }
    public float ActionLength { get; private set; }
    public IReadOnlyList<HitBox> ActionHitBoxes { get; private set; } = [];
    public SeqCommand? Move { get; private set; }
    public string? Next { get; private set; }
    /// <summary>Contador de ações iniciadas (muda quando é preciso tocar uma nova animação).</summary>
    public int ActionSerial { get; private set; }
    public List<string> Skipped { get; } = [];

    /// <summary>Inicia a sequência no bloco do estilo; false se a sequência ou o estilo não existem.</summary>
    public bool Start(string sequence, string? style)
    {
        if (!_seqs.TryGetValue(sequence, out var cmds)) return false;
        int start = 0;
        if (style != null && cmds.Any(c => c.Kind == "LABEL"))
        {
            start = cmds.FindIndex(c => c.Kind == "LABEL" && c.S(0) == style);
            if (start < 0) return false;
            start++;
        }
        int end = cmds.FindIndex(start, c => c.Kind == "LABEL");
        _cmds = cmds;
        _pc = start;
        _end = end < 0 ? cmds.Count : end;
        _pending.Clear();
        Sequence = sequence;
        Action = null;
        ActionHitBoxes = [];
        Next = null;
        Running = true;
        _attackBuffered = false;
        _skipWait = false;
        Advance();
        return true;
    }

    /// <summary>Avança <paramref name="dt"/> segundos. <paramref name="attackPressed"/> = botão de ataque apertado neste passo.</summary>
    public void Tick(float dt, bool attackPressed)
    {
        if (!Running) return;
        if (attackPressed && Action != null) _attackBuffered = true;
        ActionFrame += dt * FramesPerSecond;
        Advance();
    }

    /// <summary>Caixas de dano ativas no quadro atual da ação.</summary>
    public IEnumerable<HitBox> ActiveHitBoxes() =>
        ActionHitBoxes.Where(h => ActionFrame >= h.StartFrame && ActionFrame < h.StartFrame + h.Frames);

    void Advance()
    {
        while (Running)
        {
            if (_pc >= _end) { Finish(null); return; }
            var c = _cmds[_pc];
            switch (c.Kind)
            {
                case "HITBOX":
                    if (c.S(0) == "AllClear") _pending.Clear();
                    else _pending.Add(new HitBox(c.I(1), c.F(2), c.F(3), c.V(4), c.V(5)));
                    break;
                case "ACTION" when _pending.Any(h => h.Type == TalkProbe):
                    _pending.RemoveAll(h => h.Type == TalkProbe);
                    _skipWait = true;
                    break;
                case "ACTION":
                    Action = c.S(0);
                    ActionFrame = 0;
                    ActionLength = _animFrames(Action) ?? 0;
                    ActionHitBoxes = _pending.ToList();
                    _pending.Clear();
                    _attackBuffered = false;
                    ActionSerial++;
                    break;
                case "WCHECK" when _skipWait && c.S(0) == "Action":
                    _skipWait = false;
                    break;
                case "WCHECK" when c.S(0) == "Action" || c.S(0).StartsWith("Atck", StringComparison.Ordinal):
                    if (Action != null && ActionFrame < ActionLength) return; // espera a animação
                    if (c.S(0).StartsWith("Atck", StringComparison.Ordinal) && !_attackBuffered) { Finish(null); return; }
                    break;
                case "WCHECK":
                    break;
                case "MOVE":
                    Move = c;
                    break;
                case "JUMP":
                    Finish(c.S(0));
                    return;
                default:
                    if (!Skipped.Contains(c.Kind)) Skipped.Add(c.Kind);
                    break;
            }
            _pc++;
        }
    }

    void Finish(string? next)
    {
        Running = false;
        Next = next;
        ActionHitBoxes = [];
    }
}
