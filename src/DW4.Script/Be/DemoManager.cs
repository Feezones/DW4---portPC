namespace DW4.Script.Be;

/// <summary>
/// beDemoManager (*0x8053590C). FATO: be:DMsetSD/DMchkSD não executam, só anexam uma entrada à lista em montagem
/// (0x802F7580: tipo, mensagem, atraso); be:DMrun entrega a lista ao executor e zera a montagem; o script Lua continua
/// (Lua 4.0 não suspende). DMchkSD: "!=0"/"==1" → tipo 2, "==0"/"!=1" → tipo 3 (0x802ED140).
/// HIPÓTESES (executor não lido): as entradas rodam em ordem, quadro a quadro; um set entrega a mensagem pelo beMessenger
/// com o seu atraso e segue sem esperar; um chk segura a fila até a condição valer; DMswtSD guarda um valor e cada
/// DMcase(n) seguinte libera as entradas até o próximo DMcase só se n for esse valor.
/// </summary>
public sealed class DemoManager(BeMessenger messenger, Func<BeMessage, double?> query)
{
    public enum Kind { Set = 0, CheckNonZero = 2, CheckZero = 3, Switch = 4, Case = 5, SwitchEnd = 6 }

    public sealed record Entry(Kind Kind, BeMessage? Message, long Delay, int CaseValue = 0);

    readonly List<Entry> _building = [];
    readonly Queue<Entry> _running = new();
    double _switch;
    bool _skipping, _matched;

    /// <summary>Chamado quando uma condição não pôde ser avaliada (consulta sem resposta) e foi considerada satisfeita.</summary>
    public Action<BeMessage>? Unanswered { get; set; }

    public void Add(Entry e) => _building.Add(e);

    public void Run()
    {
        foreach (var e in _building) _running.Enqueue(e);
        _building.Clear();
    }

    /// <summary>HIPÓTESE: DMgetCount = entradas ainda não executadas (montagem + execução).</summary>
    public int Count => _building.Count + _running.Count;

    public bool Idle => _running.Count == 0;

    /// <summary>Executa o que der neste quadro; para no primeiro chk que ainda não vale.</summary>
    public void Tick()
    {
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
            if (_skipping) { _running.Dequeue(); continue; }
            switch (e.Kind)
            {
                case Kind.Set:
                    messenger.Send(e.Message!, e.Delay);
                    break;
                case Kind.Switch:
                    _switch = query(e.Message!) ?? 0;
                    _matched = false;
                    break;
                case Kind.CheckNonZero or Kind.CheckZero:
                    var v = query(e.Message!);
                    if (v == null) Unanswered?.Invoke(e.Message!);
                    else if ((v != 0) != (e.Kind == Kind.CheckNonZero)) return;
                    break;
            }
            _running.Dequeue();
        }
        _skipping = _matched = false;
    }
}
