using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Chat.Abstractions;
using DegraChat.Core.Models;
using Serilog;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace DegraChat.Chat.Twitch;

[ProviderMetadata("Twitch", RequiresOAuth = true, OAuthUrl = "https://id.twitch.tv/oauth2/authorize")]
public class TwitchChatProvider : ChatProviderBase
{
    private TwitchClient? _client;
    private string? _channelName;

    public override ChatPlatform Platform => ChatPlatform.Twitch;

    public TwitchChatProvider(ILogger logger) : base(logger)
    {
    }

    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        _channelName = config.ChannelName.ToLowerInvariant();

        var credentials = new ConnectionCredentials(
            config.ChannelName,
            config.OAuthToken ?? throw new InvalidOperationException("Twitch OAuth token is required"));

        var clientOptions = new ClientOptions
        {
            MessagesAllowedInPeriod = 750,
            ThrottlingPeriod = TimeSpan.FromSeconds(30)
        };

        var customClient = new WebSocketClient(clientOptions);
        _client = new TwitchClient(customClient);
        _client.Initialize(credentials, _channelName);

        // Wire events
        _client.OnConnected += OnConnected;
        _client.OnJoinedChannel += OnJoinedChannel;
        _client.OnMessageReceived += OnMessageReceived;
        _client.OnChatCommandReceived += OnChatCommandReceived;
        _client.OnUserJoined += OnUserJoined;
        _client.OnUserLeft += OnUserLeft;
        _client.OnDisconnected += OnDisconnected;
        _client.OnConnectionError += OnConnectionError;
        _client.OnReconnected += OnReconnected;

        _client.Connect();

        await Task.CompletedTask;
    }

    protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        if (_client != null)
        {
            _client.OnConnected -= OnConnected;
            _client.OnJoinedChannel -= OnJoinedChannel;
            _client.OnMessageReceived -= OnMessageReceived;
            _client.OnChatCommandReceived -= OnChatCommandReceived;
            _client.OnUserJoined -= OnUserJoined;
            _client.OnUserLeft -= OnUserLeft;
            _client.OnDisconnected -= OnDisconnected;
            _client.OnConnectionError -= OnConnectionError;
            _client.OnReconnected -= OnReconnected;

            _client.Disconnect();
            _client = null;
        }

        await Task.CompletedTask;
    }

    public override async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_client == null || State != ConnectionState.Connected)
            throw new InvalidOperationException("Not connected to Twitch");

        _client.SendMessage(_channelName!, text);
        await Task.CompletedTask;
    }

    private void OnConnected(object? sender, OnConnectedArgs e)
    {
        Logger.Information("Twitch: Connected as {BotUsername}", e.BotUsername);
    }

    private void OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        Logger.Information("Twitch: Joined channel #{Channel}", e.Channel);
    }

    private void OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        var chatMessage = e.ChatMessage;
        var message = new ChatMessage
        {
            Id = chatMessage.Id,
            Platform = ChatPlatform.Twitch,
            DisplayName = chatMessage.DisplayName,
            Username = chatMessage.Username,
            Text = chatMessage.Message,
            UserColor = chatMessage.ColorHex ?? "#FFFFFF",
            Badges = ParseBadges(chatMessage),
            Emotes = ParseEmotes(chatMessage),
            IsHighlighted = chatMessage.IsBroadcaster || chatMessage.IsModerator || chatMessage.IsVip,
            IsSystem = false,
            Timestamp = DateTime.UtcNow,
            Channel = chatMessage.Channel
        };

        OnMessageReceived(message);
    }

    private void OnChatCommandReceived(object? sender, OnChatCommandReceivedArgs e)
    {
        // Commands are also messages, already handled by OnMessageReceived
    }

    private void OnUserJoined(object? sender, OnUserJoinedArgs e)
    {
        Logger.Debug("Twitch: User {Username} joined #{Channel}", e.Username, e.Channel);
    }

    private void OnUserLeft(object? sender, OnUserLeftArgs e)
    {
        Logger.Debug("Twitch: User {Username} left #{Channel}", e.Username, e.Channel);
    }

    private void OnDisconnected(object? sender, OnDisconnectedArgs e)
    {
        Logger.Warning("Twitch: Disconnected");
        if (State == ConnectionState.Connected)
        {
            State = ConnectionState.Reconnecting;
            if (CurrentConfig != null)
            {
                _ = ReconnectLoopAsync(CurrentConfig);
            }
        }
    }

    private void OnConnectionError(object? sender, OnConnectionErrorArgs e)
    {
        Logger.Error("Twitch: Connection error - {Message}", e.Error.Message);
        State = ConnectionState.Error;
    }

    private void OnReconnected(object? sender, OnReconnectedArgs e)
    {
        Logger.Information("Twitch: Reconnected");
        State = ConnectionState.Connected;
    }

    private static string[] ParseBadges(ChatMessage chatMessage)
    {
        var badges = new List<string>();
        foreach (var badge in chatMessage.Badges)
        {
            // Twitch badge URLs follow a known pattern
            badges.Add($"https://static-cdn.jtvnw.net/chat-badges/v1/{badge.Key}/{badge.Value}/3");
        }
        return badges.ToArray();
    }

    private static Dictionary<string, string> ParseEmotes(ChatMessage chatMessage)
    {
        var emotes = new Dictionary<string, string>();
        if (chatMessage.EmoteSet == null) return emotes;

        foreach (var emote in chatMessage.EmoteSet.Emotes)
        {
            if (!emotes.ContainsKey(emote.Name))
            {
                emotes[emote.Name] = $"https://static-cdn.jtvnw.net/emoticons/v2/{emote.Id}/default/dark/3.0";
            }
        }
        return emotes;
    }
}
