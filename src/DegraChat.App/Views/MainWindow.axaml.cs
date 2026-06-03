using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DegraChat.App.ViewModels;

namespace DegraChat.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private void OnActivityTabClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string tag && int.TryParse(tag, out var tabIndex))
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedTabIndex = tabIndex;
            }
        }
    }
}
