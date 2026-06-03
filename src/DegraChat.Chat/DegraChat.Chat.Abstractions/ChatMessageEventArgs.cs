using System;
using DegraChat.Core.Models;

namespace DegraChat.Chat.Abstractions;

/// <summary>
/// Event arguments for received chat messages.
/// </summary>
public class ChatMessageEventArgs : EventArgs
{
    public ChatMessage Message { get; }

    public ChatMessageEventArgs(ChatMessage message)
    {
        Message = message;
    }
}
