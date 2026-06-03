using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Chat.Abstractions;
using DegraChat.Core.Events;
using DegraChat.Core.Interfaces;
using DegraChat.Core.Models;
using DegraChat.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace DegraChat.Editor.ViewModels;

public partial class ConnectionViewModel : ObservableObject
{
    private readonly IEventAggregator _eventAggregator;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger _logger;
    private readonly Dictionary<ChatPlatform, IChatProvider> _providers;

    [ObservableProperty]
    private ObservableCollection<ConnectionItemViewModel> _connections = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ConnectionViewModel(
        IEventAggregator eventAggregator,
        IEnumerable<IChatProvider> providers,
        SettingsManager settingsManager,
        ILogger logger)
    {
        _eventAggregator = eventAggregator;
        _settingsManager = settingsManager;
        _logger = logger.ForContext<ConnectionViewModel>();
        _providers = new Dictionary<ChatPlatform, IChatProvider>();

        foreach (var provider in providers)
        {
            _providers[provider.Platform] = provider;
            var item = new ConnectionItemViewModel(provider, _settingsManager, _logger);
            Connections.Add(item);
        }

        // Subscribe to connection state changes
        _eventAggregator.Subscribe<ConnectionStateChangedEvent>(OnConnectionStateChanged);
    }

    public async Task LoadSavedConfigsAsync()
    {
        var configs = await _settingsManager.LoadAllConnectionConfigsAsync();
        foreach (var kvp in configs)
        {
            var item = Connections.FirstOrDefault(c => c.Platform == kvp.Key);
            if (item != null)
            {
                item.ChannelName = kvp.Value.ChannelName;
                item.HasSavedConfig = true;
            }
        }
    }

    private void OnConnectionStateChanged(ConnectionStateChangedEvent e)
    {
        var item = Connections.FirstOrDefault(c => c.Platform == e.Platform);
        if (item != null)
        {
            item.State = e.NewState;
        }
    }

    /// <summary>
    /// Auto-reconnect platforms that were connected in the last session.
    /// Called after server auto-starts so messages can be routed immediately.
    /// </summary>
    public async Task AutoReconnectAsync()
    {
        var configs = await _settingsManager.LoadAllConnectionConfigsAsync();
        foreach (var item in Connections)
        {
            if (configs.TryGetValue(item.Platform, out var config) && !string.IsNullOrEmpty(config.ChannelName))
            {
                item.ChannelName = config.ChannelName;
                item.HasSavedConfig = true;
                // Auto-connect if the platform was previously connected
                try
                {
                    await item.ConnectCommand.ExecuteAsync(null);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Auto-reconnect failed for {Platform}", item.Platform);
                }
            }
        }
    }
}

public partial class ConnectionItemViewModel : ObservableObject
{
    private readonly IChatProvider _provider;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger _logger;
    private ConnectionConfig? _currentConfig;

    [ObservableProperty]
    private ChatPlatform _platform;

    [ObservableProperty]
    private string _channelName = string.Empty;

    [ObservableProperty]
    private string _oAuthToken = string.Empty;

    [ObservableProperty]
    private ConnectionState _state = ConnectionState.Disconnected;

    [ObservableProperty]
    private bool _hasSavedConfig;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public string PlatformDisplayName => Platform.ToString();

    public ConnectionItemViewModel(IChatProvider provider, SettingsManager settingsManager, ILogger logger)
    {
        _provider = provider;
        _settingsManager = settingsManager;
        _logger = logger.ForContext<ConnectionItemViewModel>();
        Platform = provider.Platform;

        _provider.ConnectionStateChanged += (s, e) =>
        {
            State = e.NewState;
            if (e.ErrorMessage != null) ErrorMessage = e.ErrorMessage;
        };
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            ErrorMessage = string.Empty;
            _currentConfig = new ConnectionConfig
            {
                Platform = Platform,
                ChannelName = ChannelName,
                OAuthToken = string.IsNullOrEmpty(OAuthToken) ? null : OAuthToken,
                ReconnectDelayMs = 5000,
                MaxReconnectAttempts = 10
            };

            await _provider.ConnectAsync(_currentConfig);

            // Save config for next launch
            await _settingsManager.SaveConnectionConfigAsync(_currentConfig);
            HasSavedConfig = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error connecting to {Platform}", Platform);
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        try
        {
            await _provider.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error disconnecting from {Platform}", Platform);
            ErrorMessage = ex.Message;
        }
    }
}
