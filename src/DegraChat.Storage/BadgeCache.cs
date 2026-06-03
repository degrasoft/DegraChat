using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace DegraChat.Storage;

/// <summary>
/// SQLite-based cache for badge and emote image data.
/// Reduces API calls and network requests for frequently used assets.
/// </summary>
public class BadgeCache : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly string _dbPath;
    private SqliteConnection? _connection;

    public BadgeCache(ILogger logger, string? dbPath = null)
    {
        _logger = logger.ForContext<BadgeCache>();
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DegraChat");
        Directory.CreateDirectory(appData);
        _dbPath = dbPath ?? Path.Combine(appData, "degrachat.db");
    }

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_dbPath}");
        await _connection.OpenAsync();

        var createTable = @"
            CREATE TABLE IF NOT EXISTS badge_cache (
                key TEXT PRIMARY KEY,
                image_url TEXT NOT NULL,
                image_data BLOB,
                platform TEXT NOT NULL,
                cached_at TEXT NOT NULL,
                expires_at TEXT
            );

            CREATE TABLE IF NOT EXISTS emote_cache (
                key TEXT PRIMARY KEY,
                image_url TEXT NOT NULL,
                image_data BLOB,
                platform TEXT NOT NULL,
                cached_at TEXT NOT NULL,
                expires_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_badge_platform ON badge_cache(platform);
            CREATE INDEX IF NOT EXISTS idx_emote_platform ON emote_cache(platform);
        ";

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = createTable;
        await cmd.ExecuteNonQueryAsync();

        _logger.Information("Badge cache initialized at {Path}", _dbPath);
    }

    public async Task StoreBadgeAsync(string key, string imageUrl, byte[]? imageData, string platform, TimeSpan? expiry = null)
    {
        await EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO badge_cache (key, image_url, image_data, platform, cached_at, expires_at)
            VALUES ($key, $url, $data, $platform, $now, $expires)";

        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$url", imageUrl);
        cmd.Parameters.AddWithValue("$data", (object?)imageData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", expiry.HasValue
            ? DateTime.UtcNow.Add(expiry.Value).ToString("O")
            : (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<byte[]?> GetBadgeDataAsync(string key)
    {
        await EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT image_data FROM badge_cache WHERE key = $key AND (expires_at IS NULL OR expires_at > $now)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task StoreEmoteAsync(string key, string imageUrl, byte[]? imageData, string platform, TimeSpan? expiry = null)
    {
        await EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO emote_cache (key, image_url, image_data, platform, cached_at, expires_at)
            VALUES ($key, $url, $data, $platform, $now, $expires)";

        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$url", imageUrl);
        cmd.Parameters.AddWithValue("$data", (object?)imageData ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$platform", platform);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$expires", expiry.HasValue
            ? DateTime.UtcNow.Add(expiry.Value).ToString("O")
            : (object)DBNull.Value);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<byte[]?> GetEmoteDataAsync(string key)
    {
        await EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT image_data FROM emote_cache WHERE key = $key AND (expires_at IS NULL OR expires_at > $now)";
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));

        var result = await cmd.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task CleanupExpiredAsync()
    {
        await EnsureConnection();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM badge_cache WHERE expires_at IS NOT NULL AND expires_at < $now;
            DELETE FROM emote_cache WHERE expires_at IS NOT NULL AND expires_at < $now;";
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        var deleted = await cmd.ExecuteNonQueryAsync();
        if (deleted > 0)
        {
            _logger.Information("Cleaned up {Count} expired cache entries", deleted);
        }
    }

    private Task EnsureConnection()
    {
        if (_connection == null) throw new InvalidOperationException("BadgeCache not initialized. Call InitializeAsync() first.");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
