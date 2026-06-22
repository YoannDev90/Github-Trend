using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Github_Trend.Database;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GithubRepositoryDetailsService : IRepositoryDetailsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly GitHubGraphQlService _graphQlService;
    private readonly GitHubAuthenticationService _authService;
    private readonly GitHubRateLimitService _rateLimitService;
    private readonly AppDatabase _db;

    private readonly ConcurrentDictionary<string, WeakReference<Bitmap>> _imageCache = new();

    public GithubRepositoryDetailsService(
        GitHubGraphQlService graphQlService,
        GitHubAuthenticationService authService,
        GitHubRateLimitService rateLimitService,
        AppDatabase db,
        HttpClient? httpClient = null
    )
    {
        _graphQlService = graphQlService;
        _authService = authService;
        _rateLimitService = rateLimitService;
        _db = db;
        _http = httpClient ?? HttpClientFactory.Create();
    }

    public async Task<GithubTrendingRepository> EnrichAsync(
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
        var enriched = repository.CloneWith(
            htmlUrl: details.HtmlUrl,
            bannerUrl: details.BannerUrl,
            bannerImage: details.BannerImage,
            license: details.License,
            contributors: details.Contributors,
            contributorsTotalCount: details.ContributorsTotalCount,
            topics: details.Topics,
            updatedAt: details.UpdatedAt
        );
        enriched.IsEnriched = true;
        return enriched;
    }

    private async Task<RepositoryDetails> GetDetailsAsync(
        string owner,
        string name,
        CancellationToken ct = default
    )
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

        return await FetchDetailsAsync(owner, name, ct);
    }

    private async Task<RepositoryDetails> FetchDetailsAsync(
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

        if (_rateLimitService.IsInCooldown)
        {
            Log.Information("Skipping GraphQL enrichment for {Owner}/{Name} during cooldown", owner, name);
            return details;
        }

        try
        {
            var enrichmentData = await _graphQlService.GetRepositoryDetailsAsync(owner, name, ct);
            if (enrichmentData is not null)
            {
                details.HtmlUrl = enrichmentData.HtmlUrl;
                details.License = enrichmentData.License;
                details.Topics = enrichmentData.Topics;
                details.UpdatedAt = enrichmentData.UpdatedAt;
            }

            var isAuthenticated = _authService.IsConnected;
            var contributorCount = isAuthenticated
                ? Constants.Trending.MaxContributorPreviewCount
                : Constants.Trending.AnonymousContributorPreviewCount;

            var contributorData = await FetchContributorsViaRestAsync(
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

            details.BannerImage = await TryLoadBitmapAsync(details.BannerUrl, BannerMaxWidth);

            foreach (var c in details.Contributors)
            {
                c.AvatarImage = await TryLoadBitmapAsync(c.AvatarUrl, AvatarMaxSize, AvatarMaxSize);
            }

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
        catch (Exception ex)
        {
            Log.Warning(ex, "GraphQL repo details fetch failed for {Owner}/{Name}", owner, name);
        }

        return details;
    }

    private async Task<RepositoryDetails> BuildDetailsFromRecordAsync(
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

        result.BannerImage = await TryLoadBitmapAsync(result.BannerUrl, BannerMaxWidth);

        foreach (var c in result.Contributors)
        {
            c.AvatarImage = await TryLoadBitmapAsync(c.AvatarUrl, AvatarMaxSize, AvatarMaxSize);
        }

        return result;
    }

    public async Task<Bitmap?> TryLoadBitmapAsync(string? url, int? maxWidth = null, int? maxHeight = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (_imageCache.TryGetValue(url!, out var weakRef) && weakRef.TryGetTarget(out var cached))
            return cached;

        try
        {
            byte[]? imageBytes = null;

            try
            {
                imageBytes = await _db.GetImageCacheAsync(url);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Failed to read image cache for {Url}", url);
            }

            if (imageBytes is null)
            {
                imageBytes = await _http.GetByteArrayAsync(url);

                try
                {
                    await _db.SetImageCacheAsync(url, imageBytes, Constants.Trending.ImageCacheTtl);
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Failed to write image cache for {Url}", url);
                }
            }

            var bitmap = CreateBitmapFromBytes(imageBytes, maxWidth, maxHeight);

            _imageCache[url!] = new WeakReference<Bitmap>(bitmap);

            if (++_cacheOpsSinceCleanup >= 20)
            {
                _cacheOpsSinceCleanup = 0;
                CleanupImageCache();
            }

            return bitmap;
        }
        catch (Exception ex)
        {
            try
            {
                var staleBytes = await _db.GetImageCacheAsync(url, allowExpired: true);
                if (staleBytes is not null)
                    return CreateBitmapFromBytes(staleBytes, maxWidth, maxHeight);
            }
            catch { }

            Log.Debug(ex, "Image load failed for {Url}", url);
            return null;
        }
    }

    private static Bitmap CreateBitmapFromBytes(byte[] bytes, int? maxWidth = null, int? maxHeight = null)
    {
        using var stream = new MemoryStream(bytes, false);
        var bitmap = new Bitmap(stream);

        if (maxWidth is null && maxHeight is null)
            return bitmap;

        var targetW = maxWidth ?? bitmap.PixelSize.Width;
        var targetH = maxHeight ?? bitmap.PixelSize.Height;
        var scale = Math.Min(
            (double)targetW / bitmap.PixelSize.Width,
            (double)targetH / bitmap.PixelSize.Height
        );

        if (scale >= 1.0)
            return bitmap;

        var newW = Math.Max(1, (int)(bitmap.PixelSize.Width * scale));
        var newH = Math.Max(1, (int)(bitmap.PixelSize.Height * scale));
        var resized = bitmap.CreateScaledBitmap(new PixelSize(newW, newH), BitmapInterpolationMode.MediumQuality);
        bitmap.Dispose();
        return resized;
    }

    private const int BannerMaxWidth = 640;
    private const int AvatarMaxSize = 64;

    private int _cacheOpsSinceCleanup;
    private void CleanupImageCache()
    {
        foreach (var kvp in _imageCache)
        {
            if (!kvp.Value.TryGetTarget(out _))
                _imageCache.TryRemove(kvp.Key, out _);
        }
    }

    private sealed record RepositoryDetails
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

    private sealed record RepositoryDetailsCacheRecord
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

    private sealed record ContributorCacheRecord
    {
        public string? Login { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private async Task<ContributorFetchData> FetchContributorsViaRestAsync(
        string owner,
        string name,
        int count,
        CancellationToken ct = default
    )
    {
        var url = $"https://api.github.com/repos/{owner}/{name}/contributors?per_page={count}&page=1";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            var token = await _authService.GetAccessTokenAsync(false);
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var contributors = JsonSerializer.Deserialize<List<ContributorRestResponse>>(json, JsonOptions)
                ?? new List<ContributorRestResponse>();

            var items = contributors
                .Where(c => !string.IsNullOrWhiteSpace(c.Login))
                .Select(c => new ContributorData(c.Login!, c.AvatarUrl))
                .ToList();

            var totalCount = items.Count < count
                ? items.Count
                : ParseContributorTotalCount(response, count);

            return new ContributorFetchData(items, totalCount);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "REST contributors fetch failed for {Owner}/{Name}", owner, name);
            return new ContributorFetchData(Array.Empty<ContributorData>(), 0);
        }
    }

    private static int ParseContributorTotalCount(HttpResponseMessage response, int perPage)
    {
        if (!response.Headers.TryGetValues("Link", out var linkValues))
            return perPage;

        var linkHeader = string.Join(",", linkValues);
        foreach (var part in linkHeader.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!part.Contains("rel=\"last\"", StringComparison.OrdinalIgnoreCase))
                continue;

            var pageMatch = System.Text.RegularExpressions.Regex.Match(part, @"page=(\d+)");
            if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out var lastPage))
                return lastPage * perPage;
        }

        return perPage;
    }

    private sealed record ContributorRestResponse
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; init; }
    }
}
