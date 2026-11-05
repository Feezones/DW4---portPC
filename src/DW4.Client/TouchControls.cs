using System;
using System.Collections.Generic;
using Godot;

namespace DW4.Client;

/// <summary>
/// Controles na tela do celular (DECISÃO DO PORT, versão Android, 2026-11-04): analógico que aparece onde o dedo toca na metade
/// esquerda e botões redondos à direita, ligados às mesmas ações do mapa de controles (PortInput: dw4_ataque, dw4_pulo...).
/// Somem quando um controle (Bluetooth) é usado e voltam no próximo toque. Mostra também o FPS (medição da etapa 1).
/// </summary>
public partial class TouchControls : CanvasLayer
{
    sealed record Pad(string Action, string Label, float X, float Y, float R);

    // Posição em relação ao canto (x negativo = da direita, y negativo = de baixo), em pixels da tela lógica (1600×900).
    // Pedido do usuário (2026-11-04): sem PAUSA; MENU (o TAB: status/inventário) no canto de cima à esquerda e a engrenagem do
    // lado (as opções do Esc).
    static readonly Pad[] Pads =
    [
        new("ataque", "ATQ", -175, -175, 100),
        new("pulo", "PULO", -390, -115, 68),
        new("guarda", "DEF", -120, -395, 68),
        new("tecnica", "TÉC", -340, -320, 68),
        new("tecnica_proxima", "TÉC ›", -500, -270, 46),
        new("confirmar", "OK", -90, 230, 46),
        new("cancelar", "VOLTAR", -210, 230, 46),
        new("status", "MENU", 90, 95, 46),
        new("pausa", Gear, 210, 95, 46),
        new("itens", "ITENS", -210, 95, 46),
        new("chat", "CHAT", -90, 95, 46),
    ];

    const string Gear = "⚙";
    // Com uma janela aberta (menu, itens, chat, loja, pausa) só fica este: manda o Esc, que fecha a janela como no PC.
    static readonly Pad Close = new(Esc, "FECHAR", -90, 95, 46);
    const string Esc = "esc";

    /// <summary>Janela aberta: some com os botões de luta e deixa o toque para a interface (Fatia liga).</summary>
    public Func<bool>? Blocked;
    /// <summary>Ponto da tela em cima de algo da interface que se toca (atalhos de itens): o analógico não começa ali.</summary>
    public Func<Vector2, bool>? UiAt;

    const float StickRadius = 120;

    Surface _surface = null!;

    public override void _Ready()
    {
        Layer = 90;
        // DEPURAÇÃO (só build Debug, DW4_TOQUE=1 no PC): o mouse vira toque para testar os botões sem celular.
        if (OS.IsDebugBuild() && OS.GetEnvironment("DW4_TOQUE") == "1") Input.EmulateTouchFromMouse = true;
        _surface = new Surface { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_surface);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventJoypadButton { Pressed: true } || e is InputEventJoypadMotion { AxisValue: > 0.5f or < -0.5f })
        {
            if (_surface.Visible) { _surface.ReleaseAll(); _surface.Visible = false; }
            return;
        }
        if (e is InputEventScreenTouch or InputEventScreenDrag)
        {
            _surface.Visible = true;
            _surface.Touch(e);
        }
    }

    public override void _Process(double delta)
    {
        bool blocked = Blocked?.Invoke() == true;
        if (blocked != _surface.BlockedMode) { _surface.ReleaseAll(); _surface.BlockedMode = blocked; }
        _surface.UiAt = UiAt;
        _surface.QueueRedraw();
    }

    // Botão "voltar" do Android no jogo: vale como o Esc (fecha a janela aberta ou abre a pausa).
    public override void _Notification(int what)
    {
        if (what == NotificationWMGoBackRequest) Surface.Send(Esc, true, true);
    }

    partial class Surface : Control
    {
        readonly Dictionary<int, string> _held = []; // dedo → ação do botão
        public bool BlockedMode;
        public Func<Vector2, bool>? UiAt;
        Pad[] Current => BlockedMode ? [Close] : Pads;

        // Aperta/solta como um evento de verdade (InputEventAction): o jogo pergunta tanto "está apertado" (Input) quanto
        // "acabou de apertar" (o evento, ex.: MENU, DEF, CHAT, ITENS); o Input.ActionPress só fazia o primeiro.
        public static void Send(string action, bool pressed, bool tap = false)
        {
            if (action == Esc)
            {
                if (!pressed) return;
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false });
                return;
            }
            var name = PortInput.Name(action);
            Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = pressed, Strength = pressed ? 1 : 0 });
            if (tap) Input.ParseInputEvent(new InputEventAction { Action = name, Pressed = false });
        }
        int _stickFinger = -1;
        Vector2 _stickBase, _stick;

        Vector2 At(Pad p)
        {
            var s = Size;
            return new Vector2(p.X < 0 ? s.X + p.X : p.X, p.Y < 0 ? s.Y + p.Y : p.Y);
        }

        public void Touch(InputEvent e)
        {
            if (e is InputEventScreenTouch t)
            {
                if (t.Pressed)
                {
                    foreach (var p in Current)
                        if (t.Position.DistanceTo(At(p)) <= p.R * 1.15f)
                        {
                            _held[t.Index] = p.Action;
                            Send(p.Action, true);
                            GetViewport().SetInputAsHandled();
                            return;
                        }
                    if (BlockedMode || UiAt?.Invoke(t.Position) == true) return; // o toque vai para a interface
                    if (t.Position.X < Size.X * 0.45f && t.Position.Y > Size.Y * 0.3f && _stickFinger < 0)
                    {
                        _stickFinger = t.Index;
                        _stickBase = t.Position;
                        _stick = Vector2.Zero;
                        GetViewport().SetInputAsHandled();
                    }
                }
                else
                {
                    if (_held.Remove(t.Index, out var action)) Send(action, false);
                    if (t.Index == _stickFinger) { _stickFinger = -1; Move(Vector2.Zero); }
                }
            }
            else if (e is InputEventScreenDrag d && d.Index == _stickFinger)
            {
                var v = (d.Position - _stickBase) / StickRadius;
                if (v.Length() > 1) { _stickBase = d.Position - v.Normalized() * StickRadius; v = v.Normalized(); } // a base segue o dedo
                Move(v);
                GetViewport().SetInputAsHandled();
            }
        }

        void Move(Vector2 v)
        {
            _stick = v;
            if (v.Length() < 0.15f) v = Vector2.Zero;
            Axis("direita", v.X > 0 ? v.X : 0);
            Axis("esquerda", v.X < 0 ? -v.X : 0);
            Axis("baixo", v.Y > 0 ? v.Y : 0);
            Axis("cima", v.Y < 0 ? -v.Y : 0);
        }

        static void Axis(string action, float strength)
        {
            var name = PortInput.Name(action);
            if (strength > 0) Input.ActionPress(name, strength);
            else Input.ActionRelease(name);
        }

        public void ReleaseAll()
        {
            foreach (var a in _held.Values) Send(a, false);
            _held.Clear();
            _stickFinger = -1;
            Move(Vector2.Zero);
        }

        // Engrenagem desenhada (a fonte padrão não tem o ⚙): 8 dentes em volta de um anel.
        void DrawGear(Vector2 c, float r, Color color)
        {
            for (int i = 0; i < 8; i++)
            {
                var dir = Vector2.Right.Rotated(i * Mathf.Tau / 8);
                DrawLine(c + dir * r * 0.75f, c + dir * r * 1.25f, color, r * 0.42f);
            }
            DrawArc(c, r * 0.72f, 0, Mathf.Tau, 32, color, r * 0.36f);
        }

        public override void _Draw()
        {
            var font = ThemeDB.FallbackFont;
            var fill = new Color(1, 1, 1, 0.16f);
            var ring = new Color(1, 1, 1, 0.45f);
            var press = new Color(1, 0.55f, 0.2f, 0.45f);
            foreach (var p in Current)
            {
                var c = At(p);
                bool down = _held.ContainsValue(p.Action);
                DrawCircle(c, p.R, down ? press : fill);
                DrawArc(c, p.R, 0, Mathf.Tau, 48, ring, 3);
                if (p.Label == Gear) { DrawGear(c, p.R * 0.5f, new Color(1, 1, 1, 0.85f)); continue; }
                int size = p.R > 60 ? 30 : p.Label.Length > 5 ? 18 : 22;
                var w = font.GetStringSize(p.Label, HorizontalAlignment.Left, -1, size).X;
                DrawString(font, c + new Vector2(-w / 2, size * 0.35f), p.Label, HorizontalAlignment.Left, -1, size, new Color(1, 1, 1, 0.85f));
            }
            if (BlockedMode) { }
            else if (_stickFinger >= 0)
            {
                DrawCircle(_stickBase, StickRadius, fill);
                DrawArc(_stickBase, StickRadius, 0, Mathf.Tau, 48, ring, 3);
                DrawCircle(_stickBase + _stick * StickRadius, 48, ring);
            }
            else
            {
                var hint = new Vector2(330, Size.Y - 250); // longe do HP no canto
                DrawArc(hint, StickRadius, 0, Mathf.Tau, 48, new Color(1, 1, 1, 0.2f), 3);
            }
            var fps = $"{Engine.GetFramesPerSecond():0} FPS";
            DrawString(font, new Vector2(Size.X / 2 - 40, 34), fps, HorizontalAlignment.Left, -1, 24, new Color(1, 1, 0.6f, 0.9f));
        }
    }
}
