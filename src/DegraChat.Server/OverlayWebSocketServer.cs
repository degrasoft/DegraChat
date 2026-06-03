using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Core.Interfaces;
using DegraChat.Core.Models;
using Serilog;

namespace DegraChat.Server;

/// <summary>
/// Local WebSocket server that broadcasts chat messages to OBS overlay clients.
/// Runs on http://127.0.0.1:{port} with WebSocket endpoint at /ws
/// and serves the overlay HTML at /overlay
/// </summary>
public class OverlayWebSocketServer : IAsyncDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger _logger;
    private readonly ServerConfig _config;
    private HttpListener? _httpListener;
    private readonly ConcurrentDictionary<string, WebSocket> _clients = new();
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private IDisposable? _messageSubscription;
    private int _clientCounter;

    public event EventHandler? ServerStarted;
    public event EventHandler? ServerStopped;
    public event EventHandler<int>? ClientConnected;
    public event EventHandler<int>? ClientDisconnected;

    public bool IsRunning => _httpListener?.IsListening ?? false;
    public int ConnectedClients => _clients.Count;
    public string WebSocketUrl => $"ws://{_config.Host}:{_config.Port}/ws";
    public string OverlayUrl => $"http://{_config.Host}:{_config.Port}/overlay";

    public OverlayWebSocketServer(IEventAggregator eventAggregator, ILogger logger, ServerConfig? config = null)
    {
        _eventAggregator = eventAggregator;
        _logger = logger.ForContext<OverlayWebSocketServer>();
        _config = config ?? new ServerConfig();
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_httpListener?.IsListening == true)
        {
            _logger.Warning("Server is already running");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://{_config.Host}:{_config.Port}/");

        try
        {
            _httpListener.Start();
            _logger.Information("WebSocket server started at {Url}", OverlayUrl);
        }
        catch (HttpListenerException ex)
        {
            _logger.Error(ex, "Failed to start WebSocket server on port {Port}. Try running as admin or use netsh to reserve the URL.", _config.Port);
            throw;
        }

        // Subscribe to chat message events
        _messageSubscription = _eventAggregator.Subscribe<ChatMessageReceivedEvent>(OnChatMessageReceived);

        _listenTask = AcceptLoopAsync(_cts.Token);
        ServerStarted?.Invoke(this, EventArgs.Empty);

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _messageSubscription?.Dispose();
        _messageSubscription = null;

        _cts?.Cancel();

        // Close all client connections
        foreach (var kvp in _clients)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
                }
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error closing client {ClientId}", kvp.Key);
            }
        }
        _clients.Clear();

        _httpListener?.Stop();
        _httpListener?.Close();
        _httpListener = null;

        if (_listenTask != null)
        {
            try { await _listenTask; } catch (OperationCanceledException) { }
        }
        _listenTask = null;

        _logger.Information("WebSocket server stopped");
        ServerStopped?.Invoke(this, EventArgs.Empty);
    }

    public async Task BroadcastAsync(string message, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(bytes);

        var disconnectedClients = new List<string>();

        foreach (var kvp in _clients)
        {
            try
            {
                if (kvp.Value.State == WebSocketState.Open)
                {
                    await kvp.Value.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
                }
                else
                {
                    disconnectedClients.Add(kvp.Key);
                }
            }
            catch (WebSocketException)
            {
                disconnectedClients.Add(kvp.Key);
            }
        }

        // Clean up disconnected clients
        foreach (var clientId in disconnectedClients)
        {
            RemoveClient(clientId);
        }
    }

    public async Task SendClearCommandAsync(CancellationToken cancellationToken = default)
    {
        var clearJson = """{"type":"clear"}""";
        await BroadcastAsync(clearJson, cancellationToken);
    }

    public async Task SendTestMessageAsync(string text, string displayName, string color, CancellationToken cancellationToken = default)
    {
        var testJson = $$"""
        {
            "type": "message",
            "data": {
                "id": "{{Guid.NewGuid()}}",
                "platform": "test",
                "displayName": "{{displayName}}",
                "username": "{{displayName.ToLowerInvariant()}}",
                "text": "{{text}}",
                "userColor": "{{color}}",
                "badges": [],
                "emotes": {},
                "isHighlighted": false,
                "isSystem": false,
                "timestamp": "{{DateTime.UtcNow:O}}",
                "channel": "test"
            }
        }
        """;
        await BroadcastAsync(testJson, cancellationToken);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    await HandleWebSocketAsync(context, cancellationToken);
                }
                else
                {
                    await HandleHttpRequestAsync(context, cancellationToken);
                }
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in accept loop");
            }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        WebSocket? webSocket = null;
        var clientId = $"client_{Interlocked.Increment(ref _clientCounter)}";

        try
        {
            var wsContext = await context.AcceptWebSocketAsync(null);
            webSocket = wsContext.WebSocket;

            _clients[clientId] = webSocket;
            _logger.Information("Client {ClientId} connected. Total clients: {Count}", clientId, _clients.Count);
            ClientConnected?.Invoke(this, _clients.Count);

            // Send welcome message
            var welcome = """{"type":"connected","message":"DegraChat connected"}""";
            var welcomeBytes = Encoding.UTF8.GetBytes(welcome);
            await webSocket.SendAsync(new ArraySegment<byte>(welcomeBytes), WebSocketMessageType.Text, true, cancellationToken);

            // Keep connection alive, receive any client commands
            var buffer = new byte[4096];
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                // Client messages are ignored for now (overlay is display-only)
            }
        }
        catch (WebSocketException ex)
        {
            _logger.Warning(ex, "WebSocket error for client {ClientId}", clientId);
        }
        catch (OperationCanceledException) { }
        finally
        {
            RemoveClient(clientId);
            webSocket?.Dispose();
        }
    }

    private async Task HandleHttpRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        string? content = null;
        string contentType = "text/html";

        switch (path)
        {
            case "/overlay":
            case "/overlay.html":
                content = GetOverlayHtml();
                contentType = "text/html; charset=utf-8";
                break;

            case "/overlay.css":
                content = GetOverlayCss();
                contentType = "text/css; charset=utf-8";
                break;

            case "/overlay.js":
                content = GetOverlayJs();
                contentType = "application/javascript; charset=utf-8";
                break;

            default:
                context.Response.StatusCode = 404;
                context.Response.Close();
                return;
        }

        var bytes = Encoding.UTF8.GetBytes(content);
        context.Response.ContentType = contentType;
        context.Response.ContentLength64 = bytes.Length;
        context.Response.AddHeader("Access-Control-Allow-Origin", "*");
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
    }

    private void OnChatMessageReceived(ChatMessageReceivedEvent e)
    {
        var json = ChatMessageSerializer.SerializeForOverlay(e.Message);
        _ = BroadcastAsync(json);
    }

    private void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out _))
        {
            _logger.Information("Client {ClientId} disconnected. Total clients: {Count}", clientId, _clients.Count);
            ClientDisconnected?.Invoke(this, _clients.Count);
        }
    }

    // Minimal embedded overlay files for immediate use.
    // The full overlay is generated by Overlay.Engine and served from assets.
    private static string GetOverlayHtml() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>DegraChat</title>
            <link rel="stylesheet" href="/overlay.css">
        </head>
        <body>
            <div id="chat-container"></div>
            <script src="/overlay.js"></script>
        </body>
        </html>
        """;

    private static string GetOverlayCss() => """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { background: transparent; font-family: 'Inter', sans-serif; overflow: hidden; }
        #chat-container {
            display: flex;
            flex-direction: column-reverse;
            padding: 12px;
            gap: 4px;
            max-height: 100vh;
            overflow: hidden;
        }
        .chat-message {
            display: flex;
            align-items: flex-start;
            gap: 8px;
            padding: 8px 12px;
            border-radius: 8px;
            background: rgba(0, 0, 0, 0.7);
            animation: fadeIn 0.3s ease;
            word-break: break-word;
        }
        .chat-message.system {
            color: #FF6600;
            font-style: italic;
        }
        .chat-message .platform-icon {
            width: 18px;
            height: 18px;
            flex-shrink: 0;
        }
        .chat-message .badges {
            display: flex;
            gap: 2px;
            align-items: center;
        }
        .chat-message .badges img {
            width: 18px;
            height: 18px;
        }
        .chat-message .username {
            font-weight: bold;
            margin-right: 6px;
            white-space: nowrap;
        }
        .chat-message .text {
            color: #FFFFFF;
            line-height: 1.4;
        }
        .chat-message .emote {
            width: 28px;
            height: 28px;
            vertical-align: middle;
        }
        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(10px); }
            to { opacity: 1; transform: translateY(0); }
        }
        @keyframes fadeOut {
            from { opacity: 1; }
            to { opacity: 0; }
        }
        """;

    private static string GetOverlayJs() => """
        const container = document.getElementById('chat-container');
        const maxMessages = 30;
        const wsUrl = `ws://${window.location.host}/ws`;
        let ws;

        function connect() {
            ws = new WebSocket(wsUrl);
            
            ws.onopen = () => console.log('Connected to DegraChat');
            
            ws.onmessage = (event) => {
                try {
                    const msg = JSON.parse(event.data);
                    if (msg.type === 'message') {
                        addMessage(msg.data);
                    } else if (msg.type === 'clear') {
                        container.innerHTML = '';
                    }
                } catch (e) {
                    console.error('Error parsing message:', e);
                }
            };
            
            ws.onclose = () => {
                console.log('Disconnected, reconnecting in 3s...');
                setTimeout(connect, 3000);
            };
            
            ws.onerror = (e) => console.error('WebSocket error:', e);
        }

        function addMessage(data) {
            const msgEl = document.createElement('div');
            msgEl.className = 'chat-message' + (data.isSystem ? ' system' : '');
            
            let badgesHtml = '';
            if (data.badges && data.badges.length > 0) {
                badgesHtml = '<span class="badges">' + 
                    data.badges.map(b => `<img src="${b}" alt="badge">`).join('') + 
                    '</span>';
            }
            
            let platformIcon = '';
            if (data.platform) {
                platformIcon = `<img class="platform-icon" src="data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 18 18'><text y='14' font-size='14'>${getPlatformEmoji(data.platform)}</text></svg>" alt="${data.platform}">`;
            }
            
            const textHtml = processEmotes(data.text || '', data.emotes || {});
            
            msgEl.innerHTML = `
                ${platformIcon}
                ${badgesHtml}
                <span class="username" style="color: ${data.userColor || '#FFF'}">${escapeHtml(data.displayName || 'Unknown')}</span>
                <span class="text">${textHtml}</span>
            `;
            
            container.insertBefore(msgEl, container.firstChild);
            
            // Remove old messages
            while (container.children.length > maxMessages) {
                container.removeChild(container.lastChild);
            }
        }

        function processEmotes(text, emotes) {
            if (!emotes || Object.keys(emotes).length === 0) return escapeHtml(text);
            
            let result = escapeHtml(text);
            for (const [name, url] of Object.entries(emotes)) {
                const escaped = escapeHtml(name);
                result = result.replace(new RegExp(escaped.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'g'), 
                    `<img class="emote" src="${url}" alt="${escaped}">`);
            }
            return result;
        }

        function escapeHtml(str) {
            const div = document.createElement('div');
            div.textContent = str;
            return div.innerHTML;
        }

        function getPlatformEmoji(platform) {
            const map = { twitch: '🟣', goodgame: '🟢', kick: '🟢', vkplay: '🔵', youtube: '🔴', test: '⚪' };
            return map[platform] || '⚪';
        }

        connect();
        """;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
