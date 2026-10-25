namespace DW4.Script.Be;

/// <summary>O que o jogo (cliente ou servidor) faz com os comandos dos scripts.</summary>
public interface IBeEngine
{
    /// <summary>Comando entregue a um objeto (be:sd na hora ou depois do atraso, ou uma entrada set do diretor de demo).</summary>
    void Deliver(BeMessage message);

    /// <summary>Consulta (ISSEQNAME, GETSELECT, GETGENERATEPOS…). null = o jogo ainda não sabe responder.</summary>
    double? Query(BeMessage message);
}

/// <summary>
/// O modelo "dono" do script em execução (hit_*.lua roda no contexto do objeto atingido): MDLaddHp, MDLsetSeq etc.
/// </summary>
public interface IBeModel
{
    int Hp { get; set; }
    /// <summary>Código do golpe que acertou (MDLgetBulletKind): 10000 físico, 40000 + linha técnica (HIPÓTESE), …</summary>
    int BulletKind { get; }
    void SetSequence(string name);
}

/// <summary>
/// Evento de gatilho de área (mat*.lua). FATO (0x803037D0..78): o motor roda "&lt;volume&gt;.lua" quando muda quem está dentro
/// (entrada ou saída). HIPÓTESE forte pelos scripts: MATisPlayer() = 1 na entrada, 0 na saída; MATsetRun(0) desliga o volume.
/// </summary>
public sealed record BeMatEvent(bool Entered, bool AllInside, int PlayerNo, Action<bool> SetRun);

/// <summary>Dados do sistema que os SYS* devolvem (escolhas do menu e do grupo).</summary>
public sealed class BeSystem
{
    /// <summary>Pasta da fase (CFLgetFolderName), ex.: "dng101b1".</summary>
    public string FolderName { get; set; } = "";
    /// <summary>FATO (boot.lua): índice em JPN_, USA_, ENG_, FRA_, ITA_, GER_, SPN_; USA = 1.</summary>
    public int Country { get; set; } = 1;
    public int IsPackOnly { get; set; }
    /// <summary>Até 4 jogadores: nome do modelo ("p_girumon") e as 3 armas (SYSgetWeaponName(p, 0..2)); null = ausente.</summary>
    public (string Model, string[] Weapons)?[] Players { get; } = new (string, string[])?[4];
}

/// <summary>
/// O objeto global <c>be</c> dos scripts: os métodos do motor (tabela de 116 descritores em 0x804242AC, despachante
/// 0x802EA1FC, com sobrecarga pelo número de argumentos) ligados ao <see cref="IBeEngine"/>, ao <see cref="GameRam"/>, ao
/// <see cref="BeMessenger"/> e ao <see cref="DemoManager"/>. Métodos ainda não implementados devolvem nil e são avisados
/// uma vez por <see cref="Unimplemented"/>. Tipos: FATO pelos descritores — booleano de retorno vai como número 0/1
/// (FLGisOn faz pushnumber), nunca nil.
/// </summary>
public sealed class BeHost
{
    readonly IBeEngine _engine;
    readonly HashSet<string> _warned = [];
    readonly List<string> _files = [];
    readonly Queue<string> _luaQueue = new();

    public GameRam Ram { get; }
    public BeSystem Sys { get; }
    public BeMessenger Messenger { get; }
    public DemoManager Demo { get; }
    public LuaTable Table { get; } = new();

    /// <summary>Modelo no contexto do script atual (hit_*.lua); null fora desse contexto.</summary>
    public IBeModel? Model { get; set; }

    /// <summary>Gatilho de área no contexto do script atual (mat*.lua); null fora desse contexto.</summary>
    public BeMatEvent? Mat { get; set; }

    /// <summary>Lista montada pelo boot.lua (CFLclear/CFLsetName): os IGB que a fase carrega.</summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>Método be:* ainda não implementado (nome e argumentos), avisado uma vez por nome.</summary>
    public Action<string, object?[]>? Unimplemented { get; set; }

    public BeHost(IBeEngine engine, GameRam ram, BeSystem system)
    {
        _engine = engine;
        Ram = ram;
        Sys = system;
        Messenger = new BeMessenger(engine.Deliver);
        Demo = new DemoManager(Messenger, engine.Query);
        Table.Missing = (_, key) => key is string name ? Method(name) : null;
    }

    /// <summary>Próximo script agendado por be:DMsetLua, liberado quando o diretor de demo fica livre (HIPÓTESE).</summary>
    public string? NextLua() => Demo.Idle && _luaQueue.Count > 0 ? _luaQueue.Dequeue() : null;

    /// <summary>Um quadro de 60 Hz: entrega as mensagens atrasadas e avança o diretor de demo.</summary>
    public void Tick()
    {
        Messenger.Tick();
        Demo.Tick();
    }

    static string? S(object?[] a, int i) => i < a.Length ? Lua.ToStr(a[i]) : null;
    static int I(object?[] a, int i) => i < a.Length && Lua.TryNumber(a[i], out double d) ? (int)d : 0;
    static long L(object?[] a, int i) => i < a.Length && Lua.TryNumber(a[i], out double d) ? (long)d : 0;
    // HIPÓTESE: parâmetro booleano a partir de número: diferente de zero (os scripts passam 0/1).
    static bool B(object?[] a, int i) => i < a.Length && (a[i] is double d ? d != 0 : a[i] != null);
    static object?[] N(double v) => [v];
    static object?[] Bool(bool v) => [v ? 1.0 : 0.0];
    static readonly object?[] Nothing = [];

    static BeMessage Msg(object?[] a, int cmdAt = 4, int argsAt = -1) =>
        new(S(a, 1) ?? "", S(a, 2), I(a, 3), S(a, cmdAt) ?? "", BeArg.Parse(argsAt > 0 ? S(a, argsAt) : null), argsAt > 0 ? S(a, argsAt) : null);

    LuaHostFunction Method(string name) => new(name, a => Call(name, a));

    // a[0] é o próprio be (chamada be:Metodo(...)).
    object?[] Call(string name, object?[] a)
    {
        int n = a.Length - 1;
        switch (name)
        {
            // Mensagens na hora ou com atraso: sd (ids 29..32) = (classe, nome, índice, comando[, argumentos][, atraso]).
            case "sd":
            {
                bool hasArgs = n >= 5 && a[5] is string;
                var m = Msg(a, 4, hasArgs ? 5 : -1);
                long delay = n >= 6 ? L(a, 6) : n == 5 && !hasArgs ? L(a, 5) : 0;
                Messenger.Send(m, delay);
                return Nothing;
            }
            case "DMsetSD":
            {
                bool hasArgs = n >= 5 && a[5] is string;
                long delay = n >= 6 ? L(a, 6) : n == 5 && !hasArgs ? L(a, 5) : 0;
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Set, Msg(a, 4, hasArgs ? 5 : -1), delay));
                return Nothing;
            }
            case "DMchkSD":
            {
                // (classe, nome, índice, consulta[, argumento], condição)
                string cond = S(a, n) ?? "";
                var m = Msg(a, 4, n >= 6 ? 5 : -1);
                var kind = cond is "!=0" or "==1" ? DemoManager.Kind.CheckNonZero : DemoManager.Kind.CheckZero;
                Demo.Add(new DemoManager.Entry(kind, m, 0));
                return Nothing;
            }
            case "DMswtSD":
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Switch, Msg(a, 4, n >= 5 ? 5 : -1), 0));
                return Nothing;
            case "DMcase":
                Demo.Add(new DemoManager.Entry(DemoManager.Kind.Case, null, 0, I(a, 1)));
                return Nothing;
            case "DMrun":
                Demo.Run();
                return Nothing;
            case "DMgetCount":
                return N(Demo.Count);
            case "DMsetLua":
                if (S(a, 1) is { } lua) _luaQueue.Enqueue(lua);
                return Nothing;

            // Estado comum
            case "FLGisOn": return N(Ram.FlagOn(I(a, 1)));
            case "FLGset": Ram.SetFlag(I(a, 1), B(a, 2)); return Nothing;
            case "COUNTERget": return N(Ram.Counter(I(a, 1)));
            case "COUNTERset": Ram.SetCounter(I(a, 1), I(a, 2)); return Nothing;

            // Lista de arquivos da fase
            case "CFLclear": _files.Clear(); return Nothing;
            case "CFLsetName": if (S(a, 1) is { } file) _files.Add(file); return Nothing;
            case "CFLgetFolderName": return [Sys.FolderName];

            // Sistema
            case "SYSisJoinPlayer": return Bool(Player(I(a, 1)) != null);
            case "SYSgetPlayerNinzu": return N(Ram.Ninzu);
            case "SYSgetPlayerName": return [Player(I(a, 1))?.Model];
            case "SYSgetWeaponName":
                return [Player(I(a, 1)) is { } p && I(a, 2) is var w && w >= 0 && w < p.Weapons.Length ? p.Weapons[w] : null];
            case "SYSgetDifficulty": return N(Ram.Difficulty);
            case "SYSgetCountry": return N(Sys.Country);
            case "SYSgetCameraMode": return N(Ram.CameraMode);
            case "SYSgetLightMode": return N(Ram.LightMode);
            case "SYSgetIsPackOnly": return N(Sys.IsPackOnly);

            // Modelo do contexto (hit_g_box.lua: MDLsetDMGTurn(0); se não for técnica 40040..40099, MDLaddHp(−100) e SQ_DDMG).
            case "MDLgetBulletKind": return N(Model?.BulletKind ?? 0);
            case "MDLgetHp": return N(Model?.Hp ?? 0);
            case "MDLaddHp": if (Model != null) Model.Hp += I(a, 1); return Nothing;
            case "MDLsetSeq": if (Model != null && S(a, 1) is { } seq) Model.SetSequence(seq); return Nothing;
            case "MDLisLive": return Bool(Model is { Hp: > 0 });
            // HIPÓTESE pelo nome: turno de dano (reação); sem efeito na fatia.
            case "MDLsetDMGTurn": return Nothing;

            // Gatilho de área do contexto
            case "MATisPlayer": return Bool(Mat?.Entered == true);
            case "MATisAllPlayer": return Bool(Mat?.AllInside == true);
            case "MATgetPlayerNo": return N(Mat?.PlayerNo ?? 0);
            case "MATsetRun": Mat?.SetRun(B(a, 1)); return Nothing;

            // FATO (0x802EE1F0): envia PLAYERPOINT GETGENERATEPOS e devolve o inteiro (ponto de entrada na fase).
            case "PLAYERgetPos":
                return N(_engine.Query(new BeMessage("PLAYERPOINT", null, -1, "GETGENERATEPOS", [], null)) ?? 0);
        }
        if (_warned.Add(name)) Unimplemented?.Invoke(name, a[1..]);
        return Nothing;
    }

    (string Model, string[] Weapons)? Player(int p) => p >= 0 && p < Sys.Players.Length ? Sys.Players[p] : null;
}
