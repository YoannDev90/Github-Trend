using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Github_Trend.Database;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GithubColorsService : IGithubColorsService, IAsyncDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AppDatabase _db;
    private readonly HttpClient _http;

    public GithubColorsService(AppDatabase db, HttpClient? httpClient = null)
    {
        _db = db;
        _http = httpClient ?? HttpClientFactory.Create();
    }

    public async Task<GithubColorsCatalog> FetchAsync(bool force = false)
    {
        if (!force)
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
            var json = await _http.GetStringAsync(Constants.GitHubColorsUrl);
            var colors = DeserializeColors(json) ?? new Dictionary<string, GithubColorEntry>();

            try
            {
                await _db.SetColorsCacheAsync(json, CacheTtl);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to write colors cache to database");
            }

            return new GithubColorsCatalog(colors);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Colors network fetch failed");

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

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await ValueTask.CompletedTask;
    }
}
