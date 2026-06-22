using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Github_Trend.Database;

public sealed partial class AppDatabase : IAsyncDisposable
{
    private const int SchemaVersion = 2;
    private static readonly string DatabasePath;

    private readonly SemaphoreSlim _dbLock = new(1, 1);
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

        await _dbLock.WaitAsync();
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

            _ = RunPeriodicCleanupAsync();

            Log.Information("Database initialized at {Path}", DatabasePath);
        }
        finally
        {
            _dbLock.Release();
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
            "SELECT name FROM sqlite_master WHERE type='table' AND name=$name";
        cmd.Parameters.AddWithValue("$name", tableName);
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

    private async Task EnsureInitializedAsync()
    {
        if (!_initialized)
            await InitializeAsync();
    }

    private async Task RunPeriodicCleanupAsync()
    {
        try
        {
            await CleanupExpiredImagesAsync();
            await CleanupExpiredTrendingAsync();
            await CleanupExpiredRepoDetailsAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Periodic cleanup failed");
        }
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
