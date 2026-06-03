using DegraChat.Core.Models;

namespace DegraChat.Core.Events;

/// <summary>
/// Event fired when a new chat message is received from any provider.
/// </summary>
public class ChatMessageReceivedEvent
{
    public ChatMessage Message { get; }

    public ChatMessageReceivedEvent(ChatMessage message)
    {
        Message = message;
    }
}
