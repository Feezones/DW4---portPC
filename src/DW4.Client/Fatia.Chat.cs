using System;
using Godot;
using DW4.Gameplay;

namespace DW4.Client;

/// <summary>
/// Chat e placa do jogador (DECISÃO DO PORT; regras em <see cref="ChatLog"/>). Enter abre a caixa; Enter envia e Esc cancela.
/// Enquanto a caixa está aberta, nenhuma tecla anda, ataca, confirma diálogo nem abre menu (<see cref="ChatOpen"/>). As mensagens
/// vão pelo servidor do DW4 (<see cref="Session.Chat"/>, 0.6.8). Canais (pedido do responsável, 2026-11-04): geral (branco), grupo
/// (verde), privada (rosa) e avisos do jogo (amarelo). Tab, com a caixa aberta, troca o canal; /g, /p, /w nome e /r também
/// trocam (<see cref="ChatCommand"/>).
/// </summary>
public partial class Fatia
{
    readonly ChatLog _chat = new();
    VBoxContainer? _chatLines;
    LineEdit? _chatInput;
    Label? _chatTag;
    Control? _chatBox;
    Nameplate? _plate;
    ChatChannel _chatChannel = ChatChannel.Global;
    string? _chatTo, _lastWhisperFrom;

    static Color ChatColor(ChatChannel c) => c switch
    {
        ChatChannel.Party => new Color(0.45f, 1f, 0.45f),
        ChatChannel.Whisper => new Color(1f, 0.55f, 0.85f),
        ChatChannel.System => new Color(1f, 0.88f, 0.3f),
        _ => Colors.White,
    };
    // A janela de itens rápida (Fatia.QuickItems) também segura os comandos do jogo, como o chat.
    bool ChatOpen => _chat.Typing || _quickOpen || _interactOpen;

    string PlayerName => Session.Character?.Name is { Length: > 0 } n ? n : FormName;

    void SetupChat()
    {
        var layer = new CanvasLayer { Layer = 15 };
        _chatBox = new VBoxContainer { AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 16, OffsetTop = -300, OffsetRight = 620, OffsetBottom = -16, MouseFilter = Control.MouseFilterEnum.Ignore };
        _chatLines = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill | Control.SizeFlags.ShrinkEnd, MouseFilter = Control.MouseFilterEnum.Ignore };
        _chatBox.AddChild(_chatLines);
        var row = new HBoxContainer { Visible = false };
        _chatTag = new Label { VerticalAlignment = VerticalAlignment.Center };
        // Celular: sem Tab no teclado do Android; tocar no canal ([Geral]/[Grupo]/[Privado]) troca.
        if (Touch)
        {
            _chatTag.MouseFilter = Control.MouseFilterEnum.Stop;
            _chatTag.GuiInput += ev => { if (ev is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) { NextChatChannel(); _chatTag.AcceptEvent(); } };
        }
        _chatTag.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0));
        _chatTag.AddThemeConstantOverride("outline_size", 6);
        row.AddChild(_chatTag);
        _chatInput = new LineEdit
        {
            MaxLength = ChatLog.MaxLength, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            PlaceholderText = "Mensagem (Enter envia, Tab troca o canal, Esc cancela)",
        };
        _chatInput.TextSubmitted += SubmitChat;
        _chatInput.GuiInput += e =>
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { CloseChat(); _chatInput.AcceptEvent(); }
            else if (e is InputEventKey { Pressed: true, Keycode: Key.Tab }) { NextChatChannel(); _chatInput.AcceptEvent(); }
        };
        row.AddChild(_chatInput);
        _chatBox.AddChild(row);
        layer.AddChild(_chatBox);
        AddChild(layer);
        Session.ChatReceived += OnChat;
        TreeExiting += () => Session.ChatReceived -= OnChat;
        foreach (var m in Session.ChatHistory) _chat.Add(m);
        _chat.Close();
        RefreshChat();
        if (_model != null) _plate = Nameplate.Attach(_model);
    }

    void OnChat(ChatMessage m)
    {
        if (m.Channel == ChatChannel.Whisper && !string.Equals(m.Sender, PlayerName, StringComparison.OrdinalIgnoreCase))
            _lastWhisperFrom = m.Sender;
        _chat.Add(m);
        RefreshChat();
    }

    // Enter na caixa: comando (/g, /p, /w nome, /r) troca o canal da caixa; texto vai para o canal atual.
    void SubmitChat(string line)
    {
        var cmd = ChatCommand.Parse(line, _chatChannel, _chatTo, _lastWhisperFrom);
        if (cmd.Error != null) Session.SystemMessage(cmd.Error);
        else
        {
            if (line.TrimStart().StartsWith('/'))
            {
                _chatChannel = cmd.Channel;
                if (cmd.Channel == ChatChannel.Whisper) _chatTo = cmd.To;
            }
            if (cmd.Text.Length > 0) Session.Chat.Send(PlayerName, cmd);
        }
        CloseChat();
    }

    // Tab: geral → grupo → privada (para quem falou por último) → geral.
    void NextChatChannel()
    {
        _chatChannel = _chatChannel switch { ChatChannel.Global => ChatChannel.Party, ChatChannel.Party => ChatChannel.Whisper, _ => ChatChannel.Global };
        if (_chatChannel == ChatChannel.Whisper) _chatTo ??= _lastWhisperFrom;
        UpdateChatTag();
    }

    void UpdateChatTag()
    {
        if (_chatTag == null || _chatInput == null) return;
        _chatTag.Text = _chatChannel switch
        {
            ChatChannel.Party => "[Grupo]",
            ChatChannel.Whisper => _chatTo is { Length: > 0 } to ? $"[Para {to}]" : "[Privado]",
            _ => "[Geral]",
        };
        _chatTag.AddThemeColorOverride("font_color", ChatColor(_chatChannel));
        _chatInput.AddThemeColorOverride("font_color", ChatColor(_chatChannel));
        _chatInput.PlaceholderText = _chatChannel == ChatChannel.Whisper && _chatTo is not { Length: > 0 }
            ? "Digite /w nome mensagem"
            : Touch ? "Mensagem (toque no canal para trocar)" : "Mensagem (Enter envia, Tab troca o canal, Esc cancela)";
    }

    // Enter abre o chat só quando nenhuma janela usa o Enter (menu de status, loja, janelas do port, lista de escolha).
    bool ChatInput(InputEvent e)
    {
        if (_chat.Typing) return true; // a caixa de texto consome o que precisa; o resto não chega ao jogo
        if (!PortInput.Pressed(e, "chat") && e is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.KpEnter }) return false;
        if (_menuOpen || ShopOpen || SubWindowOpen || _choice >= 0) return false;
        _chat.Open();
        ((Control)_chatInput!.GetParent()).Visible = true;
        _chatInput.Text = "";
        UpdateChatTag();
        _chatInput.GrabFocus();
        if (DataPaths.Mobile) DisplayServer.VirtualKeyboardShow(""); // celular: abre o teclado do Android
        RefreshChat();
        return true;
    }

    void CloseChat()
    {
        _chat.Close();
        if (_chatInput == null) return;
        _chatInput.ReleaseFocus();
        if (DataPaths.Mobile) DisplayServer.VirtualKeyboardHide();
        ((Control)_chatInput.GetParent()).Visible = false;
        RefreshChat();
    }

    void RefreshChat()
    {
        if (_chatLines == null || _chatBox == null) return;
        foreach (var c in _chatLines.GetChildren()) { _chatLines.RemoveChild(c); c.QueueFree(); }
        foreach (var m in _chat.RecentMessages)
        {
            var l = new Label { Text = m.Format(PlayerName), AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeColorOverride("font_color", ChatColor(m.Channel));
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
