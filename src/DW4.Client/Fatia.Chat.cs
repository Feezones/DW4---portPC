using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Chat e placa do jogador (DECISÃO DO PORT; regras em <see cref="ChatLog"/>). Enter abre a caixa; Enter envia e Esc cancela.
/// Enquanto a caixa está aberta, nenhuma tecla anda, ataca, confirma diálogo nem abre menu (<see cref="ChatOpen"/>). O canal é o
/// <see cref="Session.Chat"/> (local hoje; na Fase 6, a rede).
/// </summary>
public partial class Fatia
{
    readonly ChatLog _chat = new();
    VBoxContainer? _chatLines;
    LineEdit? _chatInput;
    Control? _chatBox;
    Nameplate? _plate;
    bool ChatOpen => _chat.Typing;

    string PlayerName => Session.Character?.Name is { Length: > 0 } n ? n : FormName;

    void SetupChat()
    {
        var layer = new CanvasLayer { Layer = 15 };
        _chatBox = new VBoxContainer { AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 16, OffsetTop = -300, OffsetRight = 620, OffsetBottom = -16, MouseFilter = Control.MouseFilterEnum.Ignore };
        _chatLines = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill | Control.SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        _chatBox.AddChild(_chatLines);
        _chatInput = new LineEdit { MaxLength = ChatLog.MaxLength, PlaceholderText = "Mensagem (Enter envia, Esc cancela)", Visible = false };
        _chatInput.TextSubmitted += text =>
        {
            Session.Chat.Send(PlayerName, text);
            CloseChat();
        };
        _chatInput.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { CloseChat(); _chatInput.AcceptEvent(); }
        };
        _chatBox.AddChild(_chatInput);
        layer.AddChild(_chatBox);
        AddChild(layer);
        Session.Chat.Received += OnChat;
        TreeExiting += () => Session.Chat.Received -= OnChat;
        foreach (var m in Session.ChatHistory) _chat.Add(m);
        _chat.Close();
        RefreshChat();
        if (_model != null) _plate = Nameplate.Attach(_model);
    }

    void OnChat(ChatMessage m)
    {
        _chat.Add(m);
        RefreshChat();
    }

    // Enter abre o chat só quando nenhuma janela usa o Enter (menu de status, loja, janelas do port, lista de escolha).
    bool ChatInput(InputEvent e)
    {
        if (ChatOpen) return true; // a caixa de texto consome o que precisa; o resto não chega ao jogo
        if (!PortInput.Pressed(e, "chat") && e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.KpEnter }) return false;
        if (_menuOpen || ShopOpen || SubWindowOpen || _choice >= 0) return false;
        _chat.Open();
        _chatInput!.Visible = true;
        _chatInput.Text = "";
        _chatInput.GrabFocus();
        RefreshChat();
        return true;
    }

    void CloseChat()
    {
        _chat.Close();
        if (_chatInput == null) return;
        _chatInput.ReleaseFocus();
        _chatInput.Visible = false;
        RefreshChat();
    }

    void RefreshChat()
    {
        if (_chatLines == null || _chatBox == null) return;
        foreach (var c in _chatLines.GetChildren()) { _chatLines.RemoveChild(c); c.QueueFree(); }
        foreach (var line in _chat.Recent)
        {
            var l = new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
            l.AddThemeConstantOverride("outline_size", 6);
            _chatLines.AddChild(l);
        }
        _chatBox.Visible = _chat.Visible;
    }

    void TickChat(float dt)
    {
        bool was = _chat.Visible;
        _chat.Tick(dt);
        if (was != _chat.Visible && _chatBox != null) _chatBox.Visible = _chat.Visible;
        _plate?.Set(PlayerName, _level);
    }
}
