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

namespace DegraChat.Chat.GoodGame;

[ProviderMetadata("GoodGame", RequiresOAuth = false)]
public class GoodGameChatProvider : ChatProviderBase
{
    private ClientWebSocket? _webSocket;
    private string? _channelName;
    private Task? _receiveLoopTask;

    private static readonly Uri GgWsUri = new("wss://chat.goodgame.ru/chat/websocket");

    public override ChatPlatform Platform => ChatPlatform.GoodGame;

    public GoodGameChatProvider(ILogger logger) : base(logger)
    {
    }

    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        _channelName = config.ChannelName;

        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(GgWsUri, cancellationToken);
        Logger.Information("GoodGame: WebSocket connected");

        // Send channel join request
        var joinMessage = new
        {
            type = "join",
            data = new
            {
                channel_id = _channelName, // GoodGame uses channel name as ID in some contexts
                hidden = false
            }
        };

        await SendJsonAsync(joinMessage, cancellationToken);

        // Start receive loop
        _receiveLoopTask = ReceiveLoopAsync(ConnectionCts?.Token ?? cancellationToken);
    }

    protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "GoodGame: Error closing WebSocket");
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch (OperationCanceledException) { }
        }
        _receiveLoopTask = null;
    }

    public override async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("Not connected to GoodGame");

        var msg = new
        {
            type = "send_message",
            data = new
            {
                channel_id = _channelName,
                text = text
            }
        };

        await SendJsonAsync(msg, cancellationToken);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Logger.Information("GoodGame: WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessMessage(json);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException ex)
        {
            Logger.Error(ex, "GoodGame: WebSocket error in receive loop");
            State = ConnectionState.Error;
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeElement)) return;
            var type = typeElement.GetString();

            switch (type)
            {
                case "welcome":
                    Logger.Information("GoodGame: Received welcome message");
                    break;

                case "success_join":
                    Logger.Information("GoodGame: Successfully joined channel");
                    break;

                case "message":
                    HandleChatMessage(root);
                    break;

                case "user_ban":
                    HandleUserBan(root);
                    break;

                case "channel_info":
                    // Channel metadata, can be used for viewer count etc.
                    break;

                case "error":
                    var errorMsg = root.TryGetProperty("data", out var errData)
                        ? errData.TryGetProperty("error_msg", out var errMsg) ? errMsg.GetString() : "Unknown error"
                        : "Unknown error";
                    Logger.Error("GoodGame: Error - {Error}", errorMsg);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "GoodGame: Error processing message: {Json}", json);
        }
    }

    private void HandleChatMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return;

        var id = data.TryGetProperty("message_id", out var idEl) ? idEl.GetString() ?? Guid.NewGuid().ToString() : Guid.NewGuid().ToString();
        var userName = data.TryGetProperty("user_name", out var nameEl) ? nameEl.GetString() ?? "Unknown" : "Unknown";
        var displayName = data.TryGetProperty("user_name", out var dispEl) ? dispEl.GetString() ?? userName : userName;
        var text = data.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
        var color = data.TryGetProperty("color", out var colorEl) ? $"#{colorEl.GetString() ?? "FFFFFF"}" : "#FFFFFF";
        var channel = data.TryGetProperty("channel_id", out var chEl) ? chEl.GetString() ?? "" : "";

        // Parse badges from GoodGame
        var badges = new List<string>();
        if (data.TryGetProperty("premium", out var premEl) && premEl.GetBoolean())
        {
            badges.Add("gg_premium");
        }
        if (data.TryGetProperty("moderator", out var modEl) && modEl.GetBoolean())
        {
            badges.Add("gg_moderator");
        }

        // Parse emotes from text
        var emotes = ParseGoodGameEmotes(data, text);

        var message = new ChatMessage
        {
            Id = id,
            Platform = ChatPlatform.GoodGame,
            DisplayName = displayName,
            Username = userName,
            Text = text,
            UserColor = color,
            Badges = badges.ToArray(),
            Emotes = emotes,
            IsHighlighted = data.TryGetProperty("moderator", out var mod2) && mod2.GetBoolean(),
            IsSystem = false,
            Timestamp = DateTime.UtcNow,
            Channel = channel
        };

        OnMessageReceived(message);
    }

    private void HandleUserBan(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data)) return;

        var userName = data.TryGetProperty("user_name", out var nameEl) ? nameEl.GetString() ?? "Unknown" : "Unknown";
        var message = new ChatMessage
        {
            Id = Guid.NewGuid().ToString(),
            Platform = ChatPlatform.GoodGame,
            DisplayName = "System",
            Username = "system",
            Text = $"{userName} was banned",
            UserColor = "#FF6600",
            IsSystem = true,
            Timestamp = DateTime.UtcNow,
            Channel = _channelName ?? ""
        };

        OnMessageReceived(message);
    }

    private static Dictionary<string, string> ParseGoodGameEmotes(JsonElement data, string text)
    {
        var emotes = new Dictionary<string, string>();

        if (data.TryGetProperty("emotes", out var emotesEl) && emotesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var emote in emotesEl.EnumerateArray())
            {
                var emoteId = emote.TryGetProperty("emote_id", out var eid) ? eid.GetString() : null;
                var emoteName = emote.TryGetProperty("emote_code", out var ename) ? ename.GetString() : null;

                if (emoteId != null && emoteName != null && !emotes.ContainsKey(emoteName))
                {
                    emotes[emoteName] = $"https://goodgame.ru/images/emotes/{emoteId}.png";
                }
            }
        }

        return emotes;
    }

    private async Task SendJsonAsync(object payload, CancellationToken cancellationToken)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }
}
