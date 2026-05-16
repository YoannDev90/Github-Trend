using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

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

    public static async Task<List<GithubTrendingRepository>> FetchAsync(bool force = false, string? since = null, string? language = null)
    {
        var cacheKey = $"trending-{since}-{language}.json";
        var cacheFile = Path.Combine(Path.GetDirectoryName(CacheFilePath)!, cacheKey);
        Console.WriteLine($"[Trending] FetchAsync force={force} since={since ?? "<null>"} language={language ?? "<null>"}");
        Debug.WriteLine($"[Trending] cache file: {cacheFile}");

        // If not forcing, try return fresh cache first
        if (!force && File.Exists(cacheFile))
        {
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(cacheFile);
                if (DateTime.UtcNow - lastWrite < CacheTtl)
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile);
                    var cached = DeserializeTrending(cachedJson);
                    if (cached != null)
                    {
                        Console.WriteLine($"[Trending] cache hit ({cached.Count}) for {cacheKey}");
                        return cached;
                    }
                }
            }
            catch
            {
                // ignore cache read errors and attempt network fetch
            }
        }

        // Build URL with query parameters
        var url = Constants.GitHubTrendingUrl;
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(since))
            queryParts.Add($"since={Uri.EscapeDataString(since)}");
        if (!string.IsNullOrWhiteSpace(language))
            queryParts.Add($"language={Uri.EscapeDataString(language)}");

        if (queryParts.Count > 0)
            url += "?" + string.Join("&", queryParts);

        Console.WriteLine($"[Trending] request url: {url}");

        // Attempt network fetch and update cache. If network fails, fallback to cache if available.
        try
        {
            var json = await Http.GetStringAsync(url);
            var trending = DeserializeTrending(json) ?? new List<GithubTrendingRepository>();
            Console.WriteLine($"[Trending] network ok ({trending.Count}) for {cacheKey}");

            // try write cache (best-effort)
            try
            {
                await File.WriteAllTextAsync(cacheFile, json);
                Console.WriteLine($"[Trending] cache updated: {cacheFile}");
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
            if (File.Exists(cacheFile))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(cacheFile);
                    var cached = DeserializeTrending(cachedJson);
                    if (cached != null)
                    {
                        Console.WriteLine($"[Trending] stale cache fallback ({cached.Count}) for {cacheKey}");
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

