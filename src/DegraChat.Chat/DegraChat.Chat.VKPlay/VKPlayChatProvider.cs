using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Chat.Abstractions;
using DegraChat.Core.Models;
using Serilog;

namespace DegraChat.Chat.VKPlay;

[ProviderMetadata("VKPlay", RequiresOAuth = true, OAuthUrl = "https://api.vkplay.ru/oauth2/authorize")]
public class VKPlayChatProvider : ChatProviderBase
{
    private ClientWebSocket? _webSocket;
    private string? _channelName;
    private string? _channelId;
    private Task? _receiveLoopTask;
    private Timer? _pingTimer;

    private static readonly Uri VkPlayWsUri = new("wss://pubsub.vkplay.ru/connection/websocket");

    public override ChatPlatform Platform => ChatPlatform.VKPlay;

    public VKPlayChatProvider(ILogger logger) : base(logger)
    {
    }

    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        _channelName = config.ChannelName;

        _webSocket = new ClientWebSocket();
        
        // VKPlay uses Centrifugo protocol
        _webSocket.Options.SetRequestHeader("Origin", "https://live.vkplay.ru");
        
        await _webSocket.ConnectAsync(VkPlayWsUri, cancellationToken);
        Logger.Information("VKPlay: WebSocket connected to Centrifugo");

        // Send connect command (Centrifugo protocol)
        var connectCommand = new
        {
            connect = new
            {
                token = config.AccessToken ?? "",
                name = "python"
            }
        };
        await SendJsonAsync(connectCommand, cancellationToken);

        // Subscribe to channel
        var subscribeCommand = new
        {
            subscribe = new
            {
                channel = $"publicStream:{_channelName}"
            }
        };
        await SendJsonAsync(subscribeCommand, cancellationToken);

        // Start ping timer (Centrifugo requires periodic pings)
        _pingTimer = new Timer(async _ => await SendPingAsync(), null, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(25));

        // Start receive loop
        _receiveLoopTask = ReceiveLoopAsync(ConnectionCts?.Token ?? cancellationToken);
    }

    protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _pingTimer?.Dispose();
        _pingTimer = null;

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "VKPlay: Error closing WebSocket");
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
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to VKPlay");

        var publishCommand = new
        {
            publish = new
            {
                channel = $"publicStream:{_channelName}",
                data = JsonSerializer.Serialize(new { text })
            }
        };
        await SendJsonAsync(publishCommand, cancellationToken);
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
                    Logger.Information("VKPlay: WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessCentrifugoMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Logger.Error(ex, "VKPlay: WebSocket error in receive loop");
            State = ConnectionState.Error;
        }
    }

    private void ProcessCentrifugoMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Centrifugo protocol: various push types
            if (root.TryGetProperty("publication", out var publication))
            {
                HandlePublication(publication);
            }
            else if (root.TryGetProperty("connect", out _))
            {
                Logger.Information("VKPlay: Centrifugo connected");
            }
            else if (root.TryGetProperty("subscribe", out var subResp))
            {
                Logger.Information("VKPlay: Subscribed to channel");
            }
            else if (root.TryGetProperty("ping", out _))
            {
                // Respond with pong
                _ = SendJsonAsync(new { }, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "VKPlay: Error processing message: {Json}", json);
        }
    }

    private void HandlePublication(JsonElement publication)
    {
        try
        {
            var dataStr = publication.TryGetProperty("data", out var dataEl) ? dataEl.GetString() : null;
            if (dataStr == null) return;

            using var dataDoc = JsonDocument.Parse(dataStr);
            var data = dataDoc.RootElement;

            var type = data.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

            if (type == "message")
            {
                HandleChatMessage(data);
            }
            else if (type == "system")
            {
                HandleSystemMessage(data);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "VKPlay: Error handling publication");
        }
    }

    private void HandleChatMessage(JsonElement data)
    {
        var id = data.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var text = data.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

        var user = data.TryGetProperty("user", out var userEl) ? userEl : default;
        var displayName = user.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() ?? "Unknown" : "Unknown";
        var username = user.TryGetProperty("username", out var unEl) ? unEl.GetString() ?? displayName : displayName;
        var color = user.TryGetProperty("color", out var colEl) ? colEl.GetString() ?? "#FFFFFF" : "#FFFFFF";

        var badges = new List<string>();
        if (user.TryGetProperty("isPremium", out var premEl) && premEl.GetBoolean())
        {
            badges.Add("vkplay_premium");
        }
        if (user.TryGetProperty("isOwner", out var ownerEl) && ownerEl.GetBoolean())
        {
            badges.Add("vkplay_owner");
        }
        if (user.TryGetProperty("isModerator", out var modEl) && modEl.GetBoolean())
        {
            badges.Add("vkplay_moderator");
        }

        var isHighlighted = user.TryGetProperty("isOwner", out var own2) && own2.GetBoolean();

        var message = new ChatMessage
        {
            Id = id,
            Platform = ChatPlatform.VKPlay,
            DisplayName = displayName,
            Username = username,
            Text = text,
            UserColor = color.StartsWith("#") ? color : $"#{color}",
            Badges = badges.ToArray(),
            Emotes = ParseVKPlayEmotes(data),
            IsHighlighted = isHighlighted,
            IsSystem = false,
            Timestamp = DateTime.UtcNow,
            Channel = _channelName ?? ""
        };

        OnMessageReceived(message);
    }

    private void HandleSystemMessage(JsonElement data)
    {
        var text = data.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";

        var message = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Platform = ChatPlatform.VKPlay,
            DisplayName = "System",
            Username = "system",
            Text = text,
            UserColor = "#FF6600",
            IsSystem = true,
            Timestamp = DateTime.UtcNow,
            Channel = _channelName ?? ""
        };

        OnMessageReceived(message);
    }

    private static Dictionary<string, string> ParseVKPlayEmotes(JsonElement data)
    {
        var emotes = new Dictionary<string, string>();

        if (data.TryGetProperty("emotes", out var emotesEl) && emotesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var emote in emotesEl.EnumerateArray())
            {
                var emoteId = emote.TryGetProperty("id", out var eid) ? eid.GetString() : null;
                var emoteName = emote.TryGetProperty("code", out var ename) ? ename.GetString() : null;

                if (emoteId != null && emoteName != null && !emotes.ContainsKey(emoteName))
                {
                    emotes[emoteName] = $"https://api.vkplay.ru/v1/emotes/{emoteId}/image";
                }
            }
        }

        return emotes;
    }

    private async Task SendPingAsync()
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        try
        {
            await SendJsonAsync(new { }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "VKPlay: Ping failed");
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
