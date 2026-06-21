using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Github_Trend.Database;

namespace Github_Trend;

public sealed class SelectedLanguagesStore
{
    private readonly AppDatabase _db;

    public SelectedLanguagesStore(AppDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> LoadAsync()
    {
        try
        {
            var languages = await _db.GetSelectedLanguagesAsync();
            return languages;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to load selected languages from database");
            return Array.Empty<string>();
        }
    }

    public async Task SaveAsync(IEnumerable<string> languages)
    {
        var distinctLanguages = languages
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await _db.SaveSelectedLanguagesAsync(distinctLanguages);
    }
}
