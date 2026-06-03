using System.Text.Json;
using System.Text.Json.Serialization;
using DegraChat.Core.Models;

namespace DegraChat.Core.Services;

/// <summary>
/// Serializes ChatMessage to JSON for WebSocket transmission and storage.
/// </summary>
public static class ChatMessageSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    static ChatMessageSerializer()
    {
        _options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <summary>
    /// Serialize a ChatMessage to JSON string for WebSocket clients.
    /// </summary>
    public static string SerializeForOverlay(ChatMessage message)
    {
        var payload = new OverlayMessagePayload
        {
            Type = "message",
            Data = new OverlayMessageData
            {
                Id = message.Id,
                Platform = message.Platform.ToString().ToLowerInvariant(),
                DisplayName = message.DisplayName,
                Username = message.Username,
                Text = message.Text,
                UserColor = message.UserColor,
                Badges = message.Badges.ToArray(),
                Emotes = message.Emotes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                IsHighlighted = message.IsHighlighted,
                IsSystem = message.IsSystem,
                Timestamp = message.Timestamp.ToString("O"),
                Channel = message.Channel
            }
        };
        return JsonSerializer.Serialize(payload, _options);
    }

    /// <summary>
    /// Create a clear-chat command payload.
    /// </summary>
    public static string SerializeClearCommand()
    {
        return JsonSerializer.Serialize(new { type = "clear" }, _options);
    }

    /// <summary>
    /// Create a test message command payload.
    /// </summary>
    public static string SerializeTestMessage(string text, string displayName, string color)
    {
        var payload = new OverlayMessagePayload
        {
            Type = "message",
            Data = new OverlayMessageData
            {
                Id = Guid.NewGuid().ToString(),
                Platform = "test",
                DisplayName = displayName,
                Username = displayName.ToLowerInvariant(),
                Text = text,
                UserColor = color,
                Badges = Array.Empty<string>(),
                Emotes = new Dictionary<string, string>(),
                IsHighlighted = false,
                IsSystem = false,
                Timestamp = DateTime.UtcNow.ToString("O"),
                Channel = "test"
            }
        };
        return JsonSerializer.Serialize(payload, _options);
    }

    private class OverlayMessagePayload
    {
        public string Type { get; set; } = string.Empty;
        public OverlayMessageData? Data { get; set; }
    }

    private class OverlayMessageData
    {
        public string Id { get; set; } = string.Empty;
        public string Platform { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string UserColor { get; set; } = "#FFFFFF";
        public string[] Badges { get; set; } = Array.Empty<string>();
        public Dictionary<string, string> Emotes { get; set; } = new();
        public bool IsHighlighted { get; set; }
        public bool IsSystem { get; set; }
        public string Timestamp { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
    }
}
