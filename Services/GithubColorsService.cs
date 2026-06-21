using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Github_Trend.Database;
using Serilog;

namespace Github_Trend;

public static class GithubColorsService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static AppDatabase? _db;

    public static void SetDatabase(AppDatabase db)
    {
        _db = db;
    }

    public static async Task<GithubColorsCatalog> FetchAsync(bool force = false)
    {
        if (!force && _db is not null)
        {
            try
            {
                var cachedJson = await _db.GetColorsCacheAsync();
                if (cachedJson is not null)
                {
                    var cached = DeserializeColors(cachedJson);
                    if (cached is not null)
                        return new GithubColorsCatalog(cached);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read colors cache from database");
            }
        }

        try
        {
            var json = await Http.GetStringAsync(Constants.GitHubColorsUrl);
            var colors = DeserializeColors(json) ?? new Dictionary<string, GithubColorEntry>();

            if (_db is not null)
            {
                try
                {
                    await _db.SetColorsCacheAsync(json, CacheTtl);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to write colors cache to database");
                }
            }

            return new GithubColorsCatalog(colors);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Colors network fetch failed");

            if (_db is not null)
            {
                try
                {
                    var staleJson = await _db.GetColorsCacheAsync();
                    if (staleJson is not null)
                    {
                        var cached = DeserializeColors(staleJson);
                        if (cached is not null)
                        {
                            Log.Warning("Colors stale-cache fallback ({Count})", cached.Count);
                            return new GithubColorsCatalog(cached);
                        }
                    }
                }
                catch (Exception cacheEx)
                {
                    Log.Debug(cacheEx, "Failed to read stale colors cache");
                }
            }

            throw;
        }
    }

    private static Dictionary<string, GithubColorEntry>? DeserializeColors(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, GithubColorEntry>>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to deserialize colors JSON");
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        return new HttpClient(handler);
    }
}
