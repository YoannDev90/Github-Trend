using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class SelectedLanguagesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;

    public SelectedLanguagesStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");

        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "selected-languages.json");
    }

    public async Task<IReadOnlyList<string>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return Array.Empty<string>();

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }

    public async Task SaveAsync(IEnumerable<string> languages)
    {
        var distinctLanguages = languages
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(language => language, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var json = JsonSerializer.Serialize(distinctLanguages, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}