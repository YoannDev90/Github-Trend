using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
    public async Task ClearAllTrendingCacheAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM trending_cache";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearAllRepoDetailsCacheAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM repository_details_cache";
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ClearAllImageCacheAsync()
    {
        await EnsureInitializedAsync();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "DELETE FROM image_cache";
        await cmd.ExecuteNonQueryAsync();
    }
}
