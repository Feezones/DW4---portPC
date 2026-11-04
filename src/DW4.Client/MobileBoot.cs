using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace DW4.Client;

/// <summary>
/// Versão do celular (DECISÃO DO RESPONSÁVEL, 2026-11-04): sem launcher. Antes da abertura, esta tela entra com a conta do
/// launcher RE:DIGITAL (DMO.API: api/auth/login, refresh e game-ticket, como o AccountSession do launcher) e baixa ou atualiza
/// os dados do jogo pelo mesmo CDN do launcher (manifesto assinado, blocos de 4 MB pelo SHA-256). Depois segue para o menu
/// de sempre com <see cref="Session.UseLogin"/>, e o ticket vai para o servidor do DW4 como no PC.
/// </summary>
public partial class MobileBoot : Control
{
    public const string Scene = "res://MobileBoot.tscn";
    public static bool Done { get; private set; }

    const string ApiBase = "https://redigital-api.felipe-brag99.workers.dev/";
    const string AccountFile = "user://conta.cfg";
    const string ManifestTest = "/games/dw4/manifest.json";

    LineEdit _user = null!, _pass = null!;
    CheckBox _remember = null!;
    Button _enter = null!, _go = null!;
    Label _status = null!, _detail = null!;
    ProgressBar _bar = null!;
    VBoxContainer _form = null!;
    MobileUpdater? _updater;
    System.Action? _goAction;
    string _account = "";

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(new ColorRect { Color = new Color(0.04f, 0.05f, 0.09f), AnchorRight = 1, AnchorBottom = 1 });
        // Formulário no alto da tela: o teclado do Android cobre a metade de baixo com o celular deitado.
        var top = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        top.AddThemeConstantOverride("margin_top", 40);
        top.AddThemeConstantOverride("margin_left", 80);
        top.AddThemeConstantOverride("margin_right", 80);
        AddChild(top);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 14);
        top.AddChild(col);
        var title = new Label { Text = "Digimon World 4", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 56);
        col.AddChild(title);

        _form = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(720, 0) };
        _form.AddThemeConstantOverride("separation", 12);
        col.AddChild(_form);
        _form.AddChild(Small("Entre com a sua conta do RE:DIGITAL (a mesma do launcher)."));
        _user = new LineEdit { PlaceholderText = "Usuário", CustomMinimumSize = new Vector2(0, 72) };
        _pass = new LineEdit { PlaceholderText = "Senha", Secret = true, CustomMinimumSize = new Vector2(0, 72) };
        foreach (var e in new[] { _user, _pass }) e.AddThemeFontSizeOverride("font_size", 34);
        _user.TextSubmitted += _ => _pass.GrabFocus();
        _pass.TextSubmitted += _ => Login();
        _form.AddChild(_user);
        _form.AddChild(_pass);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        _remember = new CheckBox { Text = "Lembrar de mim", ButtonPressed = true };
        _remember.AddThemeFontSizeOverride("font_size", 30);
        _enter = new Button { Text = "Entrar", CustomMinimumSize = new Vector2(260, 72), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _enter.AddThemeFontSizeOverride("font_size", 34);
        _enter.Pressed += Login;
        row.AddChild(_remember);
        row.AddChild(_enter);
        _form.AddChild(row);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 32);
        col.AddChild(_status);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(900, 36), SizeFlagsHorizontal = SizeFlags.ShrinkCenter, MaxValue = 1, Visible = false };
        col.AddChild(_bar);
        _detail = Small("");
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_detail);
        _go = new Button { Text = "Baixar", CustomMinimumSize = new Vector2(420, 80), SizeFlagsHorizontal = SizeFlags.ShrinkCenter, Visible = false };
        _go.AddThemeFontSizeOverride("font_size", 34);
        _go.Pressed += () => _goAction?.Invoke();
        col.AddChild(_go);
        var version = Small($"app {ProjectSettings.GetSetting("application/config/version", "").AsString()}");
        version.HorizontalAlignment = HorizontalAlignment.Right;
        col.AddChild(version);

        DisplayServer.ScreenSetKeepOn(true);
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") { _form.Visible = false; CheckUpdate(); return; }
        TryRemembered();
    }

    static Label Small(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(0.8f, 0.83f, 0.9f) };
        l.AddThemeFontSizeOverride("font_size", 26);
        return l;
    }

    void Busy(bool busy, string status)
    {
        _status.Text = status;
        _enter.Disabled = busy;
        _user.Editable = _pass.Editable = !busy;
    }

    // ---- Conta (DMO.API) ----

    async void TryRemembered()
    {
        var cfg = new ConfigFile();
        if (cfg.LoadEncryptedPass(AccountFile, OS.GetUniqueId()) != Error.Ok) { _status.Text = ""; return; }
        string user = cfg.GetValue("conta", "usuario", "").AsString(), refresh = cfg.GetValue("conta", "refresh", "").AsString();
        _user.Text = user;
        if (refresh.Length == 0) return;
        Busy(true, $"Entrando como {user}…");
        var r = await Call("api/auth/refresh", new Dictionary<string, object> { ["refreshToken"] = refresh }, null);
        if (r.Ok) { await AfterLogin(r); return; }
        if (r.Status is 400 or 401) { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AccountFile)); Busy(false, "A sessão expirou. Entre de novo."); }
        else Busy(false, r.Message);
    }

    async void Login()
    {
        if (_user.Text.Trim().Length == 0 || _pass.Text.Length == 0) { _status.Text = "Informe usuário e senha."; return; }
        DisplayServer.VirtualKeyboardHide();
        Busy(true, "Entrando…");
        var r = await Call("api/auth/login", new Dictionary<string, object> { ["username"] = _user.Text.Trim(), ["password"] = _pass.Text }, null);
        _pass.Text = "";
        if (!r.Ok) { Busy(false, r.Status == 429 ? "Muitas tentativas. Espere um pouco e tente de novo." : r.Message.Length > 0 ? r.Message : "Usuário ou senha inválidos."); return; }
        await AfterLogin(r);
    }

    async Task AfterLogin(ApiResult login)
    {
        string access = login.Str("accessToken"), refresh = login.Str("refreshToken");
        string user = login.Str("username") is { Length: > 0 } u ? u : _user.Text.Trim();
        var cfg = new ConfigFile();
        if (_remember.ButtonPressed && refresh.Length > 0)
        {
            cfg.SetValue("conta", "usuario", user);
            cfg.SetValue("conta", "refresh", refresh);
            cfg.SaveEncryptedPass(AccountFile, OS.GetUniqueId());
        }
        else DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AccountFile));
        Busy(true, "Abrindo a sessão do jogo…");
        var t = await Call("api/auth/game-ticket", null, access);
        if (!t.Ok) { Busy(false, t.Message.Length > 0 ? t.Message : "Não foi possível abrir a sessão do jogo."); return; }
        _account = t.Str("username") is { Length: > 0 } tu ? tu : user;
        Session.UseLogin(_account, t.Str("ticket"));
        _form.Visible = false;
        CheckUpdate();
    }

    readonly record struct ApiResult(bool Ok, int Status, string Message, JsonElement Data)
    {
        public string Str(string name) =>
            Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    }

    async Task<ApiResult> Call(string path, object? body, string? bearer)
    {
        var req = new HttpRequest { Timeout = 30 };
        AddChild(req);
        try
        {
            var headers = new List<string> { "Content-Type: application/json", "Accept: application/json", "User-Agent: DW4-Android/1" };
            if (bearer != null) headers.Add("Authorization: Bearer " + bearer);
            var err = req.Request(ApiBase + path, headers.ToArray(), Godot.HttpClient.Method.Post, body == null ? "" : JsonSerializer.Serialize(body));
            if (err != Error.Ok) return new(false, 0, "Sem conexão com o servidor da conta.", default);
            var r = await ToSignal(req, HttpRequest.SignalName.RequestCompleted);
            if (r[0].AsInt64() != (long)HttpRequest.Result.Success) return new(false, 0, "Sem conexão com o servidor da conta. Confira a internet.", default);
            int status = r[1].AsInt32();
            try
            {
                using var doc = JsonDocument.Parse(r[3].AsByteArray());
                var root = doc.RootElement;
                bool ok = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                string msg = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                if (msg.Length == 0 && root.TryGetProperty("errors", out var es) && es.ValueKind == JsonValueKind.Array)
                    msg = string.Join(" ", es.EnumerateArray().Select(x => x.ToString()));
                var data = root.TryGetProperty("data", out var d) ? d.Clone() : default;
                return new(ok && status < 400, status, msg, data);
            }
            catch (JsonException)
            {
                return new(false, status, status == 401 ? "Sessão expirada. Entre de novo." : $"O servidor da conta respondeu {status}.", default);
            }
        }
        finally { req.QueueFree(); }
    }

    // ---- Dados do jogo (CDN) ----

    void CheckUpdate()
    {
        _go.Visible = false;
        _status.Text = "Procurando atualização…";
        _detail.Text = "";
        // DEPURAÇÃO (só build Debug): DW4_CDN=host:porta usa um CDN de teste sem TLS, assinado pela chave PEM de DW4_CDN_CHAVE.
        _updater = OS.IsDebugBuild() && OS.GetEnvironment("DW4_CDN") is { Length: > 0 } cdn
            ? new MobileUpdater(OS.GetEnvironment("DW4_CDN_PASTA") is { Length: > 0 } dir ? dir : OS.GetUserDataDir(), cdn, ManifestTest, testKey: Godot.FileAccess.GetFileAsString(OS.GetEnvironment("DW4_CDN_CHAVE")))
            : new MobileUpdater(OS.GetUserDataDir());
        _updater.StartPlan();
    }

    void StartDownload()
    {
        _go.Visible = false;
        _bar.Visible = true;
        _updater?.StartDownload();
    }

    public override void _Process(double delta)
    {
        if (_updater is not { } u) return;
        if (u.Error is { } error)
        {
            _updater = null;
            _bar.Visible = false;
            _status.Text = "Não deu para atualizar o jogo.";
            _detail.Text = error;
            _go.Text = "Tentar de novo";
            _go.Visible = true;
            _goAction = CheckUpdate;
            if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") { GD.Print($"[teste] erro: {error}"); GetTree().Quit(1); }
            return;
        }
        switch (u.Phase)
        {
            case MobileUpdater.Stage.Planning:
                _detail.Text = u.Note;
                break;
            case MobileUpdater.Stage.Ready when !_go.Visible && !_bar.Visible:
                if (u.DownloadBytes == 0 && u.FilesToWrite == 0) { Finish(u); return; }
                _status.Text = u.FirstInstall ? "Primeira vez: é preciso baixar os dados do jogo." : $"Atualização {u.Version} disponível.";
                _detail.Text = $"{Fmt(u.DownloadBytes)} para baixar ({u.FilesToWrite} arquivos). Use o Wi-Fi, se puder.";
                _goAction = StartDownload;
                _go.Text = u.DownloadBytes > 20 << 20 ? "Baixar agora" : "Atualizar";
                _go.Visible = true;
                if (u.DownloadBytes <= 20 << 20 || OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") StartDownload(); // atualização pequena: sem perguntar
                break;
            case MobileUpdater.Stage.Downloading:
                long got = Interlocked.Read(ref u.GotBytes);
                _status.Text = $"Baixando os dados do jogo ({u.Version})…";
                _bar.Value = u.DownloadBytes > 0 ? (double)got / u.DownloadBytes : 0;
                _detail.Text = $"{Fmt(got)} de {Fmt(u.DownloadBytes)}  ·  {u.FilesDone}/{u.FilesToWrite} arquivos  ·  {Fmt((long)u.Speed)}/s";
                break;
            case MobileUpdater.Stage.Finished:
                Finish(u);
                break;
        }
    }

    void Finish(MobileUpdater u)
    {
        _updater = null;
        DataPaths.MobileVersion = u.Version;
        Done = true;
        DisplayServer.ScreenSetKeepOn(false);
        GD.Print($"[celular] dados {u.Version} prontos em {DataPaths.MobileRoot}; conta {_account}");
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") { GD.Print($"[teste] atualizado: {u.FilesDone} arquivos, {u.GotBytes} bytes baixados"); GetTree().Quit(); return; }
        GetTree().ChangeSceneToFile(Session.MenuScene);
    }

    static string Fmt(long b) => b >= 1L << 30 ? $"{b / (double)(1L << 30):0.00} GB" : b >= 1 << 20 ? $"{b / (double)(1 << 20):0.0} MB" : $"{b / 1024.0:0} KB";
}

/// <summary>
/// Baixa os dados do jogo do CDN do launcher, no formato do ReDigital.Core (Manifest/GameManifest.cs, formato 1): o manifesto é
/// assinado (RSA-SHA256, PKCS#1 v1.5, a mesma chave pública do launcher) e cada arquivo é feito de blocos guardados pelo próprio
/// SHA-256. Só entram os arquivos de "dados/". Blocos repetidos (a mesma música em várias fases) são copiados do que já está no
/// celular em vez de baixados de novo. Um arquivo só conta como pronto depois de inteiro (vai para .part e é renomeado), e a
/// lista dos prontos (dados/.instalados) deixa continuar de onde parou se a conexão cair ou o jogo fechar.
/// </summary>
public sealed class MobileUpdater
{
    public const string CdnHost = "redigital-cdn.felipe-brag99.workers.dev";
    public const string ManifestPath = "/games/dw4/manifest.json";
    const int Workers = 6;

    // Mesma chave do launcher (src/ReDigital.Launcher/Assets/publisher.public.xml), em PEM.
    const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAu/p4aaoQImwyTMXYDxWf
        j172VKSY0dvYzxDbdod4MjfPtfgSSY79mjzXLXfNK36UoGlbhfokHIy5iVsSGNtM
        Z2C5PdS+kEkKQQyQLyWeZdebmbPjd6vLPMADGfktmYIjxGtknu7S9tH5CDrGimqX
        U3RYzf7GK3nWi2tULUAoVwPJ0R7gJq8NGA0j2ltAzGyAhJNJxba6EZJmIlwX4A7F
        pqLB94ivpAHnfjhTaI6Sv+nwbf0z1HO8iFlvIBvPMxJ3IArkd6cbhdi1oFjaaSPJ
        DEnNX8bKlToN06RWxDfUV9mSYKdCqJfLIE4ztfXUfT/k2JIkloBRndrNBWbWiXn/
        9cXzEuKVtojH3ypJfZBM3zV3PXu6m11e619ny4Ok520CtoWhJ28HLXLLkZG3oCjg
        4Xt6AcEeFimBAsqz3B8pCK5TV0gaftNX1whbzHN6FUJLHUfPGaG+c50sjrrbKeUi
        FueadlJ8i90KvnMZeAxMjp67l4dYdCP7eLhPeBNaB8MRAgMBAAE=
        -----END PUBLIC KEY-----
        """;

    public enum Stage { Planning, Ready, Downloading, Finished }

    sealed record FileEntry(string Path, long Size, string Sha, string[] Chunks);

    readonly string _root; // pasta do user:// (os caminhos do manifesto começam com "dados/")
    readonly string _host;
    readonly int _port = 443;
    readonly bool _tls = true;
    readonly string? _testKey;
    readonly string _manifestPath;
    public volatile Stage Phase = Stage.Planning;
    public volatile string? Error;
    public volatile string Note = "";
    public string Version = "";
    public bool FirstInstall;
    public long DownloadBytes;
    public long GotBytes;
    public int FilesToWrite, FilesDone;
    public double Speed;

    int _chunkSize;
    string _chunkPath = "chunks";
    readonly List<FileEntry> _todo = [];
    readonly Dictionary<string, (string File, long Offset, int Length)> _local = [];
    readonly object _lock = new();
    StreamWriter? _log;
    byte[] _manifestBytes = [];

    public MobileUpdater(string userDir, string host = CdnHost, string manifestPath = ManifestPath, string? testKey = null)
    {
        _root = userDir;
        _host = host;
        _manifestPath = manifestPath;
        if (testKey != null)
        {
            // CDN de teste (só build Debug): http://host:porta e a chave de teste no lugar da do RE:DIGITAL.
            var parts = host.Split(':');
            _host = parts[0];
            _port = parts.Length > 1 ? int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 80;
            _tls = false;
            _testKey = testKey;
        }
    }

    CdnConnection Connect() => new(_host, _port, _tls);

    string Full(string rel) => Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar));
    string LogFile => Full("dados/.instalados");
    string ManifestCopy => Full("dados/.manifesto.json");

    public void StartPlan() => new Thread(() => Guard(Plan)) { IsBackground = true, Name = "dw4-plano" }.Start();

    public void StartDownload()
    {
        Phase = Stage.Downloading;
        new Thread(() => Guard(Download)) { IsBackground = true, Name = "dw4-download" }.Start();
    }

    void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or JsonException or InvalidOperationException)
        {
            GD.PushWarning($"[atualizar] {e}");
            Error = e is IOException io && io.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                ? "Sem espaço no celular para os dados do jogo." : e.Message;
        }
    }

    void Plan()
    {
        Note = "Lendo o manifesto…";
        var conn = Connect();
        byte[] manifest, sig;
        try
        {
            manifest = conn.Get(_manifestPath);
            sig = conn.Get(_manifestPath + ".sig");
        }
        finally { conn.Close(); }
        Verify(manifest, sig, _testKey ?? PublicKeyPem);
        _manifestBytes = manifest;
        var files = Parse(manifest);

        // O que já está no celular: a lista dos prontos (caminho → sha) e o tamanho no disco.
        var done = new Dictionary<string, string>(StringComparer.Ordinal);
        if (File.Exists(LogFile))
            foreach (var line in File.ReadLines(LogFile))
                if (line.Length > 65 && line[64] == ' ') done[line[65..]] = line[..64];
        FirstInstall = done.Count == 0;
        Note = "Conferindo os arquivos…";
        var needChunks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            var full = Full(f.Path);
            if (done.TryGetValue(f.Path, out var sha) && sha == f.Sha && new FileInfo(full) is { Exists: true } fi && fi.Length == f.Size)
            {
                AddLocal(f, full);
                continue;
            }
            _todo.Add(f);
        }
        foreach (var f in _todo)
            for (int i = 0; i < f.Chunks.Length; i++)
                if (!_local.ContainsKey(f.Chunks[i]) && needChunks.Add(f.Chunks[i])) DownloadBytes += ChunkLength(f, i);
        FilesToWrite = _todo.Count;
        // Arquivos de dados que saíram do manifesto ficam no disco por enquanto (não atrapalham o jogo).
        Phase = Stage.Ready;
    }

    static void Verify(byte[] manifest, byte[] sigFile, string pem)
    {
        byte[] sig;
        try { sig = Convert.FromBase64String(Encoding.ASCII.GetString(sigFile).Trim()); }
        catch (FormatException) { throw new InvalidOperationException("Assinatura do manifesto inválida."); }
        var key = new CryptoKey();
        if (key.LoadFromString(pem.Replace("\r", ""), publicOnly: true) != Godot.Error.Ok) throw new InvalidOperationException("Chave pública inválida.");
        var h = new HashingContext();
        h.Start(HashingContext.HashType.Sha256);
        h.Update(manifest);
        if (!new Crypto().Verify(HashingContext.HashType.Sha256, h.Finish(), sig, key))
            throw new InvalidOperationException("O manifesto do CDN não tem uma assinatura válida do RE:DIGITAL.");
    }

    List<FileEntry> Parse(byte[] manifest)
    {
        using var doc = JsonDocument.Parse(manifest);
        var o = doc.RootElement;
        if (o.GetProperty("format").GetInt32() != 1) throw new FormatException("Formato de manifesto não suportado; atualize o app.");
        Version = o.GetProperty("version").GetString() ?? "";
        _chunkSize = o.GetProperty("chunkSize").GetInt32();
        if (_chunkSize < 64 * 1024) throw new FormatException("Manifesto com tamanho de bloco inválido.");
        if (o.TryGetProperty("chunkPath", out var cp) && cp.GetString() is { Length: > 0 } p) _chunkPath = p.Trim('/');
        var list = new List<FileEntry>();
        foreach (var f in o.GetProperty("files").EnumerateArray())
        {
            var path = f.GetProperty("path").GetString() ?? "";
            if (!path.StartsWith("dados/", StringComparison.Ordinal)) continue; // o DW4.exe e o .NET do PC não vêm
            if (path.Contains("..", StringComparison.Ordinal) || path.Contains('\\') || path.StartsWith('/')) throw new FormatException("Caminho inválido no manifesto: " + path);
            var chunks = f.GetProperty("chunks").EnumerateArray().Select(c => c.GetString() ?? "").ToArray();
            long size = f.GetProperty("size").GetInt64();
            if (chunks.Length != (size + _chunkSize - 1) / _chunkSize || chunks.Any(c => c.Length != 64)) throw new FormatException("Blocos inválidos no manifesto: " + path);
            list.Add(new FileEntry(path, size, f.GetProperty("sha256").GetString() ?? "", chunks));
        }
        return list;
    }

    int ChunkLength(FileEntry f, int i) => (int)Math.Min(_chunkSize, f.Size - (long)i * _chunkSize);

    void AddLocal(FileEntry f, string full)
    {
        for (int i = 0; i < f.Chunks.Length; i++) _local.TryAdd(f.Chunks[i], (full, (long)i * _chunkSize, ChunkLength(f, i)));
    }

    void Download()
    {
        Directory.CreateDirectory(Full("dados"));
        _log = new StreamWriter(new FileStream(LogFile, FileMode.Append, System.IO.FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
        var queue = new ConcurrentQueue<FileEntry>(_todo.OrderBy(f => f.Path, StringComparer.Ordinal));
        var start = DateTime.UtcNow;
        var threads = Enumerable.Range(0, Workers).Select(n => new Thread(() =>
        {
            var conn = Connect();
            try
            {
                while (Error == null && queue.TryDequeue(out var f))
                {
                    try { WriteFile(conn, f); }
                    catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
                    {
                        Error ??= e is IOException io && io.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
                            ? "Sem espaço no celular para os dados do jogo." : $"{f.Path}: {e.Message}";
                    }
                    Interlocked.Increment(ref FilesDone);
                    var secs = (DateTime.UtcNow - start).TotalSeconds;
                    if (secs > 1) Speed = Interlocked.Read(ref GotBytes) / secs;
                }
            }
            finally { conn.Close(); }
        }) { IsBackground = true, Name = $"dw4-bloco{n}" }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        lock (_lock) { _log.Dispose(); _log = null; }
        if (Error != null) return;
        File.WriteAllBytes(ManifestCopy, _manifestBytes);
        Phase = Stage.Finished;
    }

    void WriteFile(CdnConnection conn, FileEntry f)
    {
        var full = Full(f.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var part = full + ".part";
        using (var output = new FileStream(part, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 1 << 16))
        {
            for (int i = 0; i < f.Chunks.Length; i++)
            {
                var hash = f.Chunks[i];
                int len = ChunkLength(f, i);
                byte[]? data = null;
                (string File, long Offset, int Length) src;
                bool have;
                lock (_lock) have = _local.TryGetValue(hash, out src);
                if (have && src.Length == len) data = ReadLocal(src.File, src.Offset, len, hash);
                if (data == null)
                {
                    data = conn.Get($"{_manifestPath[..(_manifestPath.LastIndexOf('/') + 1)]}{_chunkPath}/{hash[..2]}/{hash}");
                    if (data.Length != len || Sha(data) != hash) throw new IOException("bloco corrompido no download; tente de novo.");
                    Interlocked.Add(ref GotBytes, len);
                }
                output.Write(data, 0, data.Length);
            }
        }
        File.Move(part, full, overwrite: true);
        lock (_lock)
        {
            AddLocal(f, full);
            _log!.WriteLine($"{f.Sha} {f.Path}");
            _log.Flush();
        }
    }

    static byte[]? ReadLocal(string file, long offset, int len, string hash)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite);
            fs.Position = offset;
            var buf = new byte[len];
            int got = 0;
            while (got < len) { int n = fs.Read(buf, got, len - got); if (n <= 0) return null; got += n; }
            return Sha(buf) == hash ? buf : null;
        }
        catch (IOException) { return null; }
    }

    static string Sha(byte[] data)
    {
        var h = new HashingContext();
        h.Start(HashingContext.HashType.Sha256);
        h.Update(data);
        return Convert.ToHexString(h.Finish()).ToLowerInvariant();
    }
}

/// <summary>
/// Uma conexão HTTPS mantida aberta com o CDN (Godot HTTPClient, TLS da própria Godot), para baixar milhares de blocos sem
/// abrir uma conexão por bloco. Usada só de dentro de uma thread de download; refaz a conexão e tenta de novo quando ela cai.
/// </summary>
public sealed class CdnConnection(string host, int port = 443, bool tls = true)
{
    Godot.HttpClient? _c;

    public byte[] Get(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return TryGet(path); }
            catch (IOException) when (attempt < 5)
            {
                Close();
                Thread.Sleep(1000 * (attempt + 1));
            }
        }
    }

    public void Close()
    {
        _c?.Close();
        _c = null;
    }

    void Ensure()
    {
        if (_c != null && _c.GetStatus() == Godot.HttpClient.Status.Connected) return;
        Close();
        var c = new Godot.HttpClient { ReadChunkSize = 1 << 18 };
        if (c.ConnectToHost(host, port, tls ? TlsOptions.Client() : null) != Godot.Error.Ok) throw new IOException("sem conexão com o servidor de download");
        var limit = DateTime.UtcNow.AddSeconds(20);
        while (c.GetStatus() is Godot.HttpClient.Status.Resolving or Godot.HttpClient.Status.Connecting)
        {
            c.Poll();
            if (DateTime.UtcNow > limit) throw new IOException("o servidor de download não respondeu");
            Thread.Sleep(5);
        }
        if (c.GetStatus() != Godot.HttpClient.Status.Connected) throw new IOException($"sem conexão com o servidor de download ({c.GetStatus()})");
        _c = c;
    }

    byte[] TryGet(string path)
    {
        Ensure();
        var c = _c!;
        if (c.Request(Godot.HttpClient.Method.Get, path, ["User-Agent: DW4-Android/1", "Accept-Encoding: identity"]) != Godot.Error.Ok)
            throw new IOException("falha ao pedir " + path);
        var limit = DateTime.UtcNow.AddSeconds(60);
        while (c.GetStatus() == Godot.HttpClient.Status.Requesting)
        {
            c.Poll();
            if (DateTime.UtcNow > limit) throw new IOException("tempo esgotado");
            Thread.Sleep(1);
        }
        if (!c.HasResponse()) throw new IOException($"sem resposta ({c.GetStatus()})");
        int code = c.GetResponseCode();
        var body = new MemoryStream();
        while (c.GetStatus() == Godot.HttpClient.Status.Body)
        {
            c.Poll();
            var part = c.ReadResponseBodyChunk();
            if (part.Length == 0) { if (DateTime.UtcNow > limit) throw new IOException("tempo esgotado"); Thread.Sleep(1); }
            else { body.Write(part); limit = DateTime.UtcNow.AddSeconds(60); }
        }
        if (c.GetStatus() != Godot.HttpClient.Status.Connected) Close(); // o servidor fechou: a próxima abre outra
        if (code == 404) throw new InvalidOperationException("arquivo não encontrado no servidor: " + path);
        if (code != 200) throw new IOException($"o servidor respondeu {code}");
        return body.ToArray();
    }
}
