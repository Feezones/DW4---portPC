using System;
using System.Linq;
using Godot;
using DW4.Gameplay;

using Kind = DW4.Gameplay.ButtonNames.Kind;

namespace DW4.Client;

/// <summary>
/// Ícones de botão conforme o aparelho (DECISÃO DO PORT). O último aparelho usado (teclado/mouse, Xbox, PlayStation, Nintendo ou outro
/// controle, pelo nome do joypad na Godot) decide os nomes nas dicas do port, nas Configurações e nos textos do jogo.
/// Os ícones são desenhados pelo próprio port (círculos e cápsulas coloridas com a letra ou o símbolo; nenhuma arte de terceiros).
/// FATO (fonte do jogo, glifos usados nos textos do tutorial e das dicas): "≪" = ataque, "≒" = Z do GameCube (pulo), "∠" = L (defesa),
/// "√" = B (lista de itens), "≡"/"娃" = direcional (escolher técnica) e "ＳＴＡＲＴ" = START. Nos textos do jogo eles viram o nome do
/// botão do aparelho atual entre parênteses, com caracteres que a fonte do jogo tem (a fonte não tem colchetes nem ← →).
/// HIPÓTESE: o "√" (lista de itens, que o port não tem) vira o botão de cancelar, o B do GameCube na tradução do port.
/// </summary>
public static class DeviceIcons
{

    public static Kind Current { get; private set; } = Kind.Keyboard;
    static Kind _lastPad = Kind.Xbox;

    public static Kind FromJoyName(string name) => ButtonNames.FromJoyName(name);

    /// <summary>Chamado com cada evento: guarda o último aparelho usado.</summary>
    public static void Seen(InputEvent e)
    {
        if (e is InputEventJoypadButton { Pressed: true } jb) Current = _lastPad = FromJoyName(Input.GetJoyName(jb.Device));
        else if (e is InputEventJoypadMotion jm && Math.Abs(jm.AxisValue) > 0.5f) Current = _lastPad = FromJoyName(Input.GetJoyName(jm.Device));
        else if (e is InputEventKey { Pressed: true } || e is InputEventMouseButton { Pressed: true }) Current = Kind.Keyboard;
    }

    /// <summary>Família para mostrar ligações de controle (o último controle usado; sem nenhum, o layout Xbox).</summary>
    public static Kind PadKind => Current == Kind.Keyboard ? _lastPad : Current;

    public static string Name(string binding, Kind kind) => ButtonNames.Name(binding, kind);

    /// <summary>Nome do botão/tecla da ação no aparelho atual (o primeiro da lista), ou "—" sem ligação.</summary>
    public static string Of(string action)
    {
        var b = PortSettings.Current.Bindings;
        bool pad = Current != Kind.Keyboard;
        var list = b.Get(pad ? InputBindings.Pad : InputBindings.Keyboard, action);
        if (list.Count == 0) list = b.Get(pad ? InputBindings.Keyboard : InputBindings.Pad, action);
        return list.Count == 0 ? "—" : Name(list[0], pad ? Current : Kind.Keyboard);
    }

    /// <summary>Troca os ícones do GameCube da fonte do jogo pelo botão do aparelho atual, entre parênteses.</summary>
    public static string GameText(string text) => ButtonNames.GameText(text, Of, Current == Kind.Keyboard);

    /// <summary>Ícone desenhado pelo port para a ligação: círculo colorido (botões de face) ou cápsula (o resto), com o nome dentro.</summary>
    public static Control Chip(string binding, Kind kind)
    {
        string name = Name(binding, kind);
        bool face = binding is "botao:0" or "botao:1" or "botao:2" or "botao:3";
        var color = !face ? new Color(0.22f, 0.24f, 0.3f) : (kind, binding) switch
        {
            (Kind.Xbox or Kind.Generic, "botao:0") => new Color(0.36f, 0.66f, 0.24f), (Kind.Xbox or Kind.Generic, "botao:1") => new Color(0.82f, 0.22f, 0.2f),
            (Kind.Xbox or Kind.Generic, "botao:2") => new Color(0.2f, 0.45f, 0.85f), (Kind.Xbox or Kind.Generic, "botao:3") => new Color(0.9f, 0.72f, 0.15f),
            (Kind.PlayStation, "botao:0") => new Color(0.35f, 0.55f, 0.9f), (Kind.PlayStation, "botao:1") => new Color(0.88f, 0.3f, 0.32f),
            (Kind.PlayStation, "botao:2") => new Color(0.82f, 0.45f, 0.75f), (Kind.PlayStation, "botao:3") => new Color(0.3f, 0.75f, 0.6f),
            _ => new Color(0.45f, 0.45f, 0.5f),
        };
        var style = new StyleBoxFlat { BgColor = color, BorderColor = new Color(1, 1, 1, 0.6f), CornerDetail = 8 };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(face ? 16 : 7);
        style.SetContentMarginAll(4);
        style.ContentMarginLeft = style.ContentMarginRight = face ? 9 : 8;
        var chip = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        chip.AddThemeStyleboxOverride("panel", style);
        var label = new Label { Text = name, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 16);
        chip.AddChild(label);
        return chip;
    }
}
