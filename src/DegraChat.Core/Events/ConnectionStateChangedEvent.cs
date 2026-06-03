using DegraChat.Core.Models;

namespace DegraChat.Core.Events;

/// <summary>
/// Event fired when a chat provider changes connection state.
/// </summary>
public class ConnectionStateChangedEvent
{
    public ChatPlatform Platform { get; }
    public ConnectionState OldState { get; }
    public ConnectionState NewState { get; }
    public string? ErrorMessage { get; }

    public ConnectionStateChangedEvent(
        ChatPlatform platform,
        ConnectionState oldState,
        ConnectionState newState,
        string? errorMessage = null)
    {
        Platform = platform;
        OldState = oldState;
        NewState = newState;
        ErrorMessage = errorMessage;
    }
}
