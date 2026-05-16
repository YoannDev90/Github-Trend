using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend;

public static class GithubTrendingService
{
    private static readonly HttpClient Http = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly string CacheFilePath;

    static GithubTrendingService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");

        Directory.CreateDirectory(folder);
        CacheFilePath = Path.Combine(folder, "trending-cache.json");
    }

    public static async Task<List<GithubTrendingRepository>> FetchAsync(bool force = false)
    {
        // If not forcing, try return fresh cache first
        if (!force && File.Exists(CacheFilePath))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(CacheFilePath);
                if (DateTime.UtcNow - lastWrite < CacheTtl)
                {
                    var cachedJson = await File.ReadAllTextAsync(CacheFilePath);
                    var cached = DeserializeTrending(cachedJson);
                    if (cached != null)
                    {
                        return cached;
                    }
                }
            }
            catch
            {
                // ignore cache read errors and attempt network fetch
            }
        }

        // Attempt network fetch and update cache. If network fails, fallback to cache if available.
        try
        {
            var json = await Http.GetStringAsync(Constants.GitHubTrendingUrl);
            var trending = DeserializeTrending(json) ?? new List<GithubTrendingRepository>();

            // try write cache (best-effort)
            try
            {
                await File.WriteAllTextAsync(CacheFilePath, json);
            }
            catch
            {
                // ignore cache write failures
            }

            return trending;
        }
        catch
        {
            // network failed, try to return cache (even stale) if present
            if (File.Exists(CacheFilePath))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(CacheFilePath);
                    var cached = DeserializeTrending(cachedJson);
                    if (cached != null)
                    {
                        return cached;
                    }
                }
                catch
                {
                    // fall through to rethrow
                }
            }

            throw; // rethrow original network exception
        }
    }

    private static List<GithubTrendingRepository>? DeserializeTrending(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<GithubTrendingRepository>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }
}

