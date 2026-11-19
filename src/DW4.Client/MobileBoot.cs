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

    const string ApiBase = "https://api.redigitalon.com/";
    const string AccountFile = "user://conta.cfg";
    const string ManifestTest = "/games/dw4/manifest.json";

    LineEdit _user = null!, _pass = null!;
    CheckBox _remember = null!;
    Button _enter = null!, _go = null!, _skip = null!;
    Label _status = null!, _detail = null!;
    ProgressBar _bar = null!;
    VBoxContainer _form = null!;
    MobileUpdater? _updater;
    System.Action? _goAction;
    string _account = "";

    // ---- Visual (pedido do usuário, 2026-11-04: "mais amigável e na estética do jogo") ----
    // As janelas do DW4: fundo azul-marinho, borda ciano, títulos dourados e botões vinho com borda lilás (como as vagas do
    // menu). O fundo imita o túnel do título. Antes do primeiro download não há nada do jogo no celular; depois, o logotipo
    // original (fases/stagis10/titulo) entra no lugar do título escrito.
    internal static readonly Color Navy = new(0.05f, 0.06f, 0.16f, 0.94f), Cyan = new(0.35f, 0.85f, 1f), Gold = new(1f, 0.85f, 0.35f),
        Lilac = new(0.55f, 0.5f, 1f), Wine = new(0.32f, 0.07f, 0.16f), Soft = new(0.78f, 0.82f, 0.95f);

    internal static StyleBoxFlat Box(Color bg, Color border, int width = 3, int radius = 12, int margin = 14)
    {
        var b = new StyleBoxFlat { BgColor = bg, BorderColor = border, CornerDetail = 8, AntiAliasing = true };
        b.SetBorderWidthAll(width);
        b.SetCornerRadiusAll(radius);
        b.SetContentMarginAll(margin);
        return b;
    }

    internal static Theme MakeTheme()
    {
        var t = new Theme { DefaultFontSize = 30 };
        t.SetColor("font_color", "Label", Colors.White);
        t.SetColor("font_outline_color", "Label", new Color(0.02f, 0.03f, 0.12f));
        t.SetConstant("outline_size", "Label", 6);
        foreach (var (state, bg, border) in new[] { ("normal", Wine, Lilac), ("hover", Wine.Lightened(0.08f), Gold), ("pressed", Wine.Lightened(0.2f), Gold),
            ("focus", new Color(0, 0, 0, 0), Gold), ("disabled", Wine.Darkened(0.4f), Lilac.Darkened(0.5f)) })
            t.SetStylebox(state, "Button", state == "focus" ? Box(bg, border, 3, 12, 10) : Box(bg, border, 3, 12, 10));
        t.SetColor("font_color", "Button", Colors.White);
        t.SetColor("font_hover_color", "Button", Gold);
        t.SetColor("font_pressed_color", "Button", Gold);
        t.SetColor("font_focus_color", "Button", Gold);
        t.SetColor("font_disabled_color", "Button", new Color(1, 1, 1, 0.4f));
        t.SetStylebox("normal", "LineEdit", Box(new Color(0.02f, 0.03f, 0.1f, 0.95f), Lilac, 2, 10, 12));
        t.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), Gold, 3, 10, 12));
        t.SetStylebox("read_only", "LineEdit", Box(new Color(0.02f, 0.03f, 0.1f, 0.6f), Lilac.Darkened(0.4f), 2, 10, 12));
        t.SetColor("font_color", "LineEdit", Colors.White);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.7f, 0.72f, 0.9f, 0.6f));
        t.SetColor("caret_color", "LineEdit", Gold);
        t.SetColor("font_color", "CheckBox", Soft);
        t.SetColor("font_hover_color", "CheckBox", Gold);
        t.SetColor("font_pressed_color", "CheckBox", Soft);
        t.SetColor("font_hover_pressed_color", "CheckBox", Gold);
        var empty = new StyleBoxEmpty();
        foreach (var st in new[] { "normal", "hover", "pressed", "focus", "hover_pressed" }) t.SetStylebox(st, "CheckBox", empty);
        t.SetStylebox("background", "ProgressBar", Box(new Color(0.02f, 0.03f, 0.1f, 0.95f), Cyan, 2, 10, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Cyan.Darkened(0.15f), Cyan.Lightened(0.3f), 0, 10, 0));
        t.SetColor("font_color", "ProgressBar", Colors.White);
        t.SetColor("font_outline_color", "ProgressBar", new Color(0.02f, 0.03f, 0.12f));
        t.SetConstant("outline_size", "ProgressBar", 6);
        return t;
    }

    /// <summary>Fundo: o túnel azul do título (octógonos que vêm na direção da tela e raios girando devagar).</summary>
    internal sealed partial class Tunnel : Control
    {
        float _t;
        public override void _Process(double delta) { _t += (float)delta; QueueRedraw(); }

        public override void _Draw()
        {
            var size = Size;
            var c = size / 2;
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0.13f, 0.17f, 0.55f));
            float max = size.Length() * 0.62f;
            // brilho no centro
            for (int i = 8; i >= 1; i--) DrawCircle(c, max * 0.08f * i, new Color(0.45f, 0.55f, 1f, 0.07f));
            DrawCircle(c, max * 0.06f, new Color(0.95f, 0.97f, 1f, 0.9f));
            float spin = _t * 0.08f;
            for (int k = 0; k < 8; k++)
            {
                var dir = Vector2.Right.Rotated(spin + k * Mathf.Tau / 8);
                DrawLine(c + dir * max * 0.06f, c + dir * max, new Color(0.03f, 0.04f, 0.18f, 0.8f), 12);
            }
            // octógonos: a fase (0..1) cresce com o tempo e o tamanho cresce rápido perto da borda (perspectiva)
            for (int k = 0; k < 9; k++)
            {
                float f = (k / 9f + _t * 0.05f) % 1f;
                float r = max * 0.05f / (1.05f - f);
                if (r > max) continue;
                var pts = new Vector2[9];
                for (int i = 0; i <= 8; i++) pts[i] = c + Vector2.Right.Rotated(spin + Mathf.Pi / 8 + i * Mathf.Tau / 8) * r;
                DrawPolyline(pts, new Color(0.85f, 0.95f, 1f, 0.15f + 0.5f * f), 1.5f + 4 * f, true);
            }
            // escurece as bordas para o painel ler bem
            DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0.08f, 0.2f));
        }
    }

    Label _hello = null!, _tip = null!;
    bool _fakeDownload;
    double _tipTime;
    int _tipIndex;

    static readonly string[] Tips =
    [
        "Dica: o analógico aparece onde você encosta o dedo, na metade esquerda da tela.",
        "Dica: segure ATQ para carregar o golpe.",
        "Dica: arraste um disco da janela de ITENS até um atalho para usar com um toque.",
        "Dica: com um controle Bluetooth, os botões da tela somem sozinhos.",
        "Dica: chame os amigos para a party pelo launcher do PC ou pelo menu do jogo.",
        "Dica: a sua conta é a mesma do PC. As vagas e o progresso são os mesmos.",
    ];

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = MakeTheme();
        AddChild(new Tunnel { AnchorRight = 1, AnchorBottom = 1, MouseFilter = MouseFilterEnum.Ignore });
        // Tudo no alto da tela: o teclado do Android cobre a metade de baixo com o celular deitado.
        var top = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        top.AddThemeConstantOverride("margin_top", 24);
        top.AddThemeConstantOverride("margin_left", 60);
        top.AddThemeConstantOverride("margin_right", 60);
        AddChild(top);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        top.AddChild(col);
        col.AddChild(Logo());

        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(940, 0) };
        var style = Box(Navy, Cyan, 3, 16, 26);
        style.ShadowColor = new Color(0, 0, 0, 0.5f);
        style.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", style);
        col.AddChild(panel);
        var inner = new VBoxContainer();
        inner.AddThemeConstantOverride("separation", 12);
        panel.AddChild(inner);

        _hello = new Label { Text = "Bem-vindo, Tamer!", HorizontalAlignment = HorizontalAlignment.Center };
        _hello.AddThemeFontSizeOverride("font_size", 40);
        _hello.AddThemeColorOverride("font_color", Gold);
        inner.AddChild(_hello);

        _form = new VBoxContainer();
        _form.AddThemeConstantOverride("separation", 12);
        inner.AddChild(_form);
        var hint = Small("Entre com a sua conta RE:DIGITAL, a mesma do launcher do PC.");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        _form.AddChild(hint);
        _user = new LineEdit { PlaceholderText = "Usuário", CustomMinimumSize = new Vector2(0, 70) };
        _pass = new LineEdit { PlaceholderText = "Senha", Secret = true, CustomMinimumSize = new Vector2(0, 70) };
        foreach (var e in new[] { _user, _pass }) e.AddThemeFontSizeOverride("font_size", 32);
        _user.TextSubmitted += _ => _pass.GrabFocus();
        _pass.TextSubmitted += _ => Login();
        _form.AddChild(_user);
        var passRow = new HBoxContainer();
        passRow.AddThemeConstantOverride("separation", 10);
        _pass.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        passRow.AddChild(_pass);
        var show = new Button { Text = "Ver", ToggleMode = true, CustomMinimumSize = new Vector2(110, 70), FocusMode = FocusModeEnum.None };
        show.AddThemeFontSizeOverride("font_size", 26);
        show.Toggled += on => { _pass.Secret = !on; show.Text = on ? "Ocultar" : "Ver"; };
        passRow.AddChild(show);
        _form.AddChild(passRow);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        _remember = new CheckBox { Text = "Lembrar de mim", ButtonPressed = true };
        _remember.AddThemeFontSizeOverride("font_size", 28);
        _enter = new Button { Text = "Entrar", CustomMinimumSize = new Vector2(300, 74), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _enter.AddThemeFontSizeOverride("font_size", 36);
        _enter.Pressed += Login;
        row.AddChild(_remember);
        row.AddChild(_enter);
        _form.AddChild(row);

        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 32);
        inner.AddChild(_status);
        _bar = new ProgressBar { CustomMinimumSize = new Vector2(0, 46), MaxValue = 1, Visible = false, ShowPercentage = true };
        _bar.AddThemeFontSizeOverride("font_size", 26);
        inner.AddChild(_bar);
        _detail = Small("");
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        inner.AddChild(_detail);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 20);
        inner.AddChild(buttons);
        _go = new Button { Text = "Baixar", CustomMinimumSize = new Vector2(400, 78), Visible = false };
        _go.AddThemeFontSizeOverride("font_size", 34);
        _go.Pressed += () => _goAction?.Invoke();
        _skip = new Button { Text = "Agora não", CustomMinimumSize = new Vector2(260, 78), Visible = false };
        _skip.AddThemeFontSizeOverride("font_size", 28);
        _skip.Pressed += SkipApp;
        foreach (var b in new[] { _go, _enter }) b.AddThemeStyleboxOverride("normal", Box(Wine, Gold, 3, 12, 10));
        buttons.AddChild(_go);
        buttons.AddChild(_skip);
        _tip = Small("");
        _tip.HorizontalAlignment = HorizontalAlignment.Center;
        _tip.AddThemeColorOverride("font_color", Gold.Lerp(Colors.White, 0.4f));
        _tip.Visible = false;
        inner.AddChild(_tip);

        var version = Small($"versão {ProjectSettings.GetSetting("application/config/version", "").AsString()}");
        version.HorizontalAlignment = HorizontalAlignment.Right;
        version.AddThemeFontSizeOverride("font_size", 20);
        col.AddChild(version);

        DisplayServer.ScreenSetKeepOn(true);
        // DEPURAÇÃO (só build Debug): DW4_TELA=baixando mostra a tela de download com números de mentira (para fotografar).
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TELA") == "baixando")
        {
            _form.Visible = false; _fakeDownload = true; _hello.Text = "Olá, feelipe!"; _status.Text = "Baixando o mundo digital…";
            _bar.Visible = true; _bar.Value = 0.42; _detail.Text = $"752.3 MB de 1.76 GB  ·  8.4 MB/s{Eta(1100L << 20, 8.4 * (1 << 20))}"; return;
        }
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") { _form.Visible = false; CheckUpdate(); return; }
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_APP_CDN") is { Length: > 0 }) { _form.Visible = false; CheckApp(); return; }
        TryRemembered();
    }

    // Logotipo original se os dados já estão no celular; senão, o nome escrito em dourado.
    Control Logo()
    {
        try
        {
            GameFiles.Init();
            var path = Path.Combine(DataPaths.Fases, "stagis10", "titulo", "img03122_logo.png");
            if (GameFiles.Exists(path) && Image.LoadFromFile(GameFiles.GodotPath(path)) is { } img && !img.IsEmpty())
                return new TextureRect
                {
                    Texture = ImageTexture.CreateFromImage(img), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(0, 200), MouseFilter = MouseFilterEnum.Ignore,
                };
        }
        catch (Exception e) { GD.Print($"[celular] sem logotipo: {e.Message}"); }
        var box = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 0);
        var title = new Label { Text = "DIGIMON WORLD 4", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 84);
        title.AddThemeColorOverride("font_color", Gold);
        title.AddThemeColorOverride("font_outline_color", new Color(0.55f, 0.12f, 0.05f));
        title.AddThemeConstantOverride("outline_size", 16);
        box.AddChild(title);
        var sub = new Label { Text = "RE:DIGITAL", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeFontSizeOverride("font_size", 30);
        sub.AddThemeColorOverride("font_color", Cyan);
        box.AddChild(sub);
        return box;
    }

    static Label Small(string text)
    {
        var l = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        l.AddThemeFontSizeOverride("font_size", 26);
        l.AddThemeColorOverride("font_color", Soft);
        return l;
    }

    // Tempo que falta, pelo ritmo atual.
    static string Eta(long left, double speed)
    {
        if (speed < 1024) return "";
        double s = left / speed;
        return s < 60 ? "  ·  menos de 1 min" : s < 3600 ? $"  ·  faltam ~{Math.Ceiling(s / 60):0} min" : $"  ·  faltam ~{s / 3600:0.0} h";
    }

    void TickTip(double delta, bool on)
    {
        _tip.Visible = on;
        if (!on) return;
        _tipTime -= delta;
        if (_tipTime > 0 && _tip.Text.Length > 0) return;
        _tipTime = 7;
        _tip.Text = Tips[_tipIndex++ % Tips.Length];
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
        _hello.Text = $"Olá de novo, {user}!";
        var r = await Call("api/auth/refresh", new Dictionary<string, object> { ["refreshToken"] = refresh }, null);
        if (r.Ok) { await AfterLogin(r); return; }
        if (r.Status is 400 or 401) { DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AccountFile)); Busy(false, "Sua sessão expirou. É só entrar de novo."); }
        else Busy(false, r.Message);
    }

    async void Login()
    {
        if (_user.Text.Trim().Length == 0 || _pass.Text.Length == 0) { _status.Text = "Preencha o usuário e a senha."; return; }
        DisplayServer.VirtualKeyboardHide();
        Busy(true, "Entrando…");
        var r = await Call("api/auth/login", new Dictionary<string, object> { ["username"] = _user.Text.Trim(), ["password"] = _pass.Text }, null);
        _pass.Text = "";
        if (!r.Ok) { Busy(false, r.Status == 429 ? "Muitas tentativas. Espere um minutinho e tente de novo." : r.Message.Length > 0 ? r.Message : "Usuário ou senha não conferem."); return; }
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
        Busy(true, "Abrindo o portal…");
        var t = await Call("api/auth/game-ticket", null, access);
        if (!t.Ok) { Busy(false, t.Message.Length > 0 ? t.Message : "Não foi possível abrir a sessão do jogo."); return; }
        _account = t.Str("username") is { Length: > 0 } tu ? tu : user;
        Session.UseLogin(_account, t.Str("ticket"));
        _form.Visible = false;
        _hello.Text = $"Olá, {_account}!";
        CheckApp();
    }

    // ---- Versão nova do app (APK) ----
    // O código fica dentro do APK e o Android não deixa o app trocar o próprio código: quando o CDN tem um APK mais novo
    // (downloads/dw4/android.json), o jogo baixa, confere o SHA-256 e abre o instalador do sistema (o usuário toca em
    // "Instalar"; o Android só aceita se a assinatura for a mesma). Sem o arquivo ou sem internet, segue com o que tem.

    AppUpdater? _app;

    void CheckApp()
    {
        _go.Visible = false;
        _status.Text = "Conferindo se há versão nova do app…";
        _detail.Text = "";
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_CDN") is { Length: > 0 }) { CheckUpdate(); return; }
        _app = new AppUpdater(OS.GetUserDataDir());
        _app.StartCheck();
    }

    void ProcessApp(AppUpdater a)
    {
        switch (a.Phase)
        {
            case AppUpdater.Stage.Checking:
                break;
            case AppUpdater.Stage.None:
                _app = null;
                CheckUpdate();
                break;
            case AppUpdater.Stage.Available when !_go.Visible:
                _status.Text = $"Tem versão nova do app! ({a.Name})";
                _detail.Text = $"{Fmt(a.Size)}. Depois é só tocar em \"Instalar\" na tela do Android. Seus dados e personagens continuam.";
                _go.Text = "Atualizar o app";
                _goAction = () => { _go.Visible = false; _skip.Visible = false; _bar.Visible = true; a.StartDownload(); };
                _go.Visible = true;
                _skip.Visible = true;
                break;
            case AppUpdater.Stage.Downloading:
                long got = Interlocked.Read(ref a.GotBytes);
                _status.Text = $"Baixando o app {a.Name}…";
                _bar.Value = a.Size > 0 ? (double)got / a.Size : 0;
                _detail.Text = $"{Fmt(got)} de {Fmt(a.Size)}";
                break;
            case AppUpdater.Stage.Downloaded when !_go.Visible:
                _bar.Visible = false;
                _goAction = () => InstallApp(a);
                _go.Text = "Instalar";
                _go.Visible = true;
                _skip.Visible = true;
                InstallApp(a);
                break;
            case AppUpdater.Stage.Failed when !_go.Visible:
                _bar.Visible = false;
                _status.Text = "Não deu para baixar a versão nova do app.";
                _detail.Text = a.Error ?? "";
                _go.Text = "Baixar pelo navegador";
                _goAction = () => OS.ShellOpen(a.Url);
                _go.Visible = true;
                _skip.Visible = true;
                break;
        }
    }

    void InstallApp(AppUpdater a)
    {
        // A permissão de instalar (REQUEST_INSTALL_PACKAGES, no export) o próprio instalador do Android pede na primeira vez:
        // "Configurações" → "Permitir desta fonte" → voltar.
        _status.Text = "Abrindo o instalador do Android…";
        _detail.Text = "Toque em \"Instalar\" (ou \"Atualizar\"). Se o Android pedir, permita instalar apps desta fonte e volte. Depois abra o jogo de novo.";
        var err = OS.ShellOpen(a.FilePath);
        GD.Print($"[celular] instalador do APK {a.Name}: {err}");
        if (err != Godot.Error.Ok)
        {
            _detail.Text = "O instalador não abriu. Toque em \"Baixar pelo navegador\" e abra o arquivo baixado.";
            _go.Text = "Baixar pelo navegador";
            _goAction = () => OS.ShellOpen(a.Url);
        }
    }

    void SkipApp()
    {
        _app = null;
        _skip.Visible = false;
        _bar.Visible = false;
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
        _skip.Visible = false;
        _status.Text = "Conferindo os dados do jogo…";
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
        _status.Visible = _status.Text.Length > 0;
        _detail.Visible = _detail.Text.Length > 0;
        TickTip(delta, _fakeDownload || _updater?.Phase == MobileUpdater.Stage.Downloading || _app?.Phase == AppUpdater.Stage.Downloading);
        if (_app is { } app) { ProcessApp(app); return; }
        if (_updater is not { } u) return;
        if (u.Error is { } error)
        {
            _updater = null;
            _bar.Visible = false;
            _status.Text = "Ops! O download parou.";
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
                _status.Text = u.FirstInstall ? "Primeira vez por aqui! Vamos baixar o mundo digital." : $"Atualização {u.Version} chegou!";
                _detail.Text = $"{Fmt(u.DownloadBytes)} para baixar. Se puder, use o Wi-Fi. Dá para fechar e continuar depois de onde parou.";
                _goAction = StartDownload;
                _go.Text = u.DownloadBytes > 20 << 20 ? "Baixar agora" : "Atualizar";
                _go.Visible = true;
                if (u.DownloadBytes <= 20 << 20 || OS.GetEnvironment("DW4_TESTE_ATUALIZAR") == "1") StartDownload(); // atualização pequena: sem perguntar
                break;
            case MobileUpdater.Stage.Downloading:
                long got = Interlocked.Read(ref u.GotBytes);
                _status.Text = "Baixando o mundo digital…";
                _bar.Value = u.DownloadBytes > 0 ? (double)got / u.DownloadBytes : 0;
                _detail.Text = $"{Fmt(got)} de {Fmt(u.DownloadBytes)}  ·  {Fmt((long)u.Speed)}/s{Eta(u.DownloadBytes - got, u.Speed)}";
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
/// <summary>
/// Versão nova do APK: lê downloads/dw4/android.json no CDN ({"build":5,"name":"0.6.6-android.5","url":"/downloads/dw4/...apk",
/// "sha256":"...","size":123}) e, se o build for maior que o deste app (o N de "android.N" no config/version, igual ao
/// version/code do export), baixa para user://atualizacao/ e confere o SHA-256.
/// </summary>
public sealed class AppUpdater(string root)
{
    // DEPURAÇÃO (só build Debug): DW4_APP_CDN=host:porta busca o android.json e o APK num servidor de teste sem TLS.
    static CdnConnection Cdn() => OS.IsDebugBuild() && OS.GetEnvironment("DW4_APP_CDN") is { Length: > 0 } t
        ? new CdnConnection(t.Split(':')[0], int.Parse(t.Split(':')[1]), tls: false) : new CdnConnection(MobileUpdater.CdnHost);
    public const string InfoPath = "/downloads/dw4/android.json";
    public enum Stage { Checking, None, Available, Downloading, Downloaded, Failed }

    public volatile Stage Phase = Stage.Checking;
    public volatile string? Error;
    public string Name = "", Url = "", FilePath = "";
    public long Size, GotBytes;
    string _path = "", _sha = "";

    public static int CurrentBuild()
    {
        var v = ProjectSettings.GetSetting("application/config/version", "").AsString();
        int i = v.LastIndexOf("android.", StringComparison.Ordinal);
        return i >= 0 && int.TryParse(v[(i + 8)..], out var n) ? n : 0;
    }

    public void StartCheck()
    {
        int current = CurrentBuild();
        new Thread(() =>
        {
            try
            {
                var dir = Path.Combine(root, "atualizacao");
                if (Directory.Exists(dir)) Directory.Delete(dir, true); // APK de uma atualização que já foi instalada
                using var doc = JsonDocument.Parse(Cdn().Get(InfoPath));
                var r = doc.RootElement;
                int build = r.GetProperty("build").GetInt32();
                Name = r.GetProperty("name").GetString() ?? "";
                _path = r.GetProperty("url").GetString() ?? "";
                _sha = (r.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
                Size = r.GetProperty("size").GetInt64();
                Url = "https://" + MobileUpdater.CdnHost + _path;
                GD.Print($"[celular] app build {current}, CDN build {build} ({Name})");
                Phase = build > current && current > 0 && _path.StartsWith('/') && _sha.Length == 64 ? Stage.Available : Stage.None;
            }
            catch (Exception e)
            {
                GD.Print($"[celular] sem informação de versão do app: {e.Message}");
                Phase = Stage.None; // sem android.json ou sem internet: segue com este app
            }
        }) { IsBackground = true, Name = "dw4-app" }.Start();
    }

    public void StartDownload()
    {
        Phase = Stage.Downloading;
        new Thread(() =>
        {
            try
            {
                var dir = Path.Combine(root, "atualizacao");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, Path.GetFileName(_path));
                var part = file + ".part";
                using (var fs = new FileStream(part, FileMode.Create, System.IO.FileAccess.Write))
                using (var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256))
                {
                    Cdn().GetTo(_path, (buf) =>
                    {
                        fs.Write(buf);
                        sha.AppendData(buf);
                        Interlocked.Add(ref GotBytes, buf.Length);
                    });
                    var got = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
                    if (got != _sha) throw new IOException("o arquivo baixado veio diferente do esperado (SHA-256). Tente de novo.");
                }
                File.Move(part, file, overwrite: true);
                FilePath = file;
                Phase = Stage.Downloaded;
            }
            catch (Exception e)
            {
                Error = e.Message;
                Phase = Stage.Failed;
            }
        }) { IsBackground = true, Name = "dw4-app-download" }.Start();
    }
}

public sealed class MobileUpdater
{
    public const string CdnHost = "cdn.redigitalon.com";
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
    HashSet<string> _keep = [];

    /// <summary>
    /// Apaga os arquivos de dados que saíram do manifesto (ex.: os 52 mil soltos da 0.6.5 depois que vieram os pacotes por
    /// fase) e as pastas que ficaram vazias. Os arquivos do controle (.instalados, .manifesto.json) ficam.
    /// </summary>
    void Cleanup()
    {
        var dados = Full("dados");
        if (!Directory.Exists(dados)) return;
        int removed = 0;
        foreach (var f in Directory.EnumerateFiles(dados, "*", SearchOption.AllDirectories).ToList())
        {
            var rel = "dados/" + Path.GetRelativePath(dados, f).Replace('\\', '/');
            if (_keep.Contains(rel) || rel is "dados/.instalados" or "dados/.manifesto.json") continue;
            try { File.Delete(f); removed++; } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        foreach (var d in Directory.EnumerateDirectories(dados, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch (IOException) { }
        if (removed > 0) GD.Print($"[atualizar] {removed} arquivos antigos apagados");
    }

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
        _keep = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        if (_todo.Count == 0) Cleanup();
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
        Note = "Limpando arquivos antigos…";
        Cleanup();
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

    /// <summary>Baixa em partes (sem guardar tudo na memória). Sem novas tentativas: quem chama recomeça.</summary>
    public void GetTo(string path, Action<byte[]> sink)
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
        if (code != 200) { Close(); throw new IOException($"o servidor respondeu {code}"); }
        while (c.GetStatus() == Godot.HttpClient.Status.Body)
        {
            c.Poll();
            var part = c.ReadResponseBodyChunk();
            if (part.Length == 0) { if (DateTime.UtcNow > limit) throw new IOException("tempo esgotado"); Thread.Sleep(1); }
            else { sink(part); limit = DateTime.UtcNow.AddSeconds(60); }
        }
        Close();
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
