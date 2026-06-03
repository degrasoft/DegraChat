namespace DegraChat.Core.Models;

/// <summary>
/// Connection state of a chat provider.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Error
}
