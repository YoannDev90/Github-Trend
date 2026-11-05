using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend.Database;

partial class AppDatabase
{
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
}
