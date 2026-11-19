namespace DW4.Script.Be;

/// <summary>
/// beDemoManager (*0x8053590C). FATO: be:DMsetSD/DMchkSD não executam, só anexam uma entrada à lista em montagem
/// (0x802F7580: tipo, mensagem, atraso); be:DMrun entrega a lista ao executor e zera a montagem; o script Lua continua
/// (Lua 4.0 não suspende). DMchkSD: "!=0"/"==1" → tipo 2, "==0"/"!=1" → tipo 3 (0x802ED140).
/// HIPÓTESES (executor não lido): as entradas rodam em ordem, quadro a quadro; um set entrega a mensagem pelo beMessenger
/// com o seu atraso e segue sem esperar; um chk segura a fila ENQUANTO a condição vale (HIPÓTESE forte: é a única leitura
/// em que as esperas em dupla dos scripts fecham, ex. ISSEQNAME "SQ_BHLFIN30" "==0" seguido de "!=0" no funcStageChange,
/// ISDEMOCAMERA "==0"/"!=0" no first.lua, com as consultas booleanas naturais: 1 = rodando/na sequência/terminou); DMswtSD guarda um valor e cada
/// DMcase(n) seguinte libera as entradas até o próximo DMcase só se n for esse valor.
/// </summary>
public sealed class DemoManager(BeMessenger messenger, Func<BeMessage, double?> query)
{
    public enum Kind { Set = 0, CheckNonZero = 2, CheckZero = 3, Switch = 4, Case = 5, SwitchEnd = 6, Lua = 7, RestoreSwitch = 8 }

    /// <param name="Lua">Kind.Lua: o script do be:DMsetLua. Kind.RestoreSwitch: <paramref name="CaseValue"/> = valor do DMswtSD de fora.</param>
    public sealed record Entry(Kind Kind, BeMessage? Message, long Delay, int CaseValue = 0, string? Lua = null, double Switch = 0, bool Matched = false);

    readonly List<Entry> _building = [];
    readonly Queue<Entry> _running = new();
    double _switch;
    bool _skipping, _matched;
    // Quadros até uma SETDEMOCAMERA com atraso chegar (ver Tick).
    long _demoPending;

    /// <summary>Chamado quando uma condição não pôde ser avaliada (consulta sem resposta) e foi considerada satisfeita.</summary>
    public Action<BeMessage>? Unanswered { get; set; }

    /// <summary>Roda um script agendado na fila (Kind.Lua), quando a execução chega nele.</summary>
    public Action<string>? RunLua { get; set; }

    bool _buildingCase, _splicing;

    /// <summary>
    /// A montagem está dentro de um DMcase (entre DMcase e DMswtEnd). FATO (matz00.lua e stchg01.lua da stag0001, fim do tutorial):
    /// "DMcase 0 … DMsetLua stchg01.lua / DMcase 1 DMsetLua restart.lua / DMswtEnd"; o restart.lua faz FLGset(503, 0) na hora.
    /// Rodar o DMsetLua já na montagem rodava os dois casos e apagava a 503 (tutorial feito), então o portal do lobby (mate00.lua)
    /// mandava de novo ao tutorial. Dentro de um caso o script vira uma entrada da fila e só roda se o caso valer.
    /// </summary>
    public bool BuildingCase => _buildingCase;

    public void Add(Entry e)
    {
        if (e.Kind == Kind.Case) _buildingCase = true;
        else if (e.Kind == Kind.SwitchEnd) _buildingCase = false;
        _building.Add(e);
    }

    public void Run()
    {
        _buildingCase = false;
        if (_splicing)
        {
            // Script de um DMsetLua rodando pela fila: os passos dele entram já, antes do resto da fila (no mesmo lugar em que
            // entravam quando o DMsetLua rodava na montagem).
            var rest = _running.ToArray();
            _running.Clear();
            foreach (var e in _building) _running.Enqueue(e);
            foreach (var e in rest) _running.Enqueue(e);
        }
        else foreach (var e in _building) _running.Enqueue(e);
        _building.Clear();
    }

    /// <summary>HIPÓTESE: DMgetCount = entradas ainda não executadas (montagem + execução).</summary>
    public int Count => _building.Count + _running.Count;

    public bool Idle => _running.Count == 0;

    /// <summary>Entrada que está segurando a fila (o chk que ainda não vale), para diagnóstico; null com a fila andando.</summary>
    public Entry? Blocking { get; private set; }

    /// <summary>Executa o que der neste quadro; para no primeiro chk que ainda não vale.</summary>
    public void Tick()
    {
        if (_demoPending > 0) _demoPending--;
        while (_running.Count > 0)
        {
            var e = _running.Peek();
            // HIPÓTESE: DMcase(−1) é o padrão (vale se nenhum caso anterior valeu); DMswtEnd fecha o bloco.
            if (e.Kind == Kind.Case)
            {
                _skipping = e.CaseValue == -1 ? _matched : e.CaseValue != (int)_switch;
                _matched |= !_skipping;
                _running.Dequeue();
                continue;
            }
            if (e.Kind == Kind.SwitchEnd)
            {
                _skipping = _matched = false;
                _running.Dequeue();
                continue;
            }
            if (e.Kind == Kind.RestoreSwitch)
            {
                (_switch, _matched, _skipping) = (e.Switch, e.Matched, false);
                _running.Dequeue();
                continue;
            }
            if (_skipping) { _running.Dequeue(); continue; }
            if (e.Kind == Kind.Lua)
            {
                _running.Dequeue();
                // O DMswtSD/DMcase do script de dentro não pode mexer no bloco de fora: o estado volta depois dos passos dele.
                var rest = _running.ToArray();
                _running.Clear();
                _running.Enqueue(new Entry(Kind.RestoreSwitch, null, 0, Switch: _switch, Matched: _matched));
                foreach (var r in rest) _running.Enqueue(r);
                _splicing = true;
                try { RunLua?.Invoke(e.Lua!); }
                finally { _splicing = false; }
                if (_building.Count > 0) { _splicing = true; try { Run(); } finally { _splicing = false; } }
                continue;
            }
            switch (e.Kind)
            {
                case Kind.Set:
                    messenger.Send(e.Message!, e.Delay);
                    // FATO (gene00.lua do tutorial): DMsetSD CAMERA SETDEMOCAMERA "stageend" com atraso 220 e logo depois DMchkSD
                    // ISDEMOCAMERA "==0" e "!=0". HIPÓTESE: o chk espera a demo que ainda vai começar; sem isso a fila passava antes
                    // dela, o SETBOXCAMERA vinha antes da demo (câmera presa nela) e o PLAYER OFCTRL atrasado chegava depois do
                    // ONCTRL (jogador travado).
                    if (e.Message is { Class: "CAMERA", Command: "SETDEMOCAMERA" } && e.Delay > 0) _demoPending = Math.Max(_demoPending, e.Delay + 1);
                    break;
                case Kind.Switch:
                    _switch = query(e.Message!) ?? 0;
                    _matched = false;
                    break;
                case Kind.CheckNonZero or Kind.CheckZero:
                    var v = query(e.Message!);
                    if (_demoPending > 0 && e.Message is { Class: "CAMERA", Command: "ISDEMOCAMERA" }) v = 0;
                    if (v == null) Unanswered?.Invoke(e.Message!);
                    else if ((v != 0) == (e.Kind == Kind.CheckNonZero)) { Blocking = e; return; }
                    break;
            }
            _running.Dequeue();
        }
        Blocking = null;
        _skipping = _matched = false;
    }
}
