using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DegraChat.Core.Models;
using Serilog;

namespace DegraChat.Storage;

/// <summary>
/// Manages application settings stored as JSON files in %AppData%/DegraChat/.
/// Sensitive tokens are encrypted using Windows DPAPI on Windows,
/// or AES-256 with a machine-derived key on other platforms.
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

    // AES key for non-Windows platforms (derived from machine-specific data)
    private static byte[]? _aesKey;

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

    // ---- Encryption ----

    private static string? EncryptValue(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return EncryptWithDpapi(plainText);
            }
            else
            {
                return EncryptWithAes(plainText);
            }
        }
        catch
        {
            // Encryption not available, store as-is (should only happen in exceptional cases)
            return plainText;
        }
    }

    private static string? DecryptValue(string? encryptedText)
    {
        if (string.IsNullOrEmpty(encryptedText)) return null;

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return DecryptWithDpapi(encryptedText);
            }
            else
            {
                return DecryptWithAes(encryptedText);
            }
        }
        catch
        {
            // If decryption fails, might be plain text or encrypted on a different platform
            return encryptedText;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string EncryptWithDpapi(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var entropy = Encoding.UTF8.GetBytes("Degrachat.TokenProtection");
        var encrypted = ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser);
        return "dpapi:" + Convert.ToBase64String(encrypted);
    }

    [SupportedOSPlatform("windows")]
    private static string DecryptWithDpapi(string encryptedText)
    {
        // Check if this was encrypted with DPAPI
        if (!encryptedText.StartsWith("dpapi:"))
        {
            // Might be AES-encrypted or plain text
            return encryptedText;
        }

        var base64 = encryptedText["dpapi:".Length..];
        var bytes = Convert.FromBase64String(base64);
        var entropy = Encoding.UTF8.GetBytes("Degrachat.TokenProtection");
        var decrypted = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static string EncryptWithAes(string plainText)
    {
        var key = GetOrCreateAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
        return "aes:" + Convert.ToBase64String(result);
    }

    private static string DecryptWithAes(string encryptedText)
    {
        if (!encryptedText.StartsWith("aes:"))
        {
            // Might be DPAPI-encrypted or plain text
            return encryptedText;
        }

        var base64 = encryptedText["aes:".Length..];
        var data = Convert.FromBase64String(base64);

        var key = GetOrCreateAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        // Extract IV from the beginning
        var iv = new byte[aes.BlockSize / 8];
        var cipherText = new byte[data.Length - iv.Length];
        Buffer.BlockCopy(data, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(data, iv.Length, cipherText, 0, cipherText.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var decrypted = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        return Encoding.UTF8.GetString(decrypted);
    }

    private static byte[] GetOrCreateAesKey()
    {
        if (_aesKey != null) return _aesKey;

        // Derive a key from machine-specific data (username + machine name)
        var machineId = $"{Environment.MachineName}:{Environment.UserName}:DegraChat-v1";
        var salt = Encoding.UTF8.GetBytes("DegraChat-Salt-v1");
        using var deriveBytes = new Rfc2898DeriveBytes(machineId, salt, 100_000, HashAlgorithmName.SHA256);
        _aesKey = deriveBytes.GetBytes(32); // AES-256
        return _aesKey;
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
