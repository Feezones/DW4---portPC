using System;
using DW4.Gameplay;
using DW4.Net;

namespace DW4.Client;

/// <summary>
/// Chat pelo servidor do DW4 (0.6.8): o envio vai pelo <see cref="Online"/> e as mensagens de todos voltam pelo
/// <see cref="Received"/> (inclusive a própria, que o servidor devolve). Sem conexão, o geral aparece só para o jogador e grupo e
/// privada avisam que precisam do servidor.
/// </summary>
public sealed class NetChatChannel : IChatChannel
{
    public event Action<ChatMessage>? Received;

    public NetChatChannel()
    {
        Online.ChatIn += c => Received?.Invoke(new ChatMessage(c.Sender, c.Text, DateTime.Now, (ChatChannel)(int)c.Channel,
            c.To.Length > 0 ? c.To : null));
    }

    public void Send(string sender, string text) => Send(sender, ChatCommand.Parse(text));

    public void Send(string sender, ChatCommand command)
    {
        if (command.Error != null || command.Text.Length == 0 || command.Channel == ChatChannel.System) return;
        if (Online.SendChat((ChatTo)(int)command.Channel, command.To ?? "", command.Text)) return;
        if (command.Channel == ChatChannel.Global) Received?.Invoke(new ChatMessage(sender, command.Text, DateTime.Now));
        else Session.SystemMessage("Sem conexão com o servidor: grupo e privado não foram enviados.");
    }
}
