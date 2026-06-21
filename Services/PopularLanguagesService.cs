using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace Github_Trend;

public static class PopularLanguagesService
{
    private static HashSet<string>? _cached;
    private static readonly string DataFilePath = Path.Combine(
        AppContext.BaseDirectory,
        "Data",
        "popular-languages.json"
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<HashSet<string>> GetPopularLanguagesAsync()
    {
        if (_cached is not null)
            return _cached;

        try
        {
            var json = await File.ReadAllTextAsync(DataFilePath);
            var doc = JsonSerializer.Deserialize<PopularLanguagesFile>(json, JsonOptions);
            _cached = doc?.PopularLanguages is { Count: > 0 }
                ? new HashSet<string>(doc.PopularLanguages, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to load popular languages, using empty set");
            _cached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return _cached;
    }

    private sealed class PopularLanguagesFile
    {
        public List<string>? PopularLanguages { get; set; }
    }
}
