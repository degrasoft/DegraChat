using System;

namespace DegraChat.Chat.Abstractions;

/// <summary>
/// Metadata attribute for chat provider implementations.
/// Used for display purposes and provider discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ProviderMetadataAttribute : Attribute
{
    /// <summary>Human-readable platform name.</summary>
    public string DisplayName { get; }

    /// <summary>Whether the provider supports sending messages.</summary>
    public bool CanSendMessages { get; init; } = true;

    /// <summary>Whether OAuth is required for connecting.</summary>
    public bool RequiresOAuth { get; init; } = true;

    /// <summary>URL to obtain OAuth token.</summary>
    public string? OAuthUrl { get; init; }

    public ProviderMetadataAttribute(string displayName)
    {
        DisplayName = displayName;
    }
}
