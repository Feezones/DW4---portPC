using Godot;

namespace DW4.Client;

/// <summary>
/// Barra de HP do chefe do port (pedido do usuário 2026-11-07: "muito ruim de identificar a vida atual").
/// FATO (i_bosshp10, ndmw_0187_hud.json): no original é uma faixa de 460×10 no alto da tela (GENERATE V&lt;0,205,0&gt;), com
/// filetes dourados em cima e embaixo, e três camadas sobrepostas (hp01/hp02/hp03, mistura aditiva) que enchem a largura
/// inteira cada uma: a 1ª cobre 0–33 %, a 2ª 33–67 % e a 3ª 67–100 % do medidor bshp. Abre em 30 quadros e fecha em 120.
/// O comprimento à vista é só o da camada de cima e as cores se somam, por isso a vida atual fica difícil de ler.
/// DECISÃO DO PORT: mesmo lugar, mesmo tamanho e mesma moldura dourada, mas uma faixa só (o comprimento é a vida), fundo escuro
/// para a parte perdida, cor que muda por terço (lembra as três camadas), marcas em 25/50/75 %, rastro claro do dano que acabou
/// de entrar, piscada no golpe e o número da porcentagem no meio.
/// </summary>
public partial class BossHpBar : Control
{
    // Espaço do original (640×480, origem no centro): a barra fica com o centro em y = 205 → 35 px do alto.
    const float VirtualHeight = 480f, CenterY = 35f, BarHeight = 12f;

    float _target = 100, _shown = 100, _trail = 100, _trailHold, _flash, _alpha;
    bool _open;

    /// <summary>Largura em unidades do original: 460 no PC; no celular 400 para não encostar nos botões MENU/CHAT do alto.</summary>
    public float VirtualWidth { get; set; } = 460f;

    /// <summary>Barra aberta (some com fade, como o WINCLOSE do original).</summary>
    public bool Open => _open;

    public BossHpBar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void SetOpen(bool open)
    {
        if (open && !_open) { _shown = _trail = _target; _trailHold = 0; }
        _open = open;
    }

    /// <summary>Vida do chefe em % (0–100), como o SETPERCENT do medidor bshp.</summary>
    public void SetPercent(float pct)
    {
        pct = Mathf.Clamp(pct, 0, 100);
        if (pct < _target - 0.01f)
        {
            if (_trail < _shown) _trail = _shown;
            _trailHold = 0.45f; // o rastro espera um pouco antes de descer (mostra o tamanho do golpe)
            _flash = 1;
        }
        else if (pct > _target) _trail = Mathf.Max(_trail, pct);
        _target = pct;
        if (!_open) _shown = _trail = pct;
    }

    /// <summary>Valor que a barra está mostrando agora (teste).</summary>
    public float Shown => _shown;

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        // FATO: WINOPEN = FADESET IN 30 quadros; WINCLOSE = FADESET OUT 120 quadros (60 Hz).
        _alpha = _open ? Mathf.Min(1, _alpha + dt * 2f) : Mathf.Max(0, _alpha - dt * 0.5f);
        _shown = Mathf.MoveToward(_shown, _target, Mathf.Max(60f, Mathf.Abs(_target - _shown) * 12f) * dt);
        if (_trailHold > 0) _trailHold -= dt;
        else _trail = Mathf.MoveToward(_trail, _shown, 45f * dt);
        if (_trail < _shown) _trail = _shown;
        _flash = Mathf.Max(0, _flash - dt * 5f);
        Visible = _alpha > 0.001f;
        if (Visible) QueueRedraw();
    }

    // Cor por terço (as três camadas do original): verde → amarelo → vermelho, com brilho em cima.
    static Color FillColor(float pct) => pct > 66.7f ? new Color(0.36f, 0.86f, 0.32f) : pct > 33.3f ? new Color(0.98f, 0.78f, 0.18f) : new Color(0.93f, 0.22f, 0.16f);

    public override void _Draw()
    {
        var size = Size;
        float k = size.Y / VirtualHeight;
        float w = VirtualWidth * k, h = BarHeight * k;
        var bar = new Rect2(size.X / 2 - w / 2, CenterY * k - h / 2, w, h);
        float a = _alpha;
        Color A(Color c) => new(c, c.A * a);

        var gold = new Color(247 / 255f, 222 / 255f, 49 / 255f);
        var goldDark = new Color(82 / 255f, 66 / 255f, 0);
        float line = Mathf.Max(1, 2 * k), pad = Mathf.Max(1, 2 * k);

        // Moldura: contorno escuro + filetes dourados em cima e embaixo (como o pPlaneShape1 do original) e pontas.
        var frame = bar.Grow(pad + line);
        DrawRect(frame.Grow(Mathf.Max(1, k)), A(new Color(0, 0, 0, 0.55f)));
        DrawRect(frame, A(goldDark));
        DrawRect(new Rect2(frame.Position, new Vector2(frame.Size.X, line)), A(gold));
        DrawRect(new Rect2(frame.Position.X, frame.End.Y - line, frame.Size.X, line), A(gold));
        // Fundo da parte perdida.
        DrawRect(bar, A(new Color(0.08f, 0.06f, 0.10f, 0.92f)));

        // Rastro do dano recente (claro) e a vida atual por cima.
        if (_trail > _shown + 0.05f)
            DrawRect(new Rect2(bar.Position.X + w * _shown / 100, bar.Position.Y, w * (_trail - _shown) / 100, h), A(new Color(1, 0.93f, 0.75f, 0.9f)));
        if (_shown > 0)
        {
            var fill = new Rect2(bar.Position, new Vector2(w * _shown / 100, h));
            var c = FillColor(_shown).Lerp(Colors.White, _flash * 0.55f);
            DrawRect(fill, A(c));
            DrawRect(new Rect2(fill.Position, new Vector2(fill.Size.X, h * 0.38f)), A(new Color(1, 1, 1, 0.28f))); // brilho
            DrawRect(new Rect2(fill.Position.X, fill.End.Y - h * 0.22f, fill.Size.X, h * 0.22f), A(new Color(0, 0, 0, 0.22f)));
            DrawRect(new Rect2(fill.End.X - Mathf.Max(1, k), fill.Position.Y, Mathf.Max(1, k), h), A(new Color(1, 1, 1, 0.85f))); // ponta
        }

        // Marcas de 25/50/75 %.
        for (int i = 1; i < 4; i++)
        {
            float x = bar.Position.X + w * i / 4f;
            var tick = x <= bar.Position.X + w * _shown / 100 ? new Color(0, 0, 0, 0.55f) : new Color(1, 1, 1, 0.22f); // escura na vida, clara no fundo
            DrawRect(new Rect2(x - Mathf.Max(0.5f, k * 0.5f), bar.Position.Y, Mathf.Max(1, k), h * (i == 2 ? 1f : 0.55f)), A(tick));
        }

        // Porcentagem no meio da barra, com contorno para ler sobre qualquer cor.
        var font = ThemeDB.FallbackFont;
        int fs = Mathf.Max(10, Mathf.RoundToInt(h * 1.0f));
        var text = $"{Mathf.CeilToInt(_target)}%";
        var ts = font.GetStringSize(text, HorizontalAlignment.Left, -1, fs);
        var pos = new Vector2(size.X / 2 - ts.X / 2, bar.Position.Y + h / 2 + font.GetAscent(fs) / 2 - font.GetDescent(fs) / 2);
        DrawStringOutline(font, pos, text, HorizontalAlignment.Left, -1, fs, Mathf.Max(3, Mathf.RoundToInt(fs * 0.3f)), A(new Color(0, 0, 0, 0.9f)));
        DrawString(font, pos, text, HorizontalAlignment.Left, -1, fs, A(Colors.White));
    }
}
