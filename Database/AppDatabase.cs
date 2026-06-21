using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Github_Trend.Database;

public sealed class AppDatabase : IAsyncDisposable
{
    private const int SchemaVersion = 2;
    private static readonly string DatabasePath;
    private static readonly SemaphoreSlim DbLock = new(1, 1);

    private SqliteConnection? _connection;
    private bool _initialized;

    static AppDatabase()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend"
        );
        Directory.CreateDirectory(folder);
        DatabasePath = Path.Combine(folder, "github-trend.db");
    }

    public static string DatabaseFilePath => DatabasePath;

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await DbLock.WaitAsync();
        try
        {
            if (_initialized) return;

            _connection = new SqliteConnection($"Data Source={DatabasePath}");
            await _connection.OpenAsync();

            await ExecutePragmaAsync("PRAGMA journal_mode=WAL");
            await ExecutePragmaAsync("PRAGMA busy_timeout=5000");
            await ExecutePragmaAsync("PRAGMA synchronous=NORMAL");
            await ExecutePragmaAsync("PRAGMA foreign_keys=ON");

            await RunMigrationsAsync();
            _initialized = true;

            Log.Information("Database initialized at {Path}", DatabasePath);
        }
        finally
        {
            DbLock.Release();
        }
    }

    private async Task RunMigrationsAsync()
    {
        await EnsureTableAsync("schema_version",
            "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY)");

        var currentVersion = await GetSchemaVersionAsync();
        if (currentVersion < SchemaVersion)
        {
            await ApplySchemaAsync();
            await SetSchemaVersionAsync(SchemaVersion);
            Log.Information("Database migrated to version {Version}", SchemaVersion);
        }
    }

    private async Task<int> GetSchemaVersionAsync()
    {
        try
        {
            using var cmd = _connection!.CreateCommand();
            cmd.CommandText = "SELECT MAX(version) FROM schema_version";
            var result = await cmd.ExecuteScalarAsync();
            return result is int v ? v : 0;
        }
        catch
        {
            return 0;
        }
    }

    private async Task SetSchemaVersionAsync(int version)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM schema_version; INSERT INTO schema_version (version) VALUES ($v)";
        cmd.Parameters.AddWithValue("$v", version);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task ApplySchemaAsync()
    {
        var schemaSql = EmbeddedResourceLoader.Load("Database.schema.sql");
        using var transaction = _connection!.BeginTransaction();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = schemaSql;
            await cmd.ExecuteNonQueryAsync();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private async Task EnsureTableAsync(string tableName, string createSql)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        var exists = await cmd.ExecuteScalarAsync();
        if (exists is null)
        {
            using var createCmd = _connection.CreateCommand();
            createCmd.CommandText = createSql;
            await createCmd.ExecuteNonQueryAsync();
        }
    }

    private async Task ExecutePragmaAsync(string pragma)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = pragma;
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Trending Cache ---

    public async Task<string?> GetTrendingCacheAsync(string cacheKey)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            "SELECT data_json FROM trending_cache WHERE cache_key=$k AND expires_at_utc > datetime('now')";
        cmd.Parameters.AddWithValue("$k", cacheKey);
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SetTrendingCacheAsync(
        string cacheKey,
        string since,
        string? language,
        string dataJson,
        TimeSpan ttl
    )
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO trending_cache (cache_key, since, language, data_json, cached_at_utc, expires_at_utc)
              VALUES ($k, $s, $l, $d, datetime('now'), datetime('now', '+' || $ttl || ' seconds'))";
        cmd.Parameters.AddWithValue("$k", cacheKey);
        cmd.Parameters.AddWithValue("$s", since ?? "");
        cmd.Parameters.AddWithValue("$l", language ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$d", dataJson);
        cmd.Parameters.AddWithValue("$ttl", (int)ttl.TotalSeconds);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CleanupExpiredTrendingAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM trending_cache WHERE expires_at_utc <= datetime('now')";
        return await cmd.ExecuteNonQueryAsync();
    }

    // --- Repository Details Cache ---

    public async Task<(string? dataJson, string? etag)?> GetRepositoryDetailsCacheAsync(
        string owner,
        string name,
        bool allowExpired = false
    )
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = allowExpired
            ? "SELECT data_json, etag FROM repository_details_cache WHERE owner=$o AND name=$n"
            : "SELECT data_json, etag FROM repository_details_cache WHERE owner=$o AND name=$n AND expires_at_utc > datetime('now')";
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$n", name);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        return null;
    }

    public async Task SetRepositoryDetailsCacheAsync(
        string owner,
        string name,
        string dataJson,
        string? etag,
        TimeSpan ttl
    )
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO repository_details_cache (owner, name, data_json, etag, cached_at_utc, expires_at_utc)
              VALUES ($o, $n, $d, $e, datetime('now'), datetime('now', '+' || $ttl || ' seconds'))";
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$d", dataJson);
        cmd.Parameters.AddWithValue("$e", etag ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ttl", (int)ttl.TotalSeconds);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CleanupExpiredRepoDetailsAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            "DELETE FROM repository_details_cache WHERE expires_at_utc <= datetime('now')";
        return await cmd.ExecuteNonQueryAsync();
    }

    // --- Colors Cache ---

    public async Task<string?> GetColorsCacheAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            "SELECT data_json FROM colors_cache WHERE expires_at_utc > datetime('now') ORDER BY cached_at_utc DESC LIMIT 1";
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }

    public async Task SetColorsCacheAsync(string dataJson, TimeSpan ttl)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"DELETE FROM colors_cache;
              INSERT INTO colors_cache (data_json, cached_at_utc, expires_at_utc)
              VALUES ($d, datetime('now'), datetime('now', '+' || $ttl || ' seconds'))";
        cmd.Parameters.AddWithValue("$d", dataJson);
        cmd.Parameters.AddWithValue("$ttl", (int)ttl.TotalSeconds);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Auth Tokens ---

    public async Task<JsonElement?> GetCurrentAuthTokenAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"SELECT user_id, github_account_id, access_token_encrypted, refresh_token_encrypted,
                     expires_at, refresh_token_expires_at, scope_list_json, created_at, updated_at,
                     revoked_at, login, name, email, avatar_url
              FROM auth_tokens
              WHERE revoked_at IS NULL
              ORDER BY updated_at DESC LIMIT 1";
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        var obj = new Dictionary<string, object?>
        {
            ["user_id"] = reader.GetString(0),
            ["github_account_id"] = reader.GetInt64(1),
            ["access_token_encrypted"] = reader.GetString(2),
            ["refresh_token_encrypted"] = reader.IsDBNull(3) ? null : reader.GetString(3),
            ["expires_at"] = reader.GetString(4),
            ["refresh_token_expires_at"] = reader.IsDBNull(5) ? null : reader.GetString(5),
            ["scope_list_json"] = reader.GetString(6),
            ["created_at"] = reader.GetString(7),
            ["updated_at"] = reader.GetString(8),
            ["revoked_at"] = reader.IsDBNull(9) ? null : reader.GetString(9),
            ["login"] = reader.IsDBNull(10) ? null : reader.GetString(10),
            ["name"] = reader.IsDBNull(11) ? null : reader.GetString(11),
            ["email"] = reader.IsDBNull(12) ? null : reader.GetString(12),
            ["avatar_url"] = reader.IsDBNull(13) ? null : reader.GetString(13),
        };
        return JsonSerializer.SerializeToElement(obj);
    }

    public async Task UpsertAuthTokenAsync(
        string userId,
        long githubAccountId,
        string accessTokenEncrypted,
        string? refreshTokenEncrypted,
        DateTimeOffset expiresAt,
        DateTimeOffset? refreshTokenExpiresAt,
        IReadOnlyList<string> scopeList,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? revokedAt,
        string? login,
        string? name,
        string? email,
        string? avatarUrl
    )
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO auth_tokens
              (user_id, github_account_id, access_token_encrypted, refresh_token_encrypted,
               expires_at, refresh_token_expires_at, scope_list_json, created_at, updated_at,
               revoked_at, login, name, email, avatar_url)
              VALUES ($uid, $ghid, $at, $rt, $exp, $rtexp, $scopes, $cat, $uat, $rev, $login, $name, $email, $avatar)";
        cmd.Parameters.AddWithValue("$uid", userId);
        cmd.Parameters.AddWithValue("$ghid", githubAccountId);
        cmd.Parameters.AddWithValue("$at", accessTokenEncrypted);
        cmd.Parameters.AddWithValue("$rt", refreshTokenEncrypted ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$exp", expiresAt.ToString("o"));
        cmd.Parameters.AddWithValue(
            "$rtexp",
            refreshTokenExpiresAt?.ToString("o") ?? (object)DBNull.Value
        );
        cmd.Parameters.AddWithValue("$scopes", JsonSerializer.Serialize(scopeList));
        cmd.Parameters.AddWithValue("$cat", createdAt.ToString("o"));
        cmd.Parameters.AddWithValue("$uat", updatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("$rev", revokedAt?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$login", login ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$email", email ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$avatar", avatarUrl ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Selected Languages ---

    public async Task<string[]> GetSelectedLanguagesAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT languages_json FROM selected_languages WHERE id=1";
        var result = await cmd.ExecuteScalarAsync();
        if (result is not string json) return Array.Empty<string>();
        return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
    }

    public async Task SaveSelectedLanguagesAsync(string[] languages)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO selected_languages (id, languages_json, updated_at)
              VALUES (1, $l, datetime('now'))";
        cmd.Parameters.AddWithValue("$l", JsonSerializer.Serialize(languages));
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Starred Repos ---

    public async Task<HashSet<string>> GetStarredSlugsAsync()
    {
        await EnsureInitializedAsync();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT slug FROM user_starred";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            slugs.Add(reader.GetString(0));
        return slugs;
    }

    public async Task SetStarredSlugsAsync(IEnumerable<string> slugs)
    {
        await EnsureInitializedAsync();
        using var transaction = _connection!.BeginTransaction();
        try
        {
            using var del = _connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM user_starred";
            await del.ExecuteNonQueryAsync();

            foreach (var slug in slugs)
            {
                using var ins = _connection.CreateCommand();
                ins.Transaction = transaction;
                ins.CommandText =
                    "INSERT OR IGNORE INTO user_starred (slug) VALUES ($s)";
                ins.Parameters.AddWithValue("$s", slug);
                await ins.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> IsStarredAsync(string slug)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM user_starred WHERE slug=$s";
        cmd.Parameters.AddWithValue("$s", slug);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task SetStarredAsync(string slug, bool starred)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        if (starred)
        {
            cmd.CommandText = "INSERT OR IGNORE INTO user_starred (slug) VALUES ($s)";
        }
        else
        {
            cmd.CommandText = "DELETE FROM user_starred WHERE slug=$s";
        }
        cmd.Parameters.AddWithValue("$s", slug);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Watched Repos ---

    public async Task<HashSet<string>> GetWatchedSlugsAsync()
    {
        await EnsureInitializedAsync();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT slug FROM user_watched";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            slugs.Add(reader.GetString(0));
        return slugs;
    }

    public async Task SetWatchedSlugsAsync(IEnumerable<string> slugs)
    {
        await EnsureInitializedAsync();
        using var transaction = _connection!.BeginTransaction();
        try
        {
            using var del = _connection.CreateCommand();
            del.Transaction = transaction;
            del.CommandText = "DELETE FROM user_watched";
            await del.ExecuteNonQueryAsync();

            foreach (var slug in slugs)
            {
                using var ins = _connection.CreateCommand();
                ins.Transaction = transaction;
                ins.CommandText =
                    "INSERT OR IGNORE INTO user_watched (slug) VALUES ($s)";
                ins.Parameters.AddWithValue("$s", slug);
                await ins.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<bool> IsWatchedAsync(string slug)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM user_watched WHERE slug=$s";
        cmd.Parameters.AddWithValue("$s", slug);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task SetWatchedAsync(string slug, bool watched)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        if (watched)
        {
            cmd.CommandText = "INSERT OR IGNORE INTO user_watched (slug) VALUES ($s)";
        }
        else
        {
            cmd.CommandText = "DELETE FROM user_watched WHERE slug=$s";
        }
        cmd.Parameters.AddWithValue("$s", slug);
        await cmd.ExecuteNonQueryAsync();
    }

    // --- Image Cache ---

    public async Task<byte[]?> GetImageCacheAsync(string url, bool allowExpired = false)
    {
        await EnsureInitializedAsync();
        var hash = HashHelper.Sha256Hex(url);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = allowExpired
            ? "SELECT data_blob FROM image_cache WHERE url_hash=$h"
            : "SELECT data_blob FROM image_cache WHERE url_hash=$h AND expires_at_utc > datetime('now')";
        cmd.Parameters.AddWithValue("$h", hash);
        var result = await cmd.ExecuteScalarAsync();
        return result as byte[];
    }

    public async Task SetImageCacheAsync(string url, byte[] data, TimeSpan ttl)
    {
        await EnsureInitializedAsync();
        var hash = HashHelper.Sha256Hex(url);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            @"INSERT OR REPLACE INTO image_cache (url_hash, data_blob, cached_at_utc, expires_at_utc)
              VALUES ($h, $d, datetime('now'), datetime('now', '+' || $ttl || ' seconds'))";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$d", data);
        cmd.Parameters.AddWithValue("$ttl", (int)ttl.TotalSeconds);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CleanupExpiredImagesAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM image_cache WHERE expires_at_utc <= datetime('now')";
        return await cmd.ExecuteNonQueryAsync();
    }

    // --- Utility ---

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    // --- Dismissed Repos ---

    public async Task<HashSet<string>> GetDismissedSlugsAsync()
    {
        await EnsureInitializedAsync();
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT slug FROM dismissed_repos";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            slugs.Add(reader.GetString(0));
        return slugs;
    }

    public async Task<bool> IsDismissedAsync(string slug)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM dismissed_repos WHERE slug=$s";
        cmd.Parameters.AddWithValue("$s", slug);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task SetDismissedAsync(string slug, bool dismissed)
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        if (dismissed)
        {
            cmd.CommandText = "INSERT OR IGNORE INTO dismissed_repos (slug) VALUES ($s)";
        }
        else
        {
            cmd.CommandText = "DELETE FROM dismissed_repos WHERE slug=$s";
        }
        cmd.Parameters.AddWithValue("$s", slug);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> GetDismissedCountAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM dismissed_repos";
        var result = await cmd.ExecuteScalarAsync();
        return result is int count ? count : 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}

internal static class HashHelper
{
    public static string Sha256Hex(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal static class EmbeddedResourceLoader
{
    public static string Load(string resourceName)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var fullName = $"{assembly.GetName().Name}.{resourceName}";
        using var stream = assembly.GetManifestResourceStream(fullName);
        if (stream is null)
            throw new FileNotFoundException($"Embedded resource not found: {fullName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
