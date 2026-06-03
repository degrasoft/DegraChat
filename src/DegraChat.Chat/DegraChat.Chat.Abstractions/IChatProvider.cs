using System;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Core.Models;

namespace DegraChat.Chat.Abstractions;

/// <summary>
/// Unified contract for all streaming platform chat providers.
/// Each provider implements this interface to connect, receive messages,
/// and report connection state changes.
/// </summary>
public interface IChatProvider : IAsyncDisposable
{
    /// <summary>
    /// The platform this provider handles.
    /// </summary>
    ChatPlatform Platform { get; }

    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState State { get; }

    /// <summary>
    /// Fired when the connection state changes.
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// Fired when a chat message is received.
    /// </summary>
    event EventHandler<ChatMessageEventArgs>? MessageReceived;

    /// <summary>
    /// Connect to the platform chat using the provided configuration.
    /// </summary>
    Task ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnect from the platform chat.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a message to the connected chat (if the platform supports it).
    /// </summary>
    Task SendMessageAsync(string text, CancellationToken cancellationToken = default);
}
