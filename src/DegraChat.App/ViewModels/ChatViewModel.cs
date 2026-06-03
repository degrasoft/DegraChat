using System;
using System.Collections.ObjectModel;
using System.Linq;
using DegraChat.Core.Events;
using DegraChat.Core.Interfaces;
using DegraChat.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace DegraChat.App.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger _logger;
    private IDisposable? _subscription;
    private const int MaxVisibleMessages = 200;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _messages = new();

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private string _filterText = string.Empty;

    public ChatViewModel(IEventAggregator eventAggregator, ILogger logger)
    {
        _eventAggregator = eventAggregator;
        _logger = logger.ForContext<ChatViewModel>();

        _subscription = _eventAggregator.Subscribe<ChatMessageReceivedEvent>(OnChatMessageReceived);
    }

    private void OnChatMessageReceived(ChatMessageReceivedEvent e)
    {
        var vm = new ChatMessageViewModel(e.Message);

        // Insert at beginning (newest first)
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Messages.Insert(0, vm);

            // Trim old messages
            while (Messages.Count > MaxVisibleMessages)
            {
                Messages.RemoveAt(Messages.Count - 1);
            }
        });
    }

    [RelayCommand]
    private void ClearMessages()
    {
        Messages.Clear();
    }

    partial void OnFilterTextChanged(string value)
    {
        // Could implement filtering logic here
    }
}

public partial class ChatMessageViewModel : ObservableObject
{
    public string Id { get; }
    public ChatPlatform Platform { get; }
    public string DisplayName { get; }
    public string Username { get; }
    public string Text { get; }
    public string UserColor { get; }
    public string PlatformIcon { get; }
    public bool IsHighlighted { get; }
    public bool IsSystem { get; }
    public string Timestamp { get; }
    public string Channel { get; }

    public ChatMessageViewModel(ChatMessage message)
    {
        Id = message.Id;
        Platform = message.Platform;
        DisplayName = message.DisplayName;
        Username = message.Username;
        Text = message.Text;
        UserColor = message.UserColor;
        IsHighlighted = message.IsHighlighted;
        IsSystem = message.IsSystem;
        Timestamp = message.Timestamp.ToString("HH:mm:ss");
        Channel = message.Channel;
        PlatformIcon = GetPlatformIcon(Platform);
    }

    private static string GetPlatformIcon(ChatPlatform platform) => platform switch
    {
        ChatPlatform.Twitch => "T",
        ChatPlatform.GoodGame => "G",
        ChatPlatform.Kick => "K",
        ChatPlatform.VKPlay => "V",
        ChatPlatform.YouTube => "Y",
        _ => "?"
    };
}
