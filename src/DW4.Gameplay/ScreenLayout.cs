namespace DW4.Gameplay;

/// <summary>
/// Tela do jogo (640 × 480, origem no centro, y para cima) dentro de uma janela de qualquer proporção. DECISÃO DO PORT
/// (docs/decisoes-port-pc.md): a escala é a da altura (480 linhas do jogo ocupam a altura da janela) e a arte nunca é esticada;
/// numa janela mais larga que 4:3 sobram <see cref="Extra"/> unidades do jogo de cada lado, onde o HUD vai preso às bordas;
/// numa janela mais estreita (5:4) a escala passa a ser a da largura e sobram faixas em cima e embaixo.
/// </summary>
public readonly record struct ScreenLayout(float Scale, float Extra, float OffsetX, float OffsetY)
{
    public const float Width = 640, Height = 480;

    public static ScreenLayout Fit(float windowWidth, float windowHeight)
    {
        if (windowWidth <= 0 || windowHeight <= 0) return new(1, 0, 0, 0);
        float scale = Math.Min(windowHeight / Height, windowWidth / Width);
        float extra = Math.Max(0, (windowWidth / scale - Width) / 2);
        return new(scale, extra, (windowWidth - Width * scale) / 2, (windowHeight - Height * scale) / 2);
    }

    /// <summary>Largura da tela do jogo esticada até as bordas da janela (em unidades do jogo; 640 em 4:3).</summary>
    public float WideWidth => Width + 2 * Extra;

    /// <summary>x (unidades do jogo, origem no centro) de um elemento preso à borda do mesmo lado em que está na tela 4:3.</summary>
    public float AnchorX(float x) => x < 0 ? x - Extra : x > 0 ? x + Extra : x;

    /// <summary>Fov vertical (graus) de uma câmera cujo fov horizontal (graus) foi feito para a tela 4:3.</summary>
    public static float VerticalFov(float horizontalDeg) =>
        (float)(2 * Math.Atan(Math.Tan(horizontalDeg * Math.PI / 360) * Height / Width) * 180 / Math.PI);

    /// <summary>Ponto da tela 4:3 (canto de cima à esquerda = 0, 0) em pixels da janela, com x preso à borda mais próxima.</summary>
    public (float X, float Y) ToWindow(float x, float y, bool anchor)
    {
        float ax = anchor ? (x < Width / 2 ? x - Extra : x + Extra) : x;
        return (OffsetX + ax * Scale, OffsetY + y * Scale);
    }
}
