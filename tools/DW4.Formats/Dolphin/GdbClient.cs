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

    void Send(string data)
    {
        var bytes = Encoding.ASCII.GetBytes($"${data}#{Checksum(data)}");
        for (int attempt = 0; attempt < 5; attempt++)
        {
            _s.Write(bytes);
            int ack = _s.ReadByte();
            if (ack == '+') return;
            if (ack < 0) throw new IOException("GDB: conexão fechada");
        }
        throw new IOException("GDB: pacote recusado");
    }

    string Receive()
    {
        int c;
        while ((c = _s.ReadByte()) != '$') if (c < 0) throw new IOException("GDB: conexão fechada");
        var sb = new StringBuilder();
        while ((c = _s.ReadByte()) != '#') { if (c < 0) throw new IOException("GDB: conexão fechada"); sb.Append((char)c); }
        _s.ReadByte();
        _s.ReadByte();
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
