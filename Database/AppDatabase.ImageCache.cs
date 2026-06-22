using System;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
              VALUES ($h, $d, datetime('now'), $e)";
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$d", data);
        cmd.Parameters.AddWithValue("$e", DateTime.UtcNow.Add(ttl).ToString("yyyy-MM-dd HH:mm:ss"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<int> CleanupExpiredImagesAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM image_cache WHERE expires_at_utc <= datetime('now')";
        return await cmd.ExecuteNonQueryAsync();
    }
}
