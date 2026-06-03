using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using DegraChat.Core.Events;
using DegraChat.Core.Interfaces;
using DegraChat.Core.Models;
using DegraChat.Editor.ViewModels;
using DegraChat.Server;
using DegraChat.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace DegraChat.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly OverlayWebSocketServer _wsServer;
    private readonly SettingsManager _settingsManager;
    private readonly BadgeCache _badgeCache;
    private readonly ILogger _logger;
    private readonly IEventAggregator _eventAggregator;
    private IDisposable? _connectionStateSubscription;

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private bool _isServerRunning;

    [ObservableProperty]
    private int _connectedClients;

    [ObservableProperty]
    private string _serverStatusText = "Остановлен";

    [ObservableProperty]
    private long _messagesSent;

    [ObservableProperty]
    private string _uptimeText = "--";

    [ObservableProperty]
    private ConnectionViewModel _connectionViewModel;

    [ObservableProperty]
    private EditorViewModel _editorViewModel;

    [ObservableProperty]
    private ChatViewModel _chatViewModel;

    [ObservableProperty]
    private ObservableCollection<LogEntry> _serverLog = new();

    private DateTime _serverStartTime;

    public MainViewModel(
        ConnectionViewModel connectionViewModel,
        EditorViewModel editorViewModel,
        ChatViewModel chatViewModel,
        OverlayWebSocketServer wsServer,
        SettingsManager settingsManager,
        BadgeCache badgeCache,
        IEventAggregator eventAggregator,
        ILogger logger)
    {
        _connectionViewModel = connectionViewModel;
        _editorViewModel = editorViewModel;
        _chatViewModel = chatViewModel;
        _wsServer = wsServer;
        _settingsManager = settingsManager;
        _badgeCache = badgeCache;
        _eventAggregator = eventAggregator;
        _logger = logger.ForContext<MainViewModel>();

        _wsServer.ServerStarted += OnServerStarted;
        _wsServer.ServerStopped += OnServerStopped;
        _wsServer.ClientConnected += (_, count) => ConnectedClients = count;
        _wsServer.ClientDisconnected += (_, count) => ConnectedClients = count;

        // When a connection state changes, restart the server
        // to pick up new chat message routing
        _connectionStateSubscription = _eventAggregator.Subscribe<ConnectionStateChangedEvent>(OnConnectionStateChanged);
    }

    private void OnServerStarted(object? sender, EventArgs e)
    {
        IsServerRunning = true;
        _serverStartTime = DateTime.UtcNow;
        ServerStatusText = $"Запущен (порт 9274)";
        AddLogEntry("INFO", "Сервер запущен на ws://127.0.0.1:9274");
    }

    private void OnServerStopped(object? sender, EventArgs e)
    {
        IsServerRunning = false;
        ServerStatusText = "Остановлен";
        UptimeText = "--";
        AddLogEntry("INFO", "Сервер остановлен");
    }

    private void OnConnectionStateChanged(ConnectionStateChangedEvent e)
    {
        // Auto-restart server when connections change
        // This ensures the WebSocket server picks up the new message routing
        _ = RestartServerOnConnectionChangeAsync(e);
    }

    private async Task RestartServerOnConnectionChangeAsync(ConnectionStateChangedEvent e)
    {
        try
        {
            var stateStr = e.NewState switch
            {
                ConnectionState.Connected => "подключено",
                ConnectionState.Disconnected => "отключено",
                ConnectionState.Connecting => "подключение...",
                ConnectionState.Reconnecting => "переподключение...",
                ConnectionState.Error => $"ошибка: {e.ErrorMessage}",
                _ => e.NewState.ToString()
            };

            AddLogEntry("INFO", $"{e.Platform}: {stateStr}");

            if (IsServerRunning)
            {
                // Restart server to refresh subscriptions
                await _wsServer.StopAsync();
                await _wsServer.StartAsync();
                AddLogEntry("INFO", "Сервер перезапущен после изменения подключений");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error restarting server after connection change");
            AddLogEntry("ERROR", $"Ошибка рестарта: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RestartServerAsync()
    {
        try
        {
            if (IsServerRunning)
            {
                await _wsServer.StopAsync();
            }
            await _wsServer.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restart WebSocket server");
            ServerStatusText = $"Ошибка: {ex.Message}";
            AddLogEntry("ERROR", $"Ошибка: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopServerAsync()
    {
        try
        {
            await _wsServer.StopAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to stop WebSocket server");
            AddLogEntry("ERROR", $"Ошибка остановки: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SendTestMessageAsync()
    {
        try
        {
            await _wsServer.SendTestMessageAsync(
                "Тестовое сообщение из DegraChat",
                "DegraChat",
                "#00C896");
            AddLogEntry("SEND", "Тестовое сообщение отправлено");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send test message");
            AddLogEntry("ERROR", $"Ошибка тестового сообщения: {ex.Message}");
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _badgeCache.InitializeAsync();
            await ConnectionViewModel.LoadSavedConfigsAsync();
            await EditorViewModel.InitializeAsync();

            // Server always auto-starts with the application
            AddLogEntry("INFO", "Автозапуск сервера при старте приложения");
            await _wsServer.StartAsync();

            // Auto-connect platforms that were connected in the last session
            await ConnectionViewModel.AutoReconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during initialization");
            AddLogEntry("ERROR", $"Ошибка инициализации: {ex.Message}");
        }
    }

    private void AddLogEntry(string level, string message)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Level = level,
            Message = message
        };

        // Add on UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ServerLog.Add(entry);
            // Keep last 200 entries
            while (ServerLog.Count > 200)
                ServerLog.RemoveAt(0);
        });
    }
}

public class LogEntry
{
    public string Timestamp { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string LevelColor => Level switch
    {
        "SEND" or "CONN" => "#00C896",
        "ERROR" => "#FF5C5C",
        "WARN" => "#FFB84D",
        _ => "#B0B0B0"
    };
}
