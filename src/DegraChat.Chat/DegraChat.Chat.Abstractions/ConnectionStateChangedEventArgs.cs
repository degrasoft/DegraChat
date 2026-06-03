using System;
using DegraChat.Core.Models;

namespace DegraChat.Chat.Abstractions;

/// <summary>
/// Event arguments for connection state changes.
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionState OldState { get; }
    public ConnectionState NewState { get; }
    public string? ErrorMessage { get; }

    public ConnectionStateChangedEventArgs(
        ConnectionState oldState,
        ConnectionState newState,
        string? errorMessage = null)
    {
        OldState = oldState;
        NewState = newState;
        ErrorMessage = errorMessage;
    }
}
