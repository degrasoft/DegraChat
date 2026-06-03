namespace DegraChat.Core.Models;

/// <summary>
/// Configuration for a single platform connection.
/// </summary>
public class ConnectionConfig
{
    public ChatPlatform Platform { get; init; }
    public string ChannelName { get; set; } = string.Empty;
    public string? OAuthToken { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? ClientId { get; set; }
    public bool AutoConnect { get; set; }
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectAttempts { get; set; } = 10;
}
