namespace DW4.Gameplay;

/// <summary>
/// Mapa de controles do port (DECISÃO DO PORT, docs/decisoes-port-pc.md): ações do jogo → teclas/botões, em dois aparelhos
/// ("teclado" = teclado e mouse; "controle"), com padrão, troca, aviso de conflito e volta ao padrão. Cada ligação é um texto:
/// "tecla:J" (nome do Key da Godot), "mouse:1" (MouseButton), "botao:0" (JoyButton) ou "eixo:4+" (JoyAxis e o lado).
/// O padrão do controle traduz o do GameCube para o controle moderno (layout Xbox da Godot). FATO (ícones de botão da fonte do jogo
/// nos textos do tutorial e das dicas): "≪" ataca (segurar carrega), "≒" (botão Z) pula, "∠" (gatilho L) defende, "√" (botão B)
/// abre a lista de itens, "≡"/"娃" (direcional) escolhem a técnica, START abre o status. DESCONHECIDO: o botão de "≪" (o recorte
/// do ícone na fonte não fecha; HIPÓTESE: A) e os de X/Y/R (no código, três botões abrem as listas do painel de técnicas).
/// </summary>
public sealed class InputBindings
{
    public const string Keyboard = "teclado", Pad = "controle";

    /// <summary>Ação: id (chave no config e no InputMap, "dw4_" + id) e nome mostrado.</summary>
    public sealed record GameAction(string Id, string Label);

    public static readonly GameAction[] Actions =
    [
        new("cima", "Andar para cima"), new("baixo", "Andar para baixo"), new("esquerda", "Andar para a esquerda"), new("direita", "Andar para a direita"),
        new("ataque", "Atacar (segurar carrega)"), new("tecnica", "Usar a técnica escolhida"), new("pulo", "Pular"), new("guarda", "Defender"),
        new("tecnica_anterior", "Técnica anterior"), new("tecnica_proxima", "Próxima técnica"),
        new("tecnica1", "Técnica 1"), new("tecnica2", "Técnica 2"), new("tecnica3", "Técnica 3"), new("tecnica4", "Técnica 4"),
        new("tecnica5", "Técnica 5"), new("tecnica6", "Técnica 6"),
        new("status", "Menu de status"), new("confirmar", "Confirmar"), new("cancelar", "Cancelar"), new("equipar", "Equipar o último item"),
        new("chat", "Abrir o chat"), new("pausa", "Gravar e voltar ao menu"),
    ];

    static readonly Dictionary<string, string[]> KeyboardDefaults = new()
    {
        ["cima"] = ["tecla:W", "tecla:Up"], ["baixo"] = ["tecla:S", "tecla:Down"], ["esquerda"] = ["tecla:A", "tecla:Left"], ["direita"] = ["tecla:D", "tecla:Right"],
        ["ataque"] = ["tecla:J", "mouse:1"], ["tecnica"] = ["tecla:K", "mouse:2"], ["pulo"] = ["tecla:Space"], ["guarda"] = ["tecla:L"],
        ["tecnica_anterior"] = ["mouse:4"], ["tecnica_proxima"] = ["mouse:5"],
        ["tecnica1"] = ["tecla:Key1"], ["tecnica2"] = ["tecla:Key2"], ["tecnica3"] = ["tecla:Key3"], ["tecnica4"] = ["tecla:Key4"],
        ["tecnica5"] = ["tecla:Key5"], ["tecnica6"] = ["tecla:Key6"],
        ["status"] = ["tecla:Tab"], ["confirmar"] = ["tecla:Enter", "tecla:J"], ["cancelar"] = ["tecla:Backspace", "tecla:K"], ["equipar"] = ["tecla:E"],
        ["chat"] = ["tecla:Enter"], ["pausa"] = ["tecla:Escape"],
    };

    // GameCube → controle moderno: A → A (0), B → B (1), X → X (2), Y → Y (3), Z → RB (10), L → LT (eixo 4), R → RT (eixo 5),
    // START → Start (6), direcional → direcional (11..14), alavanca → analógico esquerdo (eixos 0/1).
    static readonly Dictionary<string, string[]> PadDefaults = new()
    {
        ["cima"] = ["eixo:1-"], ["baixo"] = ["eixo:1+"], ["esquerda"] = ["eixo:0-"], ["direita"] = ["eixo:0+"],
        ["ataque"] = ["botao:0"], ["tecnica"] = ["botao:2"], ["pulo"] = ["botao:10"], ["guarda"] = ["eixo:4+"],
        ["tecnica_anterior"] = ["botao:13"], ["tecnica_proxima"] = ["botao:14"],
        ["tecnica1"] = [], ["tecnica2"] = [], ["tecnica3"] = [], ["tecnica4"] = [], ["tecnica5"] = [], ["tecnica6"] = [],
        ["status"] = ["botao:6"], ["confirmar"] = ["botao:0"], ["cancelar"] = ["botao:1"], ["equipar"] = ["botao:3"],
        ["chat"] = [], ["pausa"] = ["botao:4"],
    };

    // Duas telas: menus (confirmar, cancelar) e jogo (o resto). Ações de telas diferentes podem dividir a mesma tecla sem conflito
    // (J ataca e confirma; K usa a técnica e cancela; Enter abre o chat e confirma).
    static int Context(string action) => action is "confirmar" or "cancelar" ? 1 : 0;

    readonly Dictionary<string, Dictionary<string, List<string>>> _maps = new()
    {
        [Keyboard] = [],
        [Pad] = [],
    };

    public InputBindings()
    {
        RestoreDefaults(Keyboard);
        RestoreDefaults(Pad);
    }

    static Dictionary<string, string[]> Defaults(string device) => device == Pad ? PadDefaults : KeyboardDefaults;

    public IReadOnlyList<string> Get(string device, string action) => _maps[device].TryGetValue(action, out var l) ? l : [];

    public void RestoreDefaults(string device)
    {
        var map = _maps[device];
        map.Clear();
        foreach (var (action, list) in Defaults(device)) map[action] = [.. list];
    }

    /// <summary>Ações (fora a própria e as compatíveis) que já usam a ligação nesse aparelho.</summary>
    public List<string> Conflicts(string device, string binding, string action) =>
        _maps[device].Where(kv => kv.Key != action && Context(kv.Key) == Context(action) && kv.Value.Contains(binding)).Select(kv => kv.Key).ToList();

    /// <summary>
    /// Troca a ligação principal da ação pela nova; quem já a usava perde essa ligação (o aviso é a lista devolvida).
    /// </summary>
    public List<string> Set(string device, string action, string binding)
    {
        var taken = Conflicts(device, binding, action);
        foreach (var other in taken) _maps[device][other].Remove(binding);
        var list = _maps[device].TryGetValue(action, out var l) ? l : _maps[device][action] = [];
        list.Remove(binding);
        if (list.Count > 0) list[0] = binding; else list.Add(binding);
        return taken;
    }

    public void Clear(string device, string action) => _maps[device][action] = [];

    /// <summary>Linhas "id=\"a,b\"" de um aparelho, para a seção dele no config.cfg.</summary>
    public IEnumerable<(string Key, string Value)> Save(string device) =>
        Actions.Select(a => (a.Id, string.Join(",", Get(device, a.Id))));

    /// <summary>Lê uma linha da seção do aparelho; ações desconhecidas são ignoradas e ligações inválidas descartadas.</summary>
    public void Load(string device, string action, string value)
    {
        if (!Actions.Any(a => a.Id == action)) return;
        _maps[device][action] = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Valid).Distinct().ToList();
    }

    public static bool Valid(string binding)
    {
        int colon = binding.IndexOf(':');
        if (colon <= 0 || colon == binding.Length - 1) return false;
        string kind = binding[..colon], value = binding[(colon + 1)..];
        return kind switch
        {
            "tecla" => value.All(c => char.IsLetterOrDigit(c) || c == '_'),
            "mouse" or "botao" => int.TryParse(value, out int n) && n >= 0 && n < 128,
            "eixo" => value.Length >= 2 && value[^1] is '+' or '-' && int.TryParse(value[..^1], out int a) && a is >= 0 and < 16,
            _ => false,
        };
    }

    /// <summary>Texto curto da ligação para a tela ("J", "Botão esquerdo", "A", "LT").</summary>
    public static string Describe(string binding)
    {
        int colon = binding.IndexOf(':');
        if (colon < 0) return binding;
        string kind = binding[..colon], v = binding[(colon + 1)..];
        return kind switch
        {
            "tecla" => v.StartsWith("Key", StringComparison.Ordinal) && v.Length == 4 ? v[3..] : v,
            "mouse" => v switch { "1" => "Mouse esquerdo", "2" => "Mouse direito", "3" => "Mouse do meio", "4" => "Roda para cima", "5" => "Roda para baixo", _ => $"Mouse {v}" },
            "botao" => v switch
            {
                "0" => "A", "1" => "B", "2" => "X", "3" => "Y", "4" => "Back", "6" => "Start", "7" => "L3", "8" => "R3", "9" => "LB", "10" => "RB",
                "11" => "Direcional ↑", "12" => "Direcional ↓", "13" => "Direcional ←", "14" => "Direcional →", _ => $"Botão {v}",
            },
            "eixo" => v switch
            {
                "0-" => "Analógico ←", "0+" => "Analógico →", "1-" => "Analógico ↑", "1+" => "Analógico ↓", "2-" => "Direito ←", "2+" => "Direito →",
                "3-" => "Direito ↑", "3+" => "Direito ↓", "4+" => "LT", "5+" => "RT", _ => $"Eixo {v}",
            },
            _ => binding,
        };
    }
}
