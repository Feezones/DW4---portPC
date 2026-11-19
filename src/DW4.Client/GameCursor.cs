using System;
using Godot;

namespace DW4.Client;

/// <summary>
/// Cursor do mouse do port (DECISÃO DO PORT PARA PC, não é do original: o GameCube não tem mouse). Arte própria em SVG (seta clara de
/// borda azul neon, como a HUD): seta nos menus e mira em combate, onde o golpe sai na direção do cursor (Fatia.cs). Contorno
/// preto para aparecer em fundo claro e escuro (o chão e os menus do jogo são azuis). Fica como cursor de hardware (não atrasa com o FPS) e cresce com a altura da janela.
/// Configurações > Gráfico: "Cursor do mouse" (digital com mira / só a seta / sistema) e "Tamanho do cursor".
/// </summary>
public static class GameCursor
{
    const string Arrow = """
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <defs><linearGradient id="f" x1="0" y1="0" x2="0.6" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#8fe6ff"/></linearGradient></defs>
  <path d="M2.5 2 L2.5 25 L8.2 19.6 L12 28.2 L16.6 26.2 L12.9 17.9 L20.8 17.9 Z" fill="none" stroke="#000" stroke-opacity="0.9" stroke-width="3.4" stroke-linejoin="round"/>
  <path d="M2.5 2 L2.5 25 L8.2 19.6 L12 28.2 L16.6 26.2 L12.9 17.9 L20.8 17.9 Z" fill="url(#f)" stroke="#1aa8e6" stroke-width="1.4" stroke-linejoin="round"/>
  <path d="M4.7 7.5 L4.7 19 L7.6 16.3" fill="none" stroke="#1679c4" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M7.6 12.8 L10.6 15.8" fill="none" stroke="#1679c4" stroke-width="1" stroke-linecap="round"/>
</svg>
""";

    // Sobre um botão: a mesma seta em dourado (a cor da seleção nos menus do jogo), para saber que dá para clicar.
    const string ArrowHover = """
<svg xmlns="http://www.w3.org/2000/svg" width="32" height="32" viewBox="0 0 32 32">
  <defs><linearGradient id="f" x1="0" y1="0" x2="0.6" y2="1"><stop offset="0" stop-color="#fff6c4"/><stop offset="1" stop-color="#ffcf33"/></linearGradient></defs>
  <path d="M2.5 2 L2.5 25 L8.2 19.6 L12 28.2 L16.6 26.2 L12.9 17.9 L20.8 17.9 Z" fill="none" stroke="#000" stroke-opacity="0.9" stroke-width="3.4" stroke-linejoin="round"/>
  <path d="M2.5 2 L2.5 25 L8.2 19.6 L12 28.2 L16.6 26.2 L12.9 17.9 L20.8 17.9 Z" fill="url(#f)" stroke="#e09a00" stroke-width="1.4" stroke-linejoin="round"/>
  <path d="M4.7 7.5 L4.7 19 L7.6 16.3" fill="none" stroke="#b06a00" stroke-width="1.1" stroke-linecap="round" stroke-linejoin="round"/>
  <path d="M7.6 12.8 L10.6 15.8" fill="none" stroke="#b06a00" stroke-width="1" stroke-linecap="round"/>
</svg>
""";

    const string Crosshair = """
<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48">
  <g fill="none" stroke-linecap="round" stroke-linejoin="round">
    <g stroke="#000" stroke-opacity="0.9" stroke-width="4.4"><path d="M10.5 17 L10.5 10.5 L17 10.5 M31 10.5 L37.5 10.5 L37.5 17 M37.5 31 L37.5 37.5 L31 37.5 M17 37.5 L10.5 37.5 L10.5 31"/><path d="M24 4.5 L24 14 M24 34 L24 43.5 M4.5 24 L14 24 M34 24 L43.5 24"/></g>
    <g stroke="#ffffff" stroke-width="2.2"><path d="M10.5 17 L10.5 10.5 L17 10.5 M31 10.5 L37.5 10.5 L37.5 17 M37.5 31 L37.5 37.5 L31 37.5 M17 37.5 L10.5 37.5 L10.5 31"/></g>
    <g stroke="#2fd8ff" stroke-width="2.2"><path d="M24 4.5 L24 14 M24 34 L24 43.5 M4.5 24 L14 24 M34 24 L43.5 24"/></g>
  </g>
  <path d="M24 20.2 L27.8 24 L24 27.8 L20.2 24 Z" fill="#ffd23a" stroke="#000" stroke-opacity="0.9" stroke-width="1.5" stroke-linejoin="round"/>
</svg>
""";

    const float CrossScale = 1.25f; // a mira sai com 60 px em 1080p (48 sumia no chão azul do lobby)
    static int s_style = -1, s_look = -1;
    static float s_scale;
    static ImageTexture? s_arrow, s_hover, s_cross;

    /// <summary>Chamado a cada quadro pela tela ativa. combat = em jogo, sem menu, com mouse/teclado: a seta vira mira.</summary>
    public static void Update(Viewport viewport, bool combat)
    {
        if (DataPaths.Mobile || DisplayServer.GetName() == "headless") return;
        var c = PortSettings.Current;
        int style = c.CursorStyle;
        float scale = MathF.Round(Math.Clamp(DisplayServer.WindowGetSize().Y / 1080f, 1f, 2f) * c.CursorSize * 8f) / 8f; // degraus de 1/8
        // Sobre um botão, barra ou lista (inclusive os da HUD em combate) a seta acende; em combate, volta a seta.
        var hovered = viewport.GuiGetHoveredControl();
        bool clickable = hovered is BaseButton or Godot.Range or ItemList or TabBar or LineEdit or TextEdit && hovered.Visible;
        int look = style == 2 ? -2 : clickable ? 1 : combat && style == 0 ? 2 : 0;
        if (style == s_style && scale == s_scale && look == s_look) return;
        bool rebuilt = style != s_style || scale != s_scale;
        (s_style, s_scale, s_look) = (style, scale, look);
        if (style == 2)
        {
            Input.SetCustomMouseCursor(null, Input.CursorShape.Arrow);
            Input.SetCustomMouseCursor(null, Input.CursorShape.PointingHand);
            return;
        }
        if (rebuilt || s_arrow == null)
        {
            s_arrow = Raster(Arrow, scale);
            s_hover = Raster(ArrowHover, scale);
            s_cross = Raster(Crosshair, scale * CrossScale);
        }
        var tip = new Vector2(2, 2) * scale;
        if (look == 2) Input.SetCustomMouseCursor(s_cross, Input.CursorShape.Arrow, new Vector2(24, 24) * scale * CrossScale);
        else Input.SetCustomMouseCursor(look == 1 ? s_hover : s_arrow, Input.CursorShape.Arrow, tip);
        if (rebuilt) Input.SetCustomMouseCursor(s_hover, Input.CursorShape.PointingHand, tip);
    }

    static ImageTexture Raster(string svg, float scale)
    {
        var image = new Image();
        image.LoadSvgFromString(svg, scale);
        return ImageTexture.CreateFromImage(image);
    }
}
