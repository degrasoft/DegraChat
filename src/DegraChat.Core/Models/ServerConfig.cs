namespace DegraChat.Core.Models;

/// <summary>
/// Configuration for the local WebSocket server.
/// </summary>
public class ServerConfig
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 9274;
    public bool AutoStart { get; set; } = true;
}
