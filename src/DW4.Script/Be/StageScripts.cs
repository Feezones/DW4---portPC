namespace DW4.Script.Be;

/// <summary>
/// Scripts de uma fase na ordem fixa do motor (máquina de estados em 0x802F8A64, FATO): func.lua e boot.lua, depois a carga
/// dos IGB listados, depois start.lua e first.lua; todos na mesma VM. Os demais entram por be:DMsetLua e por eventos.
/// </summary>
public sealed class StageScripts
{
    readonly IReadOnlyDictionary<string, byte[]> _chunks;

    public LuaVM Vm { get; } = new();
    public BeHost Host { get; }

    /// <summary>Erro de script (nome, mensagem); o jogo continua.</summary>
    public Action<string, string>? Error { get; set; }

    public StageScripts(IReadOnlyDictionary<string, byte[]> chunks, BeHost host)
    {
        _chunks = chunks;
        Host = host;
        Vm.Globals["be"] = host.Table;
        host.RunNow ??= name => Has(name) && Run(name); // o primeiro conjunto de scripts da fase (o gameStart.lua reusa o host)
    }

    public bool Has(string name) => _chunks.ContainsKey(name);

    public bool Run(string name)
    {
        if (!_chunks.TryGetValue(name, out var chunk)) { Error?.Invoke(name, "script não existe nesta fase"); return false; }
        try { Vm.DoChunk(chunk); return true; }
        catch (LuaException ex) { Error?.Invoke(name, ex.Message); return false; }
    }

    /// <summary>Executa um script no contexto de um modelo (hit_*.lua do objeto atingido).</summary>
    public bool RunAs(string name, IBeModel model)
    {
        var previous = Host.Model;
        Host.Model = model;
        try { return Run(name); }
        finally { Host.Model = previous; }
    }

    /// <summary>
    /// Roda o script de textos (txt_status.lua) para uma tela e devolve os TEXTSET pedidos, sem entregar nada ao motor.
    /// FATO: o script define F(texto, fonte, nó, alinhamento) = sd TEXTCTL TEXTSET texto, ALIGN fonte/nó/alinhamento e SWITCH
    /// fonte/nó/DISPON, e cada bloco "if be:TXTgetCallNo() == n" chama F com mswkPRGxxxx = "FONTE/nó/I&lt;k&gt;". Os índices k
    /// mudam por fase (o pacote de textos de cada fase tem outra ordem). <paramref name="fallback"/>: chunk da pasta comum.
    /// </summary>
    public List<BeMessage> RunText(string name, int callNo, byte[]? fallback = null)
    {
        var got = new List<BeMessage>();
        if (!_chunks.TryGetValue(name, out var chunk) && (chunk = fallback!) == null) return got;
        var (prevNo, prevCapture) = (Host.TextCallNo, Host.Capture);
        Host.TextCallNo = callNo;
        Host.Capture = m => { if (m.Class == "TEXTCTL" && m.Command == "TEXTSET") got.Add(m); };
        try { Vm.DoChunk(chunk); }
        catch (LuaException ex) { Error?.Invoke(name, ex.Message); }
        finally { (Host.TextCallNo, Host.Capture) = (prevNo, prevCapture); }
        return got;
    }

    /// <summary>Executa o script de um gatilho de área (entrada ou saída).</summary>
    public bool RunMat(string name, BeMatEvent ev)
    {
        var previous = Host.Mat;
        Host.Mat = ev;
        try { return Run(name); }
        finally { Host.Mat = previous; }
    }

    /// <summary>Estado 3: func.lua e boot.lua. Devolve a lista de IGB que a fase pediu.</summary>
    public IReadOnlyList<string> Boot()
    {
        Run("func.lua");
        Run("boot.lua");
        return Host.Files;
    }

    /// <summary>Estado 7 em diante: start.lua e first.lua.</summary>
    public void Start()
    {
        Run("start.lua");
        Run("first.lua");
    }

    /// <summary>Um quadro de 60 Hz: mensagens atrasadas, diretor de demo e o próximo script agendado.</summary>
    public void Tick()
    {
        Host.Tick();
        if (Host.NextLua() is { } next) Run(next);
    }
}
