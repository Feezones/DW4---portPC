using DW4.Net;
using DW4.Server;

// Servidor do online do DW4. Configuração por variáveis de ambiente:
//   DW4_PORTA          porta UDP (padrão 7031)
//   DW4_API            endereço da DMO.API na mesma máquina (padrão http://127.0.0.1:5000/)
//   DW4_CHAVE_SERVICO  a mesma Dw4:ServiceKey da DMO.API (32+ letras)
//   DW4_DEV=1          teste local sem a API: o ticket "dev:Nome" entra como Nome (nunca em produção)
int port = int.TryParse(Environment.GetEnvironmentVariable("DW4_PORTA"), out var p) ? p : Protocol.DefaultPort;
IAccountApi api;
if (Environment.GetEnvironmentVariable("DW4_DEV") == "1")
{
    Console.WriteLine("MODO DE TESTE: sem DMO.API, tickets dev:Nome. Não use em produção.");
    api = new DevApi();
}
else
{
    var key = Environment.GetEnvironmentVariable("DW4_CHAVE_SERVICO") ?? "";
    if (key.Length < 32) { Console.Error.WriteLine("Defina DW4_CHAVE_SERVICO (a Dw4:ServiceKey da DMO.API, 32+ letras)."); return 1; }
    var baseUri = new Uri((Environment.GetEnvironmentVariable("DW4_API") ?? "http://127.0.0.1:5000/").TrimEnd('/') + "/");
    api = new DmoApi(baseUri, key);
}

var server = new GameServer(api);
if (!server.Start(port)) { Console.Error.WriteLine($"Não abriu a porta UDP {port}."); return 1; }
Console.WriteLine($"Servidor do DW4 na porta UDP {server.Port} (protocolo {Protocol.Version}). Ctrl+C para parar.");
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
while (!stop.IsCancellationRequested)
{
    server.Tick();
    Thread.Sleep(15);
}
server.Stop();
return 0;
