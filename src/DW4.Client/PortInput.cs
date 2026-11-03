using System;
using System.Linq;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Liga o mapa de controles do port (InputBindings, no config.cfg) ao InputMap da Godot: cada ação vira "dw4_&lt;id&gt;" com os eventos
/// do teclado/mouse e do controle. O jogo pergunta por <see cref="Down"/> (segurado) e <see cref="Pressed"/> (evento).
/// </summary>
public static class PortInput
{
    public static string Name(string action) => "dw4_" + action;

    public static void Apply(InputBindings b)
    {
        foreach (var a in InputBindings.Actions)
        {
            var name = (StringName)Name(a.Id);
            if (InputMap.HasAction(name)) InputMap.ActionEraseEvents(name);
            else InputMap.AddAction(name, 0.35f);
            foreach (var device in new[] { InputBindings.Keyboard, InputBindings.Pad })
                foreach (var binding in b.Get(device, a.Id))
                    if (ToEvent(binding) is { } ev) InputMap.ActionAddEvent(name, ev);
        }
    }

    public static bool Down(string action) => InputMap.HasAction(Name(action)) && Input.IsActionPressed(Name(action));

    public static bool Pressed(InputEvent e, string action) => InputMap.HasAction(Name(action)) && e.IsActionPressed(Name(action), allowEcho: false, exactMatch: false);

    /// <summary>"tecla:J" → InputEventKey; "mouse:1" → botão do mouse; "botao:0" → botão do controle; "eixo:4+" → eixo.</summary>
    public static InputEvent? ToEvent(string binding)
    {
        int colon = binding.IndexOf(':');
        if (colon < 0) return null;
        string kind = binding[..colon], v = binding[(colon + 1)..];
        switch (kind)
        {
            case "tecla" when Enum.TryParse<Key>(v, out var key): return new InputEventKey { PhysicalKeycode = key }; // física: WASD no mesmo lugar em qualquer layout
            case "mouse" when int.TryParse(v, out int m): return new InputEventMouseButton { ButtonIndex = (MouseButton)m };
            case "botao" when int.TryParse(v, out int j): return new InputEventJoypadButton { ButtonIndex = (JoyButton)j, Device = -1 };
            case "eixo" when v.Length >= 2 && int.TryParse(v[..^1], out int axis):
                return new InputEventJoypadMotion { Axis = (JoyAxis)axis, AxisValue = v[^1] == '-' ? -1 : 1, Device = -1 };
            default: return null;
        }
    }

    /// <summary>Evento apertado na tela de troca → ligação do aparelho pedido (null se não serve).</summary>
    public static string? FromEvent(InputEvent e, string device)
    {
        if (device == InputBindings.Keyboard)
        {
            if (e is InputEventKey { Pressed: true, Echo: false } k)
            {
                var key = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode;
                return key == Key.None ? null : $"tecla:{key}";
            }
            if (e is InputEventMouseButton { Pressed: true } mb) return $"mouse:{(int)mb.ButtonIndex}";
            return null;
        }
        if (e is InputEventJoypadButton { Pressed: true } jb) return $"botao:{(int)jb.ButtonIndex}";
        if (e is InputEventJoypadMotion jm && Math.Abs(jm.AxisValue) > 0.6f) return $"eixo:{(int)jm.Axis}{(jm.AxisValue < 0 ? "-" : "+")}";
        return null;
    }
}
