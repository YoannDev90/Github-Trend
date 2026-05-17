using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using Serilog;

namespace Github_Trend;

public static class GithubTrendingService
{
    private static readonly HttpClient Http = new();
    private static readonly TimeSpan CacheTtl = Constants.Trending.TrendingCacheTtl;
    private static readonly string CacheFilePath;
    private static readonly SemaphoreSlim EnrichmentLimiter = new(Constants.Trending.MaxParallelEnrichmentRequests);

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
        Log.Information("Trending fetch started (force={Force}, since={Since}, language={Language})", force, since ?? "<null>", language ?? "<null>");
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
                        Log.Information("Trending cache hit ({Count}) for {CacheKey}", cached.Count, cacheKey);
                        return await EnrichTrendingAsync(cached);
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

        Log.Debug("Trending request URL: {Url}", url);

        // Attempt network fetch and update cache. If network fails, fallback to cache if available.
        try
        {
            var json = await Http.GetStringAsync(url);
            var trending = DeserializeTrending(json) ?? new List<GithubTrendingRepository>();
            Log.Information("Trending network fetch ok ({Count}) for {CacheKey}", trending.Count, cacheKey);

            // try write cache (best-effort)
            try
            {
                await File.WriteAllTextAsync(cacheFile, json);
                Log.Debug("Trending cache updated: {CacheFile}", cacheFile);
            }
            catch
            {
                // ignore cache write failures
            }

            return await EnrichTrendingAsync(trending);
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
                        Log.Warning("Trending stale-cache fallback ({Count}) for {CacheKey}", cached.Count, cacheKey);
                        return await EnrichTrendingAsync(cached);
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

    private static async Task<List<GithubTrendingRepository>> EnrichTrendingAsync(IEnumerable<GithubTrendingRepository> repositories)
    {
        var enrichTasks = repositories.Select(async repo =>
        {
            await EnrichmentLimiter.WaitAsync();
            try
            {
                return await GithubRepositoryDetailsService.EnrichAsync(repo);
            }
            finally
            {
                EnrichmentLimiter.Release();
            }
        });

        var enriched = await Task.WhenAll(enrichTasks);
        return enriched.ToList();
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

