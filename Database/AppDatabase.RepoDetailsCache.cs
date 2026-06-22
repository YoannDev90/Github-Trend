using System;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
              VALUES ($o, $n, $d, $e, datetime('now'), $ex)";
        cmd.Parameters.AddWithValue("$o", owner);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$d", dataJson);
        cmd.Parameters.AddWithValue("$e", etag ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$ex", DateTime.UtcNow.Add(ttl).ToString("yyyy-MM-dd HH:mm:ss"));
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
}
