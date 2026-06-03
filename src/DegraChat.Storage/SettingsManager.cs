using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DegraChat.Core.Models;
using Serilog;

namespace DegraChat.Storage;

/// <summary>
/// Manages application settings stored as JSON files in %AppData%/DegraChat/.
/// Sensitive tokens are encrypted using Windows DPAPI.
/// </summary>
public class SettingsManager
{
    private readonly ILogger _logger;
    private readonly string _appDataPath;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public SettingsManager(ILogger logger)
    {
        _logger = logger.ForContext<SettingsManager>();
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DegraChat");
        Directory.CreateDirectory(_appDataPath);
    }

    public string AppDataPath => _appDataPath;

    // ---- Connection Configs ----

    public async Task SaveConnectionConfigAsync(ConnectionConfig config)
    {
        var filePath = GetConfigPath($"connections/{config.Platform}.json");
        EnsureDirectoryExists(filePath);

        // Encrypt sensitive fields
        var safeCopy = new ConnectionConfig
        {
            Platform = config.Platform,
            ChannelName = config.ChannelName,
            OAuthToken = EncryptValue(config.OAuthToken),
            AccessToken = EncryptValue(config.AccessToken),
            RefreshToken = EncryptValue(config.RefreshToken),
            ClientId = config.ClientId, // ClientId is not sensitive
            AutoConnect = config.AutoConnect,
            ReconnectDelayMs = config.ReconnectDelayMs,
            MaxReconnectAttempts = config.MaxReconnectAttempts
        };

        var json = JsonSerializer.Serialize(safeCopy, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        _logger.Information("Saved connection config for {Platform}", config.Platform);
    }

    public async Task<ConnectionConfig?> LoadConnectionConfigAsync(ChatPlatform platform)
    {
        var filePath = GetConfigPath($"connections/{platform}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        var config = JsonSerializer.Deserialize<ConnectionConfig>(json, _jsonOptions);
        if (config == null) return null;

        // Decrypt sensitive fields
        config.OAuthToken = DecryptValue(config.OAuthToken);
        config.AccessToken = DecryptValue(config.AccessToken);
        config.RefreshToken = DecryptValue(config.RefreshToken);

        return config;
    }

    public async Task<Dictionary<ChatPlatform, ConnectionConfig>> LoadAllConnectionConfigsAsync()
    {
        var configs = new Dictionary<ChatPlatform, ConnectionConfig>();
        foreach (ChatPlatform platform in Enum.GetValues<ChatPlatform>())
        {
            var config = await LoadConnectionConfigAsync(platform);
            if (config != null)
            {
                configs[platform] = config;
            }
        }
        return configs;
    }

    // ---- Overlay Styles ----

    public async Task SaveOverlayStyleAsync(OverlayStyle style)
    {
        var filePath = GetConfigPath($"styles/{SanitizeFileName(style.ProfileName)}.json");
        EnsureDirectoryExists(filePath);

        var json = JsonSerializer.Serialize(style, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        _logger.Information("Saved overlay style profile: {Profile}", style.ProfileName);
    }

    public async Task<OverlayStyle?> LoadOverlayStyleAsync(string profileName)
    {
        var filePath = GetConfigPath($"styles/{SanitizeFileName(profileName)}.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<OverlayStyle>(json, _jsonOptions);
    }

    public async Task<List<OverlayStyle>> LoadAllOverlayStylesAsync()
    {
        var styles = new List<OverlayStyle>();
        var stylesDir = GetConfigPath("styles");

        if (!Directory.Exists(stylesDir)) return styles;

        foreach (var file in Directory.GetFiles(stylesDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var style = JsonSerializer.Deserialize<OverlayStyle>(json, _jsonOptions);
                if (style != null) styles.Add(style);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error loading style from {File}", file);
            }
        }

        return styles;
    }

    public Task DeleteOverlayStyleAsync(string profileName)
    {
        var filePath = GetConfigPath($"styles/{SanitizeFileName(profileName)}.json");
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            _logger.Information("Deleted overlay style profile: {Profile}", profileName);
        }
        return Task.CompletedTask;
    }

    // ---- Server Config ----

    public async Task SaveServerConfigAsync(ServerConfig config)
    {
        var filePath = GetConfigPath("server.json");
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        _logger.Information("Saved server config");
    }

    public async Task<ServerConfig> LoadServerConfigAsync()
    {
        var filePath = GetConfigPath("server.json");
        if (!File.Exists(filePath)) return new ServerConfig();

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ServerConfig>(json, _jsonOptions) ?? new ServerConfig();
    }

    // ---- Window State ----

    public async Task SaveWindowStateAsync(double x, double y, double width, double height, bool isMaximized)
    {
        var state = new WindowState { X = x, Y = y, Width = width, Height = height, IsMaximized = isMaximized };
        var filePath = GetConfigPath("window.json");
        var json = JsonSerializer.Serialize(state, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public async Task<WindowState?> LoadWindowStateAsync()
    {
        var filePath = GetConfigPath("window.json");
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<WindowState>(json, _jsonOptions);
    }

    // ---- DPAPI Encryption ----

    private static string? EncryptValue(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }
        catch
        {
            // DPAPI not available (non-Windows), store as-is
            return plainText;
        }
    }

    private static string? DecryptValue(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return null;

        try
        {
            var bytes = Convert.FromBase64String(encryptedText);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // If decryption fails, might be plain text (non-Windows or not encrypted)
            return encryptedText;
        }
    }

    // ---- Helpers ----

    private string GetConfigPath(string relativePath) => Path.Combine(_appDataPath, relativePath);

    private static void EnsureDirectoryExists(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}

/// <summary>
/// Serializable window position/size state.
/// </summary>
public class WindowState
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}
