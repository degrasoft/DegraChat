using System;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Core.Models;
using Serilog;

namespace DegraChat.Chat.Abstractions;

/// <summary>
/// Base class for chat providers with common reconnection and state management logic.
/// Concrete providers inherit from this and implement platform-specific connection.
/// </summary>
public abstract class ChatProviderBase : IChatProvider
{
    private ConnectionState _state = ConnectionState.Disconnected;
    private int _reconnectAttempts;
    protected readonly ILogger Logger;
    protected ConnectionConfig? CurrentConfig;
    protected CancellationTokenSource? ConnectionCts;

    public abstract ChatPlatform Platform { get; }

    public ConnectionState State
    {
        get => _state;
        protected set
        {
            if (_state == value) return;
            var old = _state;
            _state = value;
            Logger.Information("{Platform}: State changed {Old} → {New}", Platform, old, value);
            ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(old, value));
        }
    }

    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    public event EventHandler<ChatMessageEventArgs>? MessageReceived;

    protected ChatProviderBase(ILogger logger)
    {
        Logger = logger.ForContext(GetType());
    }

    public async Task ConnectAsync(ConnectionConfig config, CancellationToken cancellationToken = default)
    {
        if (State == ConnectionState.Connected || State == ConnectionState.Connecting)
        {
            Logger.Warning("{Platform}: Already connected or connecting", Platform);
            return;
        }

        CurrentConfig = config;
        _reconnectAttempts = 0;
        ConnectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        State = ConnectionState.Connecting;

        try
        {
            await ConnectCoreAsync(config, ConnectionCts.Token);
            State = ConnectionState.Connected;
            _reconnectAttempts = 0;
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.Disconnected;
            Logger.Information("{Platform}: Connection cancelled", Platform);
        }
        catch (Exception ex)
        {
            State = ConnectionState.Error;
            Logger.Error(ex, "{Platform}: Connection failed", Platform);
            ConnectionStateChanged?.Invoke(this,
                new ConnectionStateChangedEventArgs(ConnectionState.Error, ConnectionState.Error, ex.Message));

            if (config.MaxReconnectAttempts > 0 && _reconnectAttempts < config.MaxReconnectAttempts)
            {
                _ = ReconnectLoopAsync(config);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ConnectionCts?.Cancel();
        try
        {
            await DisconnectCoreAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "{Platform}: Error during disconnect", Platform);
        }
        State = ConnectionState.Disconnected;
    }

    public abstract Task SendMessageAsync(string text, CancellationToken cancellationToken = default);

    protected abstract Task ConnectCoreAsync(ConnectionConfig config, CancellationToken cancellationToken);
    protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);

    protected void OnMessageReceived(ChatMessage message)
    {
        MessageReceived?.Invoke(this, new ChatMessageEventArgs(message));
    }

    protected async Task ReconnectLoopAsync(ConnectionConfig config)
    {
        while (_reconnectAttempts < config.MaxReconnectAttempts && ConnectionCts?.IsCancellationRequested == false)
        {
            _reconnectAttempts++;
            var delay = Math.Min(config.ReconnectDelayMs * _reconnectAttempts, 60000);
            State = ConnectionState.Reconnecting;
            Logger.Information("{Platform}: Reconnect attempt {Attempt}/{Max} in {Delay}ms",
                Platform, _reconnectAttempts, config.MaxReconnectAttempts, delay);

            try
            {
                await Task.Delay(delay, ConnectionCts.Token);
                await ConnectCoreAsync(config, ConnectionCts.Token);
                State = ConnectionState.Connected;
                _reconnectAttempts = 0;
                Logger.Information("{Platform}: Reconnected successfully", Platform);
                return;
            }
            catch (OperationCanceledException)
            {
                State = ConnectionState.Disconnected;
                return;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "{Platform}: Reconnect attempt {Attempt} failed", Platform, _reconnectAttempts);
            }
        }

        State = ConnectionState.Error;
        Logger.Error("{Platform}: Max reconnection attempts reached", Platform);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        ConnectionCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
