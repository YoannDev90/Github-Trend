using System;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
        using var transaction = _connection!.BeginTransaction();
        try
        {
            using var deleteCmd = _connection.CreateCommand();
            deleteCmd.Transaction = transaction;
            deleteCmd.CommandText = "DELETE FROM colors_cache";
            await deleteCmd.ExecuteNonQueryAsync();

            using var insertCmd = _connection.CreateCommand();
            insertCmd.Transaction = transaction;
            insertCmd.CommandText =
                "INSERT INTO colors_cache (data_json, cached_at_utc, expires_at_utc) VALUES ($d, datetime('now'), $e)";
            insertCmd.Parameters.AddWithValue("$d", dataJson);
            insertCmd.Parameters.AddWithValue("$e", DateTime.UtcNow.Add(ttl).ToString("yyyy-MM-dd HH:mm:ss"));
            await insertCmd.ExecuteNonQueryAsync();

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
