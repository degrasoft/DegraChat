using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DegraChat.App.ViewModels;
using DegraChat.App.Views;
using DegraChat.Chat.Abstractions;
using DegraChat.Chat.GoodGame;
using DegraChat.Chat.Kick;
using DegraChat.Chat.Twitch;
using DegraChat.Chat.VKPlay;
using DegraChat.Chat.YouTube;
using DegraChat.Core.Interfaces;
using DegraChat.Core.Models;
using DegraChat.Core.Services;
using DegraChat.Editor.ViewModels;
using DegraChat.Overlay.Engine;
using DegraChat.Server;
using DegraChat.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace DegraChat.App;

public class App : Application
{
    private IHost? _host;
    private IDisposable? _logSubscription;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Logging
                services.AddSingleton<ILogger>(Log.Logger);

                // Core services
                services.AddSingleton<IEventAggregator, EventAggregator>();
                services.AddSingleton(sp =>
                {
                    var settings = sp.GetRequiredService<SettingsManager>();
                    return settings.LoadServerConfigAsync().GetAwaiter().GetResult();
                });

                // Chat providers
                services.AddSingleton<IChatProvider, TwitchChatProvider>();
                services.AddSingleton<IChatProvider, GoodGameChatProvider>();
                services.AddSingleton<IChatProvider, KickChatProvider>();
                services.AddSingleton<IChatProvider, VKPlayChatProvider>();
                services.AddSingleton<IChatProvider, YouTubeChatProvider>();

                // WebSocket server
                services.AddSingleton<OverlayWebSocketServer>();

                // Overlay
                services.AddSingleton<OverlayGenerator>();

                // Storage
                services.AddSingleton<SettingsManager>();
                services.AddSingleton<BadgeCache>();

                // View Models
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<ConnectionViewModel>();
                services.AddSingleton<EditorViewModel>();
                services.AddSingleton<ChatViewModel>();
            });

        _host = hostBuilder.Build();

        // Wire chat providers → event aggregator
        var eventAggregator = _host.Services.GetRequiredService<IEventAggregator>();
        foreach (var provider in _host.Services.GetRequiredService<IEnumerable<IChatProvider>>())
        {
            provider.MessageReceived += (sender, args) =>
            {
                eventAggregator.Publish(new ChatMessageReceivedEvent(args.Message));
            };
        }

        // Wire event aggregator → WebSocket server broadcast
        var wsServer = _host.Services.GetRequiredService<OverlayWebSocketServer>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainVm = _host.Services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm
            };

            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void OnExit(object? sender, EventArgs e)
    {
        var wsServer = _host?.Services.GetService<OverlayWebSocketServer>();
        if (wsServer != null)
        {
            await wsServer.StopAsync();
        }

        // Disconnect all chat providers
        var providers = _host?.Services.GetRequiredService<IEnumerable<IChatProvider>>();
        if (providers != null)
        {
            foreach (var provider in providers)
            {
                await provider.DisconnectAsync();
            }
        }

        _host?.Dispose();
    }

    public new static App? Current => (App?)Application.Current;

    public T GetRequiredService<T>() where T : notnull
    {
        return _host!.Services.GetRequiredService<T>();
    }
}
