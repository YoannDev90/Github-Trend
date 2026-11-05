using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
    public async Task<int> GetTrendingCacheCountAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM trending_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<int> GetRepoDetailsCacheCountAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM repository_details_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<int> GetImageCacheCountAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM image_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? (int)l : 0;
    }

    public async Task<long> GetTrendingCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data_json)), 0) FROM trending_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : 0;
    }

    public async Task<long> GetRepoDetailsCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data_json)), 0) FROM repository_details_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : 0;
    }

    public async Task<long> GetImageCacheSizeAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(LENGTH(data_blob)), 0) FROM image_cache";
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : 0;
    }
}
