using System;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
              VALUES ($k, $s, $l, $d, datetime('now'), $e)";
        cmd.Parameters.AddWithValue("$k", cacheKey);
        cmd.Parameters.AddWithValue("$s", since ?? "");
        cmd.Parameters.AddWithValue("$l", language ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$d", dataJson);
        cmd.Parameters.AddWithValue("$e", DateTime.UtcNow.Add(ttl).ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CleanupExpiredTrendingAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM trending_cache WHERE expires_at_utc <= datetime('now')";
        return await cmd.ExecuteNonQueryAsync();
    }
}
