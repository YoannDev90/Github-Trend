using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Github_Trend;

public static class GithubColorsService
{
    private static readonly HttpClient Http = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly string CacheFilePath;

    static GithubColorsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend");

        Directory.CreateDirectory(folder);
        CacheFilePath = Path.Combine(folder, "colors-cache.json");
    }

    // If force == true, always attempt a network fetch and update the cache. On network failure,
    // fallback to any existing cached file (even if stale). If force == false, use cache when fresh.
    public static async Task<GithubColorsCatalog> FetchAsync(bool force = false)
    {
        // If not forcing, try return fresh cache first
        if (!force && File.Exists(CacheFilePath))
            try
            {
                var lastWrite = File.GetLastWriteTimeUtc(CacheFilePath);
                if (DateTime.UtcNow - lastWrite < CacheTtl)
                {
                    var cachedJson = await File.ReadAllTextAsync(CacheFilePath);
                    var cached = DeserializeColors(cachedJson);
                    if (cached != null) return new GithubColorsCatalog(cached);
                }
            }
            catch
            {
                // ignore cache read errors and attempt network fetch
            }

        // Attempt network fetch and update cache. If network fails, fallback to cache if available.
        try
        {
            var json = await Http.GetStringAsync(Constants.GitHubColorsUrl);
            var colors = DeserializeColors(json) ?? new Dictionary<string, GithubColorEntry>();

            // try write cache (best-effort)
            try
            {
                await File.WriteAllTextAsync(CacheFilePath, json);
            }
            catch
            {
                // ignore cache write failures
            }

            return new GithubColorsCatalog(colors);
        }
        catch
        {
            // network failed, try to return cache (even stale) if present
            if (File.Exists(CacheFilePath))
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(CacheFilePath);
                    var cached = DeserializeColors(cachedJson);
                    if (cached != null) return new GithubColorsCatalog(cached);
                }
                catch
                {
                    // fall through to rethrow
                }

            throw; // rethrow original network exception
        }
    }

    private static Dictionary<string, GithubColorEntry>? DeserializeColors(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, GithubColorEntry>>(json, new JsonSerializerOptions
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