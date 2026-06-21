using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Github_Trend.Database;
using Github_Trend.Services;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend;

public static class GithubRepositoryDetailsService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static GitHubGraphQlService? _graphQlService;
    private static GitHubAuthenticationService? _authService;
    private static GitHubRateLimitService? _rateLimitService;
    private static AppDatabase? _db;

    // In-memory bitmap cache
    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> ImageCache = new();

    public static void Initialize(
        GitHubGraphQlService graphQlService,
        GitHubAuthenticationService authService,
        GitHubRateLimitService rateLimitService,
        AppDatabase db
    )
    {
        _graphQlService = graphQlService;
        _authService = authService;
        _rateLimitService = rateLimitService;
        _db = db;
    }

    public static async Task<GithubTrendingRepository> EnrichAsync(
        GithubTrendingRepository repository,
        CancellationToken ct = default
    )
    {
        if (!RepositoryUrlParser.TryParse(
            !string.IsNullOrWhiteSpace(repository.Name) ? repository.Name : repository.Repository,
            out var owner,
            out var name
        ))
        {
            return repository.CloneWith(htmlUrl: repository.RepositoryLink);
        }

        var details = await GetDetailsAsync(owner, name, ct);
        return repository.CloneWith(
            htmlUrl: details.HtmlUrl,
            bannerUrl: details.BannerUrl,
            bannerImage: details.BannerImage,
            license: details.License,
            contributors: details.Contributors,
            contributorsTotalCount: details.ContributorsTotalCount,
            topics: details.Topics,
            updatedAt: details.UpdatedAt
        );
    }

    private static async Task<RepositoryDetails> GetDetailsAsync(
        string owner,
        string name,
        CancellationToken ct = default
    )
    {
        // 1. Try memory/disk cache via SQLite
        if (_db is not null)
        {
            try
            {
                var cached = await _db.GetRepositoryDetailsCacheAsync(owner, name);
                if (cached is not null)
                {
                    var record = JsonSerializer.Deserialize<RepositoryDetailsCacheRecord>(
                        cached.Value.dataJson!, JsonOptions
                    );
                    if (record is not null)
                    {
                        Log.Debug("Repo details cache hit for {Owner}/{Name}", owner, name);
                        return await BuildDetailsFromRecordAsync(record, owner, name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read repo details cache for {Owner}/{Name}", owner, name);
            }
        }

        // 2. Fetch from GitHub GraphQL API
        return await FetchDetailsAsync(owner, name, ct);
    }

    private static async Task<RepositoryDetails> FetchDetailsAsync(
        string owner,
        string name,
        CancellationToken ct = default
    )
    {
        var htmlUrl = $"https://github.com/{owner}/{name}";
        var bannerUrl = $"https://opengraph.githubassets.com/1/{owner}/{name}";

        var details = new RepositoryDetails
        {
            HtmlUrl = htmlUrl,
            BannerUrl = bannerUrl,
        };

        if (_rateLimitService?.IsInCooldown == true)
        {
            Log.Information("Skipping GraphQL enrichment for {Owner}/{Name} during cooldown", owner, name);
            return details;
        }

        if (_graphQlService is null)
        {
            Log.Warning("GraphQL service not initialized for {Owner}/{Name}", owner, name);
            return details;
        }

        try
        {
            // Fetch repo details (topics, license, etc.)
            var enrichmentData = await _graphQlService.GetRepositoryDetailsAsync(owner, name, ct);
            if (enrichmentData is not null)
            {
                details.HtmlUrl = enrichmentData.HtmlUrl;
                details.License = enrichmentData.License;
                details.Topics = enrichmentData.Topics;
                details.UpdatedAt = enrichmentData.UpdatedAt;
            }

            // Fetch contributors
            var isAuthenticated = _authService?.IsConnected == true;
            var contributorCount = isAuthenticated
                ? Constants.Trending.MaxContributorPreviewCount
                : Constants.Trending.AnonymousContributorPreviewCount;

            var contributorData = await _graphQlService.GetContributorsAsync(
                owner, name, contributorCount, ct
            );

            var contributors = new List<GithubContributorPreview>();
            foreach (var c in contributorData.Contributors)
            {
            contributors.Add(new GithubContributorPreview(
                c.Login,
                c.AvatarUrl,
                null
            ));
        }

        details.Contributors = contributors;
        details.ContributorsTotalCount = contributorData.TotalCount;

        // Load banner image
        details.BannerImage = await TryLoadBitmapAsync(details.BannerUrl);

        // Load avatar images
        foreach (var c in details.Contributors)
        {
            c.AvatarImage = await TryLoadBitmapAsync(c.AvatarUrl);
        }

        // Save to cache
            if (_db is not null)
            {
                try
                {
                    var record = new RepositoryDetailsCacheRecord
                    {
                        HtmlUrl = details.HtmlUrl,
                        BannerUrl = details.BannerUrl,
                        License = details.License,
                        Contributors = details.Contributors.ConvertAll(c => new ContributorCacheRecord
                        {
                            Login = c.Login,
                            AvatarUrl = c.AvatarUrl,
                        }),
                        ContributorsTotalCount = details.ContributorsTotalCount,
                        Topics = details.Topics,
                        UpdatedAt = details.UpdatedAt,
                        CachedAtUtc = DateTimeOffset.UtcNow,
                    };
                    var json = JsonSerializer.Serialize(record, JsonOptions);
                    await _db.SetRepositoryDetailsCacheAsync(
                        owner, name, json, null, Constants.Trending.RepositoryDetailsCacheTtl
                    );
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to save repo details cache for {Owner}/{Name}", owner, name);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GraphQL repo details fetch failed for {Owner}/{Name}", owner, name);
        }

        return details;
    }

    private static async Task<RepositoryDetails> BuildDetailsFromRecordAsync(
        RepositoryDetailsCacheRecord cached,
        string owner,
        string name
    )
    {
        var fallbackBannerUrl = $"https://opengraph.githubassets.com/1/{owner}/{name}";
        var bannerUrl = string.IsNullOrWhiteSpace(cached.BannerUrl) ? fallbackBannerUrl : cached.BannerUrl;
        var contributors = new List<GithubContributorPreview>();

        foreach (var c in cached.Contributors)
        {
            if (string.IsNullOrWhiteSpace(c.Login)) continue;
            contributors.Add(new GithubContributorPreview(
                c.Login,
                c.AvatarUrl,
                null
            ));
        }

        var result = new RepositoryDetails
        {
            HtmlUrl = string.IsNullOrWhiteSpace(cached.HtmlUrl) ? $"https://github.com/{owner}/{name}" : cached.HtmlUrl,
            BannerUrl = bannerUrl,
            License = cached.License,
            Contributors = contributors,
            ContributorsTotalCount = cached.ContributorsTotalCount,
            Topics = cached.Topics.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            UpdatedAt = cached.UpdatedAt,
        };

        // Load banner image
        result.BannerImage = await TryLoadBitmapAsync(result.BannerUrl);

        // Load avatar images
        foreach (var c in result.Contributors)
        {
            c.AvatarImage = await TryLoadBitmapAsync(c.AvatarUrl);
        }

        return result;
    }

    public static async Task<Bitmap?> TryLoadBitmapAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // Check in-memory cache
        if (ImageCache.TryGetValue(url!, out var weakRef) && weakRef.TryGetTarget(out var cached))
            return cached;

        try
        {
            byte[]? imageBytes = null;

            // Try SQLite image cache first
            if (_db is not null)
            {
                try
                {
                    imageBytes = await _db.GetImageCacheAsync(url);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to read image cache for {Url}", url);
                }
            }

            // Download if not cached
            if (imageBytes is null)
            {
                imageBytes = await Http.GetByteArrayAsync(url);

                if (_db is not null)
                {
                    try
                    {
                        await _db.SetImageCacheAsync(url, imageBytes, Constants.Trending.ImageCacheTtl);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "Failed to write image cache for {Url}", url);
                    }
                }
            }

            var bitmap = CreateBitmapFromBytes(imageBytes);

            // Store in memory cache with WeakReference
            ImageCache[url!] = new WeakReference<Bitmap>(bitmap);

            return bitmap;
        }
        catch (Exception ex)
        {
            // Try stale image cache
            if (_db is not null)
            {
                try
                {
                    var staleBytes = await _db.GetImageCacheAsync(url, allowExpired: true);
                    if (staleBytes is not null)
                        return CreateBitmapFromBytes(staleBytes);
                }
                catch { }
            }

            Log.Debug(ex, "Image load failed for {Url}", url);
            return null;
        }
    }

    private static Bitmap CreateBitmapFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        return new Bitmap(stream);
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

    // --- Internal models ---

    private sealed class RepositoryDetails
    {
        public string HtmlUrl { get; set; } = string.Empty;
        public string BannerUrl { get; set; } = string.Empty;
        public Bitmap? BannerImage { get; set; }
        public string? License { get; set; }
        public List<GithubContributorPreview> Contributors { get; set; } = new();
        public int ContributorsTotalCount { get; set; }
        public List<string> Topics { get; set; } = new();
        public string? UpdatedAt { get; set; }
    }

    private sealed class RepositoryDetailsCacheRecord
    {
        public string? HtmlUrl { get; set; }
        public string? BannerUrl { get; set; }
        public string? License { get; set; }
        public int ContributorsTotalCount { get; set; }
        public List<ContributorCacheRecord> Contributors { get; set; } = new();
        public List<string> Topics { get; set; } = new();
        public string? UpdatedAt { get; set; }
        public DateTimeOffset CachedAtUtc { get; set; }
    }

    private sealed class ContributorCacheRecord
    {
        public string? Login { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
