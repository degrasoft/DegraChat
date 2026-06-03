using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Chat.Abstractions;
using DegraChat.Core.Models;
using Polly;
using Serilog;

namespace DegraChat.Chat.Kick;

[ProviderMetadata("Kick", RequiresOAuth = false)]
public class KickChatProvider : ChatProviderBase
{
    private ClientWebSocket? _webSocket;
    private string? _channelName;
    private int _channelId;
    private string? _chatroomId;
    private Task? _receiveLoopTask;
    private Timer? _heartbeatTimer;
    private int _phoenixRef;
    private readonly HttpClient _httpClient;

    private static readonly Uri KickWsUri = new("wss://ws-us2.pusherapp.com/app/32cbd69e76a5f0202820?protocol=7&client=js&version=7.6.0&flash=false");

    public override ChatPlatform Platform => ChatPlatform.Kick;

    public KickChatProvider(ILogger logger) : base(logger)
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "DegraChat/1.0");
    }

    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        _channelName = config.ChannelName;

        // First, resolve channel to chatroom ID via Kick API
        _chatroomId = await ResolveChatroomIdAsync(_channelName, cancellationToken);
        if (string.IsNullOrEmpty(_chatroomId))
        {
            throw new InvalidOperationException($"Could not resolve Kick channel '{_channelName}' to a chatroom ID");
        }

        Logger.Information("Kick: Resolved channel {Channel} → chatroom {ChatroomId}", _channelName, _chatroomId);

        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(KickWsUri, cancellationToken);
        Logger.Information("Kick: WebSocket connected to Pusher");

        // Subscribe to chatroom channel
        var subscribeMsg = new
        {
            @event = "pusher:subscribe",
            data = new
            {
                channel = $"chatrooms.{_chatroomId}.v2"
            }
        };
        await SendJsonAsync(subscribeMsg, cancellationToken);

        // Start heartbeat
        _heartbeatTimer = new Timer(async _ => await SendHeartbeatAsync(), null, TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(120));

        // Start receive loop
        _receiveLoopTask = ReceiveLoopAsync(ConnectionCts?.Token ?? cancellationToken);
    }

    protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Kick: Error closing WebSocket");
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;

        if (_receiveLoopTask != null)
        {
            try { await _receiveLoopTask; } catch (OperationCanceledException) { }
        }
        _receiveLoopTask = null;
    }

    public override async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        // Kick chat sending requires API authentication - not fully implemented
        // as it requires user session tokens. This is a placeholder for future implementation.
        throw new NotSupportedException("Sending messages to Kick requires API authentication. Use the Kick website directly.");
    }

    private async Task<string?> ResolveChatroomIdAsync(string channelName, CancellationToken cancellationToken)
    {
        try
        {
            var retryPolicy = Policy
                .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
                .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

            var response = await retryPolicy.ExecuteAsync(async () =>
                await _httpClient.GetAsync($"https://kick.com/api/v2/channels/{channelName}", cancellationToken));

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("Kick: Failed to resolve channel {Channel}, status {Status}", channelName, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("chatroom", out var chatroom) &&
                chatroom.TryGetProperty("id", out var idEl))
            {
                return idEl.GetInt32().ToString();
            }

            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Kick: Error resolving channel {Channel}", channelName);
            return null;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16384];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Logger.Information("Kick: WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessPusherEvent(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Logger.Error(ex, "Kick: WebSocket error in receive loop");
            State = ConnectionState.Error;
        }
    }

    private void ProcessPusherEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var eventName = root.TryGetProperty("event", out var evEl) ? evEl.GetString() : null;

            switch (eventName)
            {
                case "pusher:connection_established":
                    Logger.Information("Kick: Pusher connection established");
                    break;

                case "App\\Events\\ChatMessageEvent":
                    if (root.TryGetProperty("data", out var dataEl))
                    {
                        var innerJson = dataEl.GetString();
                        if (innerJson != null)
                        {
                            HandleChatMessage(innerJson);
                        }
                    }
                    break;

                case "pusher:pong":
                    // Heartbeat response, ignore
                    break;

                default:
                    Logger.Debug("Kick: Unhandled event {Event}", eventName);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Kick: Error processing Pusher event");
        }
    }

    private void HandleChatMessage(string innerJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(innerJson);
            var root = doc.RootElement;

            var id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt32().ToString() : Guid.NewGuid().ToString();
            var content = root.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "";

            var sender = root.TryGetProperty("sender", out var senderEl) ? senderEl : default;
            var username = sender.TryGetProperty("username", out var unameEl) ? unameEl.GetString() ?? "Unknown" : "Unknown";
            var displayName = sender.TryGetProperty("slug", out var slugEl) ? slugEl.GetString() ?? username : username;
            var color = sender.TryGetProperty("identity", out var identityEl) &&
                        identityEl.TryGetProperty("color", out var colorEl)
                ? colorEl.GetString() ?? "#FFFFFF"
                : "#FFFFFF";

            var badges = new List<string>();
            if (sender.TryGetProperty("is_verified", out var verifiedEl) && verifiedEl.GetBoolean())
            {
                badges.Add("kick_verified");
            }
            if (sender.TryGetProperty("is_moderator", out var modEl) && modEl.GetBoolean())
            {
                badges.Add("kick_moderator");
            }
            if (sender.TryGetProperty("is_subscriber", out var subEl) && subEl.GetBoolean())
            {
                badges.Add("kick_subscriber");
            }

            var isHighlighted = sender.TryGetProperty("is_moderator", out var mod2) && mod2.GetBoolean();

            var message = new ChatMessage
            {
                Id = id,
                Platform = ChatPlatform.Kick,
                DisplayName = displayName,
                Username = username,
                Text = content,
                UserColor = color.StartsWith("#") ? color : $"#{color}",
                Badges = badges.ToArray(),
                Emotes = ParseKickEmotes(root),
                IsHighlighted = isHighlighted,
                IsSystem = false,
                Timestamp = DateTime.UtcNow,
                Channel = _channelName ?? ""
            };

            OnMessageReceived(message);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Kick: Error parsing chat message");
        }
    }

    private static Dictionary<string, string> ParseKickEmotes(JsonElement root)
    {
        var emotes = new Dictionary<string, string>();

        if (root.TryGetProperty("emotes", out var emotesEl) && emotesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var emote in emotesEl.EnumerateArray())
            {
                var emoteId = emote.TryGetProperty("id", out var eid) ? eid.GetInt32().ToString() : null;
                var emoteName = emote.TryGetProperty("name", out var ename) ? ename.GetString() : null;

                if (emoteId != null && emoteName != null && !emotes.ContainsKey(emoteName))
                {
                    emotes[emoteName] = $"https://images.kick.com/emotes/{emoteId}/fullsize";
                }
            }
        }

        return emotes;
    }

    private async Task SendHeartbeatAsync()
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        try
        {
            var ping = new { @event = "pusher:ping", data = "{}" };
            await SendJsonAsync(ping, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Kick: Heartbeat failed");
        }
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }
}
