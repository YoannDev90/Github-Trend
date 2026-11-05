using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
}
