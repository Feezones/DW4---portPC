using System.Net.Sockets;
using System.Text;

namespace DW4.Formats.Dolphin;

/// <summary>
/// Cliente mínimo do protocolo remoto do GDB, para o servidor GDB do Dolphin (GDBPort): parar, continuar e ler memória.
/// Só leitura: nunca escreve na memória nem nos registradores do jogo emulado.
/// </summary>
public sealed class GdbClient : IDisposable
{
    readonly TcpClient _tcp;
    readonly NetworkStream _s;

    GdbClient(TcpClient tcp)
    {
        _tcp = tcp;
        _s = tcp.GetStream();
    }

    public static GdbClient Connect(string host, int port, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (true)
        {
            try { return new GdbClient(new TcpClient(host, port) { NoDelay = true }); }
            catch (SocketException) when (DateTime.UtcNow < until) { Thread.Sleep(500); }
        }
    }

    public static string Checksum(string data) => (data.Aggregate(0, (sum, c) => sum + c) & 0xFF).ToString("x2");

    int _pushed = -1;
    /// <summary>Bytes inesperados recebidos no lugar do ack (para diagnóstico).</summary>
    public List<string> Stray { get; } = [];

    int ReadByte()
    {
        if (_pushed >= 0) { int p = _pushed; _pushed = -1; return p; }
        int c = _s.ReadByte();
        if (c < 0) throw new IOException("GDB: conexão fechada");
        return c;
    }

    void Send(string data)
    {
        var bytes = Encoding.ASCII.GetBytes($"${data}#{Checksum(data)}");
        for (int attempt = 0; attempt < 5; attempt++)
        {
            _s.Write(bytes);
            // Espera '+' (aceito) ou '-' (reenviar). Um '$' é o começo de um pacote sem ack antes: fica para o Receive.
            // Outros bytes são guardados para diagnóstico e ignorados.
            for (int skipped = 0; ; skipped++)
            {
                int c = ReadByte();
                if (c == '+') return;
                if (c == '-') break;
                if (c == '$') { _pushed = c; return; }
                if (Stray.Count < 64) Stray.Add($"0x{c:X2} depois de \"{data}\"");
                if (skipped > 4096) throw new IOException($"GDB: sem ack; bytes estranhos: {string.Join(", ", Stray)}");
            }
        }
        throw new IOException($"GDB: pacote \"{data}\" recusado 5 vezes; bytes estranhos: {string.Join(", ", Stray)}");
    }

    string Receive()
    {
        int c;
        while ((c = ReadByte()) != '$') { }
        var sb = new StringBuilder();
        while ((c = ReadByte()) != '#') sb.Append((char)c);
        ReadByte();
        ReadByte();
        _s.WriteByte((byte)'+');
        return sb.ToString();
    }

    public string Query(string data)
    {
        Send(data);
        return Receive();
    }

    /// <summary>Retoma a emulação (a resposta só vem na próxima parada).</summary>
    public void Continue() => Send("c");

    /// <summary>Interrompe a emulação (Ctrl-C) e espera a resposta de parada.</summary>
    public string Interrupt()
    {
        _s.WriteByte(3);
        return Receive();
    }

    public byte[] Read(uint address, int length)
    {
        var hex = Query($"m{address:x},{length:x}");
        if (hex.Length != 2 * length || hex.StartsWith('E')) throw new IOException($"GDB: leitura de 0x{address:X8} falhou ({hex[..Math.Min(hex.Length, 8)]})");
        return Convert.FromHexString(hex);
    }

    public void Dispose() => _tcp.Dispose();
}
