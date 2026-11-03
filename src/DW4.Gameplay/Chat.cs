namespace DW4.Gameplay;

/// <summary>Canal da mensagem (DECISÃO DO PORT, online): todos, só o grupo ou privada para um jogador.</summary>
public enum ChatChannel { Global, Party, Whisper }

/// <summary>
/// Chat do port (DECISÃO DO PORT, pedido do responsável; docs/decisoes-port-pc.md). Não existe no original. Cada linha é
/// "nome: mensagem - HH:MM" na hora local de quem lê; grupo e privada levam um prefixo ("[Grupo] ", "[De X] ", "[Para X] ").
/// <paramref name="To"/> é o destinatário da privada.
/// </summary>
public sealed record ChatMessage(string Sender, string Text, DateTime Time, ChatChannel Channel = ChatChannel.Global, string? To = null)
{
    /// <summary>Linha para quem lê (<paramref name="me"/> decide entre "De" e "Para" na privada).</summary>
    public string Format(string? me = null)
    {
        string prefix = Channel switch
        {
            ChatChannel.Party => "[Grupo] ",
            ChatChannel.Whisper when me != null && string.Equals(Sender, me, StringComparison.OrdinalIgnoreCase) => $"[Para {To}] ",
            ChatChannel.Whisper => $"[De {Sender}] ",
            _ => "",
        };
        return $"{prefix}{Sender}: {Text} - {Time:HH:mm}";
    }
}

/// <summary>O que o jogador digitou, já separado em canal, destinatário e texto.</summary>
public readonly record struct ChatCommand(ChatChannel Channel, string? To, string Text, string? Error)
{
    /// <summary>
    /// Lê a linha digitada. Comandos (DECISÃO DO PORT): "/g texto" geral, "/p texto" grupo, "/w nome texto" privada,
    /// "/r texto" responde a última privada recebida. Sem comando, vai para o canal atual da caixa (<paramref name="current"/>;
    /// na privada, para <paramref name="currentTo"/>).
    /// </summary>
    public static ChatCommand Parse(string line, ChatChannel current = ChatChannel.Global, string? currentTo = null, string? lastWhisperFrom = null)
    {
        var text = ChatLog.Clean(line);
        if (!text.StartsWith('/'))
            return current == ChatChannel.Whisper && string.IsNullOrEmpty(currentTo)
                ? new(current, null, text, "Escolha para quem: /w nome mensagem")
                : new(current, current == ChatChannel.Whisper ? currentTo : null, text, null);
        int sp = text.IndexOf(' ');
        string cmd = (sp < 0 ? text : text[..sp]).ToLowerInvariant(), rest = sp < 0 ? "" : text[(sp + 1)..].Trim();
        switch (cmd)
        {
            case "/g" or "/geral" or "/global": return new(ChatChannel.Global, null, rest, null);
            case "/p" or "/grupo" or "/party": return new(ChatChannel.Party, null, rest, null);
            case "/r":
                return lastWhisperFrom == null ? new(ChatChannel.Whisper, null, rest, "Ninguém mandou mensagem privada ainda.")
                                               : new(ChatChannel.Whisper, lastWhisperFrom, rest, null);
            case "/w" or "/m" or "/pm":
                int s2 = rest.IndexOf(' ');
                string to = s2 < 0 ? rest : rest[..s2], body = s2 < 0 ? "" : rest[(s2 + 1)..].Trim();
                return to.Length == 0 ? new(ChatChannel.Whisper, null, body, "Use /w nome mensagem") : new(ChatChannel.Whisper, to, body, null);
            default: return new(current, currentTo, text, $"Comando desconhecido: {cmd} (use /g, /p, /w nome ou /r)");
        }
    }
}

/// <summary>Contra spam (lado do servidor): no máximo <see cref="Max"/> mensagens por remetente a cada <see cref="Window"/>.</summary>
public sealed class ChatFlood
{
    public int Max { get; init; } = 5;
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(5);
    readonly Dictionary<string, Queue<DateTime>> _sent = new(StringComparer.OrdinalIgnoreCase);

    public bool Allow(string sender, DateTime nowUtc)
    {
        if (!_sent.TryGetValue(sender, out var q)) _sent[sender] = q = new();
        while (q.Count > 0 && nowUtc - q.Peek() >= Window) q.Dequeue();
        if (q.Count >= Max) return false;
        q.Enqueue(nowUtc);
        return true;
    }
}

/// <summary>
/// Canal por onde as mensagens chegam. PONTO DA FASE 6: um canal de rede troca o <see cref="LocalChatChannel"/> (o envio vai ao
/// servidor e o <see cref="Received"/> traz as mensagens de todos). Nada de rede antes da Fase 6 aprovada.
/// </summary>
public interface IChatChannel
{
    event Action<ChatMessage>? Received;
    void Send(string sender, string text);
    /// <summary>Envio com canal (geral, grupo, privada). Sem rede, só o geral volta para o próprio jogador.</summary>
    void Send(string sender, ChatCommand command) { if (command.Error == null && command.Channel == ChatChannel.Global) Send(sender, command.Text); }
}

/// <summary>Sem rede: a mensagem volta só para o próprio jogador.</summary>
public sealed class LocalChatChannel : IChatChannel
{
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;
    public event Action<ChatMessage>? Received;

    public void Send(string sender, string text)
    {
        var clean = ChatLog.Clean(text);
        if (clean.Length > 0) Received?.Invoke(new ChatMessage(sender, clean, Clock()));
    }
}

/// <summary>Histórico do chat: guarda as últimas linhas, mostra as mais recentes e some depois de um tempo sem uso.</summary>
public sealed class ChatLog
{
    public const int MaxLength = 120;
    readonly List<ChatMessage> _lines = [];

    public int Capacity { get; init; } = 50;
    public int Shown { get; init; } = 8;
    /// <summary>Segundos sem mensagem nova (e com a caixa fechada) até o histórico sumir.</summary>
    public float FadeAfter { get; init; } = 8f;

    public float Idle { get; private set; }
    public bool Typing { get; private set; }
    public bool Visible => Typing || (_lines.Count > 0 && Idle < FadeAfter);
    public IReadOnlyList<ChatMessage> All => _lines;
    /// <summary>As últimas <see cref="Shown"/> linhas, da mais antiga para a mais nova.</summary>
    public IEnumerable<string> Recent => _lines.Skip(Math.Max(0, _lines.Count - Shown)).Select(m => m.Format());

    public void Add(ChatMessage message)
    {
        _lines.Add(message);
        if (_lines.Count > Capacity) _lines.RemoveRange(0, _lines.Count - Capacity);
        Idle = 0;
    }

    /// <summary>Abre a caixa de digitação (o histórico reaparece).</summary>
    public void Open() { Typing = true; Idle = 0; }

    /// <summary>Fecha a caixa (Enter enviou ou Esc cancelou); o histórico conta o tempo para sumir a partir daqui.</summary>
    public void Close() { Typing = false; Idle = 0; }

    public void Tick(float dt)
    {
        if (!Typing) Idle += dt;
    }

    /// <summary>Uma linha só, sem espaços nas pontas e no máximo <see cref="MaxLength"/> letras.</summary>
    public static string Clean(string text)
    {
        var one = string.Join(' ', text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return one.Length > MaxLength ? one[..MaxLength] : one;
    }
}
