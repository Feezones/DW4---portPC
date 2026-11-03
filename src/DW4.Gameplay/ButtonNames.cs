using System;

namespace DW4.Gameplay;

/// <summary>Nomes de botão por família de controle e a troca dos ícones do GameCube da fonte do jogo (ver DeviceIcons no cliente).</summary>
public static class ButtonNames
{
    public enum Kind { Keyboard, Xbox, PlayStation, Nintendo, Generic }

    /// <summary>Família do controle pelo nome que a Godot dá ao joypad.</summary>
    public static Kind FromJoyName(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("xbox") || n.Contains("xinput") || n.Contains("x-box")) return Kind.Xbox;
        if (n.Contains("ps4") || n.Contains("ps5") || n.Contains("ps3") || n.Contains("dualshock") || n.Contains("dualsense") || n.Contains("playstation") || n.Contains("sony")) return Kind.PlayStation;
        if (n.Contains("nintendo") || n.Contains("switch") || n.Contains("pro controller") || n.Contains("joy-con")) return Kind.Nintendo;
        return Kind.Generic;
    }

    /// <summary>Nome da ligação para a família dada (as de teclado/mouse não dependem dela).</summary>
    public static string Name(string binding, Kind kind)
    {
        if (!binding.StartsWith("botao:", StringComparison.Ordinal) && !binding.StartsWith("eixo:", StringComparison.Ordinal)) return InputBindings.Describe(binding);
        string v = binding[(binding.IndexOf(':') + 1)..];
        bool axis = binding.StartsWith("eixo:", StringComparison.Ordinal);
        return (kind, axis, v) switch
        {
            (Kind.PlayStation, false, "0") => "×", (Kind.PlayStation, false, "1") => "○", (Kind.PlayStation, false, "2") => "□", (Kind.PlayStation, false, "3") => "△",
            (Kind.PlayStation, false, "4") => "Share", (Kind.PlayStation, false, "6") => "Options", (Kind.PlayStation, false, "9") => "L1", (Kind.PlayStation, false, "10") => "R1",
            (Kind.PlayStation, true, "4+") => "L2", (Kind.PlayStation, true, "5+") => "R2",
            // Nintendo: a Godot numera pela posição (0 = baixo), e embaixo fica o B, à direita o A, à esquerda o Y e em cima o X.
            (Kind.Nintendo, false, "0") => "B", (Kind.Nintendo, false, "1") => "A", (Kind.Nintendo, false, "2") => "Y", (Kind.Nintendo, false, "3") => "X",
            (Kind.Nintendo, false, "4") => "-", (Kind.Nintendo, false, "6") => "+", (Kind.Nintendo, false, "9") => "L", (Kind.Nintendo, false, "10") => "R",
            (Kind.Nintendo, true, "4+") => "ZL", (Kind.Nintendo, true, "5+") => "ZR",
            (Kind.Generic, false, _) when int.TryParse(v, out int n) && n <= 3 => $"Botão {n + 1}",
            _ => InputBindings.Describe(binding),
        };
    }

    /// <summary>Troca os ícones do GameCube da fonte do jogo pelo botão do aparelho atual, entre parênteses.</summary>
    public static string GameText(string text, Func<string, string> of, bool keyboard)
    {
        if (text.Length == 0 || text.IndexOfAny(['≪', '≒', '∠', '√', '≡', '娃', 'Ｓ']) < 0) return text;
        string P(string s) => $"({s})";
        string dpad = keyboard ? $"{of("tecnica_anterior")}/{of("tecnica_proxima")}" : "Direcional";
        return text.Replace("ＳＴＡＲＴ", P(of("status"))).Replace("≪", P(of("ataque"))).Replace("≒", P(of("pulo"))).Replace("∠", P(of("guarda")))
            .Replace("√", P(of("cancelar"))).Replace("≡", P(dpad)).Replace("娃", P(dpad));
    }

}
