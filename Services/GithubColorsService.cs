using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend;

public static class GithubColorsService
{
    private static readonly HttpClient Http = new();

    public static async Task<GithubColorsCatalog> FetchAsync()
    {
        var json = await Http.GetStringAsync(Constants.GitHubColorsUrl);

        var colors = JsonSerializer.Deserialize<Dictionary<string, GithubColorEntry>>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new Dictionary<string, GithubColorEntry>();

        return new GithubColorsCatalog(colors);
    }
}

