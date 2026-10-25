using System.Numerics;

namespace DW4.Gameplay;

/// <summary>
/// Sequências dos objetos de cenário (portas, pisos, gravador, caixas: KIND_OBJECT sem esqueleto). Valores dos comandos são
/// FATO (igb-seq do #179, docs/gameplay/fase4-objetos-dng1.md); a semântica é HIPÓTESE, pelo fluxo dos dados:
///  · cada sequência guarda o rótulo atual (LABEL); começar uma sequência executa só o bloco desse rótulo;
///  · LABELCTRL &lt;rótulo&gt; [sequência] troca o rótulo guardado de outra sequência (padrão: SQ_WAIT);
///  · ACTION liga o JOINTSET a..b da ação (quadros de 60 Hz; "LP" repete, senão para no fim) e o FADESET dela;
///  · a ação roda os comandos dela (JOINTSET, FADESET, WAITCHECK "Frame" n / "Joint"); WCHECK "Action" espera a ação
///    passar por todos (as esperas do piso ficam nas ações: WIT2ON 30, ONSW01 120, WAIT02 180);
///  · HITBOX "Set" tipo início duração posição tamanho: caixa ativa por "duração" quadros (−1 = sempre), na unidade do
///    WAITCHECK (HITBOX 120 ↔ espera 120 no piso);
///  · JUMP vai para outra sequência no rótulo guardado dela (assim o ciclo do piso ligado se repete).
/// </summary>
public sealed class ObjectSequencer
{
    public const float FramesPerSecond = 60f;
    readonly IReadOnlyDictionary<string, List<SeqCommand>> _seqs;
    readonly IReadOnlyDictionary<string, List<SeqCommand>> _actions;
    readonly Dictionary<string, string> _labels = [];
    // Caixa e quadros desde que foi armada (começa a valer no quadro inicial do HITBOX e dura os quadros pedidos; −1 = até limpar).
    readonly List<(HitBox Box, float Age)> _boxes = [];
    List<SeqCommand> _cmds = [];
    int _pc, _end, _jumps;
    float _wait;
    bool _waitAction;
    float _from, _to, _fadeFrom = 1, _fadeTo = 1, _fadeLen, _fadeT;
    bool _loop;
    // A ação também é um pequeno programa: JOINTSET, FADESET e as esperas dela (WAITCHECK "Frame" n / "Joint").
    List<SeqCommand> _act = [];
    int _apc;
    float _actWait;
    bool _actWaitJoint;

    public ObjectSequencer(IReadOnlyDictionary<string, List<SeqCommand>> sequences, IReadOnlyDictionary<string, List<SeqCommand>> actions)
    {
        _seqs = sequences;
        _actions = actions;
    }

    public string? Sequence { get; private set; }
    public string? Action { get; private set; }
    /// <summary>Quadro do JOINTSET (60 Hz) para o tempo das igTransformSequence do objeto.</summary>
    public float Frame { get; private set; }
    public float Alpha { get; private set; } = 1;
    public bool Running { get; private set; }
    /// <summary>SQ_ERAS: o objeto deve sumir.</summary>
    public bool Erased { get; private set; }
    public IEnumerable<HitBox> ActiveHitBoxes => _boxes.Where(b => b.Age >= b.Box.StartFrame).Select(b => b.Box);
    /// <summary>Último AIMAP executado (ex.: SQ_PHASE2 do f_bit troca para MAP01); quem roda a IA consome com <see cref="TakeAiMap"/>.</summary>
    public string? AiMap { get; private set; }
    public string? TakeAiMap() { var m = AiMap; AiMap = null; return m; }
    /// <summary>Contador de caixas ligadas (muda a cada HITBOX "Set"): um acerto por caixa.</summary>
    public int BoxSerial { get; private set; }

    public string? Label(string sequence) => _labels.GetValueOrDefault(sequence);
    public void SetLabel(string sequence, string label) => _labels[sequence] = label;

    /// <summary>Começa a sequência no rótulo guardado (ou do começo, sem rótulos). false se não existe.</summary>
    public bool Start(string sequence)
    {
        _jumps = 0;
        return Begin(sequence);
    }

    bool Begin(string sequence)
    {
        if (sequence == "SQ_ERAS") { Erased = true; Running = false; return true; }
        if (!_seqs.TryGetValue(sequence, out var cmds)) return false;
        int start = 0;
        if (cmds.Any(c => c.Kind == "LABEL"))
        {
            var label = _labels.GetValueOrDefault(sequence);
            int at = label == null ? -1 : cmds.FindIndex(c => c.Kind == "LABEL" && c.S(0) == label);
            start = at >= 0 ? at + 1 : cmds.FindIndex(c => c.Kind == "LABEL") + 1;
        }
        int end = cmds.FindIndex(start, c => c.Kind == "LABEL");
        _cmds = cmds;
        _pc = start;
        _end = end < 0 ? cmds.Count : end;
        _wait = 0;
        _waitAction = false;
        Sequence = sequence;
        Running = true;
        Advance();
        return true;
    }

    /// <summary>Avança <paramref name="dt"/> segundos.</summary>
    public void Tick(float dt)
    {
        float frames = dt * FramesPerSecond;
        if (Action != null)
        {
            Frame += frames;
            if (Frame >= _to)
                Frame = _loop && _to > _from ? _from + (Frame - _to) % (_to - _from) : _to;
        }
        if (_fadeLen > 0 && _fadeT < _fadeLen)
        {
            _fadeT = Math.Min(_fadeLen, _fadeT + frames);
            Alpha = _fadeFrom + (_fadeTo - _fadeFrom) * _fadeT / _fadeLen;
        }
        for (int i = _boxes.Count - 1; i >= 0; i--)
        {
            var (box, age) = _boxes[i];
            age += frames;
            if (box.Frames >= 0 && age >= box.StartFrame + box.Frames) _boxes.RemoveAt(i); else _boxes[i] = (box, age);
        }
        if (_actWait > 0) _actWait -= frames;
        RunAction();
        if (!Running) return;
        if (_wait > 0) _wait -= frames;
        _jumps = 0;
        Advance();
    }

    void Advance()
    {
        while (Running)
        {
            if (_wait > 0) return;
            if (_waitAction)
            {
                if (!ActionDone) return;
                _waitAction = false;
            }
            if (_pc >= _end) { Running = false; return; }
            var c = _cmds[_pc++];
            switch (c.Kind)
            {
                case "ACTION":
                    PlayAction(c.S(0));
                    break;
                case "WCHECK" when c.S(0) == "Action":
                    _waitAction = true;
                    break;
                case "WAITCHECK" when c.S(0) == "Frame":
                    _wait = c.F(1);
                    break;
                case "HITBOX" when c.S(0) == "AllClear":
                    _boxes.Clear();
                    break;
                case "HITBOX":
                    _boxes.Add((new HitBox(c.I(1), c.F(2), c.F(3), c.V(4), c.V(5)), 0));
                    BoxSerial++;
                    break;
                case "LABELCTRL":
                {
                    // Valores: rótulo e, opcionalmente, a sequência alvo (a ordem exata ainda não foi conferida).
                    var texts = c.Values.OfType<string>().ToList();
                    string? target = texts.FirstOrDefault(t => t.StartsWith("SQ_", StringComparison.Ordinal));
                    string? label = texts.FirstOrDefault(t => !t.StartsWith("SQ_", StringComparison.Ordinal));
                    if (label != null) _labels[target ?? "SQ_WAIT"] = label;
                    break;
                }
                case "JUMP":
                    if (++_jumps > 16) { Running = false; return; } // laço sem espera
                    var next = c.S(0);
                    if (!Begin(next)) Running = false;
                    return;
                case "AIMAP":
                    AiMap = c.S(0);
                    break;
                case "FADESET":
                    Fade(c);
                    break;
            }
        }
    }

    void PlayAction(string name)
    {
        Action = name;
        _loop = false;
        _act = _actions.TryGetValue(name, out var cmds) ? cmds : [];
        _apc = 0;
        _actWait = 0;
        _actWaitJoint = false;
        RunAction();
    }

    // FATO: as esperas ficam dentro das ações (WIT2ON: JOINTSET 40–70 LP + WAITCHECK "Frame" 30; ONSW01: … "Frame" 120;
    // OFF2ON: "Frame" 20, FADESET OUT, "Joint" 0). HIPÓTESE: o WCHECK "Action" da sequência espera a ação passar por todas.
    void RunAction()
    {
        while (_apc < _act.Count)
        {
            if (_actWait > 0) return;
            if (_actWaitJoint)
            {
                if (!_loop && Frame < _to) return;
                _actWaitJoint = false;
            }
            var c = _act[_apc++];
            switch (c.Kind)
            {
                case "JOINTSET":
                    _from = c.F(1);
                    _to = c.F(2);
                    _loop = c.S(3) == "LP";
                    Frame = _from;
                    break;
                case "FADESET":
                    Fade(c);
                    break;
                case "WAITCHECK" when c.S(0) == "Frame":
                    _actWait = c.F(1);
                    break;
                case "WAITCHECK" when c.S(0) == "Joint":
                    _actWaitJoint = true;
                    break;
            }
        }
    }

    bool ActionDone => _apc >= _act.Count && _actWait <= 0 && (!_actWaitJoint || _loop || Frame >= _to);

    // FADESET "IN"/"OUT" n: transparência em n quadros; "HIDE": escondido (porta aberta).
    void Fade(SeqCommand c)
    {
        switch (c.S(0))
        {
            case "IN": (_fadeFrom, _fadeTo, _fadeLen, _fadeT) = (0, 1, Math.Max(1, c.F(1)), 0); Alpha = 0; break;
            case "OUT": (_fadeFrom, _fadeTo, _fadeLen, _fadeT) = (Alpha, 0, Math.Max(1, c.F(1)), 0); break;
            case "HIDE": (_fadeLen, Alpha) = (0, 0); break;
        }
    }
}
