using System.Collections.Generic;

namespace DegraChat.Core.Models;

/// <summary>
/// Unified chat message model from any platform.
/// </summary>
public class ChatMessage
{
    /// <summary>Unique message identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Source platform.</summary>
    public ChatPlatform Platform { get; init; }

    /// <summary>Display name of the sender.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Username/login of the sender.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Message text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Color assigned to the username (hex, e.g. "#FF0000").</summary>
    public string UserColor { get; init; } = "#FFFFFF";

    /// <summary>URLs of badge icons for this user.</summary>
    public IReadOnlyList<string> Badges { get; init; } = Array.Empty<string>();

    /// <summary>Custom emotes in the message (name → image URL).</summary>
    public IReadOnlyDictionary<string, string> Emotes { get; init; } = new Dictionary<string, string>();

    /// <summary>Whether this message is from a broadcaster/moderator/VIP.</summary>
    public bool IsHighlighted { get; init; }

    /// <summary>Whether this is a system notice (sub, raid, etc.).</summary>
    public bool IsSystem { get; init; }

    /// <summary>Timestamp of the message.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Channel name the message was sent in.</summary>
    public string Channel { get; init; } = string.Empty;
}
