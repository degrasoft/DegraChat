using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Chat.Abstractions;
using DegraChat.Core.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using Serilog;

namespace DegraChat.Chat.YouTube;

[ProviderMetadata("YouTube", RequiresOAuth = true, OAuthUrl = "https://accounts.google.com/o/oauth2/auth")]
public class YouTubeChatProvider : ChatProviderBase
{
    private YouTubeService? _youTubeService;
    private string? _liveChatId;
    private string? _channelName;
    private Task? _pollingTask;
    private TimeSpan _pollingInterval = TimeSpan.FromSeconds(2);
    private string? _nextPageToken;

    public override ChatPlatform Platform => ChatPlatform.YouTube;

    public YouTubeChatProvider(ILogger logger) : base(logger)
    {
    }

    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        _channelName = config.ChannelName;

        // Initialize YouTube service with OAuth
        var credential = await AuthorizeAsync(config, cancellationToken);
        
        _youTubeService = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "DegraChat"
        });

        // Resolve the live chat ID for the channel
        _liveChatId = await ResolveLiveChatIdAsync(cancellationToken);
        if (string.IsNullOrEmpty(_liveChatId))
        {
            throw new InvalidOperationException($"Could not find an active live stream for channel '{_channelName}'. Make sure the stream is live.");
        }

        Logger.Information("YouTube: Resolved live chat ID: {ChatId}", _liveChatId);

        // Start polling
        _pollingTask = PollMessagesAsync(ConnectionCts?.Token ?? cancellationToken);
    }

    protected override async Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        ConnectionCts?.Cancel();

        if (_pollingTask != null)
        {
            try { await _pollingTask; } catch (OperationCanceledException) { }
        }
        _pollingTask = null;

        _youTubeService?.Dispose();
        _youTubeService = null;
    }

    public override async Task SendMessageAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_youTubeService == null || string.IsNullOrEmpty(_liveChatId))
            throw new InvalidOperationException("Not connected to YouTube Live Chat");

        var message = new LiveChatMessage
        {
            Snippet = new LiveChatMessageSnippet
            {
                LiveChatId = _liveChatId,
                Type = "textMessageEvent",
                TextMessageDetails = new LiveChatTextMessageDetails
                {
                    MessageText = text
                }
            }
        };

        var request = _youTubeService.LiveChatMessages.Insert(message, "snippet");
        await request.ExecuteAsync(cancellationToken);
    }

    private async Task<UserCredential> AuthorizeAsync(ConnectionConfig config, CancellationToken cancellationToken)
    {
        var scopes = new[] { YouTubeService.Scope.YoutubeReadonly };

        if (!string.IsNullOrEmpty(config.ClientId) && !string.IsNullOrEmpty(config.AccessToken))
        {
            // Use stored credentials
            var token = new Google.Apis.Auth.OAuth2.Responses.TokenResponse
            {
                AccessToken = config.AccessToken,
                RefreshToken = config.RefreshToken,
                TokenType = "Bearer"
            };

            var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = config.ClientId,
                    ClientSecret = config.OAuthToken ?? ""
                },
                Scopes = scopes,
                DataStore = new FileDataStore("DegraChat.YouTube")
            });

            return new UserCredential(flow, "user", token);
        }

        // Interactive OAuth flow (opens browser)
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMinutes(5));

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets
            {
                ClientId = config.ClientId ?? throw new InvalidOperationException("YouTube ClientId is required"),
                ClientSecret = config.OAuthToken ?? throw new InvalidOperationException("YouTube ClientSecret is required")
            },
            scopes,
            "user",
            cts.Token,
            new FileDataStore("DegraChat.YouTube"));
    }

    private async Task<string?> ResolveLiveChatIdAsync(CancellationToken cancellationToken)
    {
        if (_youTubeService == null) return null;

        try
        {
            // Search for live broadcasts by channel
            var searchRequest = _youTubeService.Search.List("snippet");
            searchRequest.ChannelId = await ResolveChannelIdAsync(cancellationToken);
            searchRequest.EventType = SearchResource.ListRequest.EventTypeEnum.Live;
            searchRequest.Type = "video";
            searchRequest.MaxResults = 1;

            var searchResponse = await searchRequest.ExecuteAsync(cancellationToken);

            foreach (var result in searchResponse.Items)
            {
                var videoId = result.Id.VideoId;

                // Get video details to extract liveChatId
                var videoRequest = _youTubeService.Videos.List("liveStreamingDetails");
                videoRequest.Id = videoId;
                var videoResponse = await videoRequest.ExecuteAsync(cancellationToken);

                foreach (var video in videoResponse.Items)
                {
                    if (video.LiveStreamingDetails?.ActiveLiveChatId != null)
                    {
                        return video.LiveStreamingDetails.ActiveLiveChatId;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "YouTube: Error resolving live chat ID");
        }

        return null;
    }

    private async Task<string> ResolveChannelIdAsync(CancellationToken cancellationToken)
    {
        if (_youTubeService == null) return "";

        try
        {
            var channelRequest = _youTubeService.Channels.List("id");
            channelRequest.ForHandle = _channelName;
            var channelResponse = await channelRequest.ExecuteAsync(cancellationToken);

            foreach (var channel in channelResponse.Items)
            {
                return channel.Id;
            }

            // Fallback: search by username
            var searchRequest = _youTubeService.Search.List("snippet");
            searchRequest.Q = _channelName;
            searchRequest.Type = "channel";
            searchRequest.MaxResults = 1;

            var searchResponse = await searchRequest.ExecuteAsync(cancellationToken);
            foreach (var item in searchResponse.Items)
            {
                return item.Snippet.ChannelId;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "YouTube: Error resolving channel ID for {Channel}", _channelName);
        }

        return _channelName ?? "";
    }

    private async Task PollMessagesAsync(CancellationToken cancellationToken)
    {
        Logger.Information("YouTube: Starting message polling");

        while (!cancellationToken.IsCancellationRequested && _youTubeService != null && _liveChatId != null)
        {
            try
            {
                var request = _youTubeService.LiveChatMessages.List(_liveChatId, "snippet,authorDetails");
                request.MaxResults = 200;
                request.PageToken = _nextPageToken;

                var response = await request.ExecuteAsync(cancellationToken);

                // Update polling interval from API response
                if (response.PollingIntervalMillis.HasValue)
                {
                    _pollingInterval = TimeSpan.FromMilliseconds(response.PollingIntervalMillis.Value);
                }

                _nextPageToken = response.NextPageToken;

                foreach (var message in response.Items)
                {
                    ProcessLiveChatMessage(message);
                }

                await Task.Delay(_pollingInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "YouTube: Error polling messages, waiting before retry");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }

        Logger.Information("YouTube: Message polling stopped");
    }

    private void ProcessLiveChatMessage(LiveChatMessage message)
    {
        try
        {
            var snippet = message.Snippet;
            var author = message.AuthorDetails;

            if (snippet.Type != "textMessageEvent" && snippet.Type != "superChatEvent")
                return;

            var text = snippet.DisplayMessage ?? snippet.TextMessageDetails?.MessageText ?? "";
            var isSuperChat = snippet.Type == "superChatEvent";

            var badges = new List<string>();
            if (author.IsChatOwner == true) badges.Add("youtube_owner");
            if (author.IsChatModerator == true) badges.Add("youtube_moderator");
            if (author.IsChatSponsor == true) badges.Add("youtube_member");
            if (author.IsVerified == true) badges.Add("youtube_verified");

            var userColor = "#FFFFFF";
            if (author.IsChatOwner == true) userColor = "#FFD700";
            else if (author.IsChatModerator == true) userColor = "#5DADE2";
            else if (isSuperChat) userColor = "#F39C12";

            var chatMessage = new ChatMessage
            {
                Id = message.Id,
                Platform = ChatPlatform.YouTube,
                DisplayName = author.DisplayName ?? "Unknown",
                Username = author.DisplayName ?? "Unknown",
                Text = text,
                UserColor = userColor,
                Badges = badges.ToArray(),
                Emotes = new Dictionary<string, string>(),
                IsHighlighted = author.IsChatOwner == true || author.IsChatModerator == true || isSuperChat,
                IsSystem = snippet.Type != "textMessageEvent",
                Timestamp = snippet.PublishedAtDateTimeOffset?.UtcDateTime ?? DateTime.UtcNow,
                Channel = _channelName ?? ""
            };

            OnMessageReceived(chatMessage);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "YouTube: Error processing chat message");
        }
    }
}
