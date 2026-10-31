namespace DW4.Gameplay;

/// <summary>
/// Chat do port (DECISÃO DO PORT, pedido do responsável; docs/decisoes-port-pc.md). Não existe no original. Cada linha é
/// "nome: mensagem - HH:MM" na hora local de quem lê.
/// </summary>
public sealed record ChatMessage(string Sender, string Text, DateTime Time)
{
    public string Format() => $"{Sender}: {Text} - {Time:HH:mm}";
}

/// <summary>
/// Canal por onde as mensagens chegam. PONTO DA FASE 6: um canal de rede troca o <see cref="LocalChatChannel"/> (o envio vai ao
/// servidor e o <see cref="Received"/> traz as mensagens de todos). Nada de rede antes da Fase 6 aprovada.
/// </summary>
public interface IChatChannel
{
    event Action<ChatMessage>? Received;
    void Send(string sender, string text);
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
