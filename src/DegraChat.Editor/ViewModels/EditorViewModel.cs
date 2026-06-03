using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Core.Models;
using DegraChat.Overlay.Engine;
using DegraChat.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace DegraChat.Editor.ViewModels;

public partial class EditorViewModel : ObservableObject
{
    private readonly OverlayGenerator _overlayGenerator;
    private readonly SettingsManager _settingsManager;
    private readonly ILogger _logger;
    private readonly ServerConfig _serverConfig;

    [ObservableProperty]
    private OverlayStyle _currentStyle = new();

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private ObservableCollection<OverlayStyle> _savedProfiles = new();

    [ObservableProperty]
    private string _newProfileName = "New Profile";

    [ObservableProperty]
    private string _previewHtml = string.Empty;

    // Animation options for UI dropdowns
    public static string[] AnimationOptions { get; } = { "none", "fadeIn", "slideIn" };
    public static string[] AnimationOutOptions { get; } = { "none", "fadeOut", "slideOut" };
    public static string[] DirectionOptions { get; } = { "bottom-up", "top-down" };

    public EditorViewModel(
        OverlayGenerator overlayGenerator,
        SettingsManager settingsManager,
        ILogger logger,
        ServerConfig serverConfig)
    {
        _overlayGenerator = overlayGenerator;
        _settingsManager = settingsManager;
        _logger = logger.ForContext<EditorViewModel>();
        _serverConfig = serverConfig;
    }

    public async Task InitializeAsync()
    {
        var styles = await _settingsManager.LoadAllOverlayStylesAsync();
        SavedProfiles.Clear();
        foreach (var style in styles)
        {
            SavedProfiles.Add(style);
        }

        // Load default style or create one
        var defaultStyle = await _settingsManager.LoadOverlayStyleAsync("Default");
        CurrentStyle = defaultStyle ?? new OverlayStyle();

        await UpdatePreviewAsync();
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            await _settingsManager.SaveOverlayStyleAsync(CurrentStyle);

            // Update collection
            var existing = SavedProfiles.FirstOrDefault(p => p.ProfileName == CurrentStyle.ProfileName);
            if (existing != null)
            {
                var idx = SavedProfiles.IndexOf(existing);
                SavedProfiles[idx] = CurrentStyle;
            }
            else
            {
                SavedProfiles.Add(CurrentStyle);
            }

            StatusMessage = $"Profile '{CurrentStyle.ProfileName}' saved";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error saving profile");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task LoadProfileAsync(string profileName)
    {
        try
        {
            var style = await _settingsManager.LoadOverlayStyleAsync(profileName);
            if (style != null)
            {
                CurrentStyle = style;
                StatusMessage = $"Loaded profile '{profileName}'";
                await UpdatePreviewAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading profile");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateNewProfileAsync()
    {
        try
        {
            var newStyle = new OverlayStyle { ProfileName = NewProfileName };
            await _settingsManager.SaveOverlayStyleAsync(newStyle);
            SavedProfiles.Add(newStyle);
            CurrentStyle = newStyle;
            StatusMessage = $"Created profile '{NewProfileName}'";
            await UpdatePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error creating profile");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(string profileName)
    {
        try
        {
            await _settingsManager.DeleteOverlayStyleAsync(profileName);
            var toRemove = SavedProfiles.FirstOrDefault(p => p.ProfileName == profileName);
            if (toRemove != null) SavedProfiles.Remove(toRemove);
            StatusMessage = $"Deleted profile '{profileName}'";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error deleting profile");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UpdatePreviewAsync()
    {
        try
        {
            IsGenerating = true;
            var html = await _overlayGenerator.GenerateOverlayAsync(CurrentStyle, _serverConfig);
            PreviewHtml = html;
            StatusMessage = "Preview updated";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error generating preview");
            StatusMessage = $"Preview error: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    [RelayCommand]
    private async Task ExportOverlayAsync(string outputPath)
    {
        try
        {
            var html = await _overlayGenerator.GenerateOverlayAsync(CurrentStyle, _serverConfig);
            await _overlayGenerator.SaveOverlayAsync(html, outputPath);
            StatusMessage = $"Exported to {outputPath}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error exporting overlay");
            StatusMessage = $"Export error: {ex.Message}";
        }
    }
}
