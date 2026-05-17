using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Serilog;

namespace Github_Trend;

public static class GithubRepositoryDetailsService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Lazy<Task<RepositoryDetails>>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly GitHubAuthenticationService AuthService = new();
    private static readonly SemaphoreSlim AnonymousContributorLimiter = new(1, 1);
    private static readonly SemaphoreSlim DiskCacheLock = new(1, 1);
    private static readonly object RateLimitStateSync = new();
    private static DateTimeOffset? _rateLimitCooldownUntilUtc;
    private static readonly string RepositoryDetailsCacheDirectory = CreateCacheDirectory("repo-details");
    private static readonly string ImageCacheDirectory = CreateCacheDirectory("images");

    public static async Task<GithubTrendingRepository> EnrichAsync(GithubTrendingRepository repository)
    {
        if (!TryGetRepositorySlug(repository, out var owner, out var name))
        {
            return repository.CloneWith(htmlUrl: repository.RepositoryLink);
        }

        var details = await GetDetailsAsync(owner, name);
        return repository.CloneWith(
            htmlUrl: details.HtmlUrl,
            bannerUrl: details.BannerUrl,
            bannerImage: details.BannerImage,
            license: details.License,
            contributors: details.Contributors,
            contributorsTotalCount: details.ContributorsTotalCount,
            topics: details.Topics,
            updatedAt: details.UpdatedAt);
    }

    private static async Task<RepositoryDetails> GetDetailsAsync(string owner, string name)
    {
        var cacheKey = $"{owner}/{name}";
        var lazy = Cache.GetOrAdd(cacheKey, _ => new Lazy<Task<RepositoryDetails>>(() => FetchDetailsAsync(owner, name)));
        return await lazy.Value;
    }

    private static async Task<RepositoryDetails> FetchDetailsAsync(string owner, string name)
    {
        var htmlUrl = $"https://github.com/{owner}/{name}";
        var bannerUrl = $"https://opengraph.githubassets.com/1/{owner}/{name}";

        var freshCache = await TryLoadRepositoryDetailsFromDiskAsync(owner, name, allowExpired: false, fallbackHtmlUrl: htmlUrl, fallbackBannerUrl: bannerUrl);
        if (freshCache is not null)
        {
            return freshCache;
        }

        var details = new RepositoryDetails
        {
            HtmlUrl = htmlUrl,
            BannerUrl = bannerUrl,
            BannerImage = await TryLoadBitmapAsync(bannerUrl)
        };

        if (TryGetRateLimitCooldownRemaining(out var remaining))
        {
            Log.Information("Skipping GitHub API enrichment for {Owner}/{Name} during cooldown ({Seconds}s remaining)", owner, name, Math.Max(1, (int)remaining.TotalSeconds));
            var staleCache = await TryLoadRepositoryDetailsFromDiskAsync(owner, name, allowExpired: true, fallbackHtmlUrl: htmlUrl, fallbackBannerUrl: bannerUrl);
            if (staleCache is not null)
            {
                return staleCache;
            }

            return details;
        }

        try
        {
            var apiUrl = $"{Constants.GitHub.ApiBaseUrl}/repos/{owner}/{name}";
            using var response = await SendGitHubResponseAsync(apiUrl);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync();
            var repo = JsonSerializer.Deserialize<GitHubRepositoryResponse>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (repo is null)
            {
                return details;
            }

            details.HtmlUrl = repo.HtmlUrl ?? htmlUrl;
            details.License = NormalizeLicense(repo.License);
            details.Topics = repo.Topics?.Where(topic => !string.IsNullOrWhiteSpace(topic)).Take(6).ToList() ?? new List<string>();
            details.UpdatedAt = repo.UpdatedAt;

            var isAuthenticated = await HasAuthenticatedGitHubAccessAsync();
            var contributorResult = await FetchContributorsAsync(repo.ContributorsUrl, isAuthenticated);
            details.Contributors = contributorResult.Contributors.ToList();
            details.ContributorsTotalCount = contributorResult.TotalCount;

            await TrySaveRepositoryDetailsToDiskAsync(owner, name, details);
        }
        catch (RateLimitCooldownException)
        {
            var staleCache = await TryLoadRepositoryDetailsFromDiskAsync(owner, name, allowExpired: true, fallbackHtmlUrl: htmlUrl, fallbackBannerUrl: bannerUrl);
            if (staleCache is not null)
            {
                return staleCache;
            }

            return details;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitHub repo details fetch failed for {Owner}/{Name}", owner, name);

            var staleCache = await TryLoadRepositoryDetailsFromDiskAsync(owner, name, allowExpired: true, fallbackHtmlUrl: htmlUrl, fallbackBannerUrl: bannerUrl);
            if (staleCache is not null)
            {
                return staleCache;
            }
        }

        return details;
    }

    private static bool TryGetRepositorySlug(GithubTrendingRepository repository, out string owner, out string name)
    {
        owner = string.Empty;
        name = string.Empty;

        var candidate = !string.IsNullOrWhiteSpace(repository.Name)
            ? repository.Name
            : repository.Repository;

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            candidate = candidate.Trim();
            if (candidate.Contains('/'))
            {
                var parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length >= 2)
                {
                    owner = parts[^2];
                    name = parts[^1];
                    return true;
                }
            }
        }

        if (Uri.TryCreate(repository.Repository, UriKind.Absolute, out var uri))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                owner = parts[0];
                name = parts[1];
                return true;
            }
        }

        return false;
    }

    private static string? NormalizeLicense(GitHubLicenseInfo? license)
    {
        var value = license?.SpdxId;
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "NOASSERTION", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        value = license?.Name;
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return null;
    }

    private static async Task<ContributorFetchResult> FetchContributorsAsync(string? contributorsUrl, bool isAuthenticated)
    {
        var url = NormalizeApiUrl(contributorsUrl);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ContributorFetchResult(Array.Empty<GithubContributorPreview>(), 0);
        }

        try
        {
            return await FetchContributorSnapshotAsync(url, isAuthenticated);
        }
        catch (RateLimitCooldownException)
        {
            return new ContributorFetchResult(Array.Empty<GithubContributorPreview>(), 0);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "GitHub contributors fetch failed for {Url}", url);
            return new ContributorFetchResult(Array.Empty<GithubContributorPreview>(), 0);
        }
    }

    private static async Task<ContributorFetchResult> FetchContributorSnapshotAsync(string url, bool isAuthenticated)
    {
        var previewCount = isAuthenticated
            ? Constants.Trending.MaxContributorPreviewCount
            : Constants.Trending.AnonymousContributorPreviewCount;

        using var response = await SendGitHubResponseAsync(url + $"?per_page={previewCount}&page=1");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var contributors = JsonSerializer.Deserialize<List<GitHubContributorResponse>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<GitHubContributorResponse>();

        var previewTasks = contributors
            .Where(contributor => !string.IsNullOrWhiteSpace(contributor.Login))
            .Take(previewCount)
            .Select(async contributor => new GithubContributorPreview(
                contributor.Login!,
                contributor.AvatarUrl,
                await TryLoadBitmapAsync(contributor.AvatarUrl)))
            .ToArray();

        var previews = await Task.WhenAll(previewTasks);
        var totalCount = previews.Length;
        if (response.Headers.TryGetValues("Link", out var linkValues))
        {
            var estimated = ParseLastPageNumber(string.Join(",", linkValues));
            if (estimated is > 0)
            {
                totalCount = Math.Max(totalCount, estimated.Value * previewCount);
            }
        }

        return new ContributorFetchResult(previews, totalCount);
    }

    private static int? ParseLastPageNumber(string linkHeader)
    {
        foreach (var part in linkHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!part.Contains("rel=\"last\"", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var pageIndex = part.IndexOf("page=", StringComparison.OrdinalIgnoreCase);
            if (pageIndex < 0)
            {
                continue;
            }

            pageIndex += 5;
            var endIndex = part.IndexOf('&', pageIndex);
            var pageToken = endIndex >= 0 ? part[pageIndex..endIndex] : part[pageIndex..];
            if (int.TryParse(pageToken, out var pageNumber))
            {
                return pageNumber;
            }
        }

        return null;
    }

    private static async Task<string> SendGitHubJsonAsync(string url)
    {
        using var response = await SendGitHubResponseAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static async Task<HttpResponseMessage> SendGitHubResponseAsync(string url)
    {
        if (TryGetRateLimitCooldownRemaining(out var cooldownRemaining))
        {
            throw new RateLimitCooldownException(cooldownRemaining);
        }

        for (var attempt = 0; attempt <= Constants.RateLimit.MaxRetries; attempt++)
        {
            using var request = await CreateGitHubRequestAsync(url);
            var response = await Http.SendAsync(request);
            if (!IsRetriableRateLimit(response))
            {
                return response;
            }

            RegisterRateLimitCooldown(response);

            if (attempt >= Constants.RateLimit.MaxRetries)
            {
                return response;
            }

            var delay = ComputeRetryDelay(response, attempt);
            Log.Warning("GitHub rate-limit reached on {Url}. Retry {Attempt}/{Max} in {DelayMs}ms",
                url,
                attempt + 1,
                Constants.RateLimit.MaxRetries,
                (int)delay.TotalMilliseconds);
            response.Dispose();
            await Task.Delay(delay);
        }

        throw new InvalidOperationException("Unreachable retry state for GitHub API call.");
    }

    private static async Task<HttpRequestMessage> CreateGitHubRequestAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
        request.Headers.Accept.ParseAdd(Constants.GitHub.ApiAccept);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", Constants.GitHub.ApiVersion);

        var token = await AuthService.GetAccessTokenAsync(refreshIfNeeded: true);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private static bool IsRetriableRateLimit(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return true;
        }

        return response.StatusCode == HttpStatusCode.Forbidden
               && (HasRateLimitRemainingZero(response)
                   || response.Headers.RetryAfter is not null);
    }

    private static bool HasRateLimitRemainingZero(HttpResponseMessage response)
        => response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
           && string.Equals(remaining.FirstOrDefault(), "0", StringComparison.OrdinalIgnoreCase);

    private static TimeSpan ComputeRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
        {
            return response.Headers.RetryAfter!.Delta!.Value + TimeSpan.FromMilliseconds(Random.Shared.Next(
                Constants.RateLimit.RetryJitterMinMilliseconds,
                Constants.RateLimit.RetryJitterMaxMilliseconds));
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var unixSeconds))
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var delay = resetTime - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Constants.RateLimit.ResetSafetySeconds);
            if (delay > TimeSpan.Zero)
            {
                return delay + TimeSpan.FromMilliseconds(Random.Shared.Next(
                    Constants.RateLimit.RetryJitterMinMilliseconds,
                    Constants.RateLimit.RetryJitterMaxMilliseconds));
            }
        }

        var exponential = Constants.RateLimit.BaseBackoffMilliseconds * Math.Pow(2, attempt);
        var bounded = Math.Min(exponential, Constants.RateLimit.MaxBackoffMilliseconds);
        return TimeSpan.FromMilliseconds(bounded + Random.Shared.Next(50, 200));
    }

    private static void RegisterRateLimitCooldown(HttpResponseMessage response)
    {
        var now = DateTimeOffset.UtcNow;
        var cooldown = ComputeRateLimitCooldown(response);
        if (cooldown <= TimeSpan.Zero)
        {
            cooldown = TimeSpan.FromSeconds(Constants.RateLimit.CooldownFallbackSeconds);
        }

        lock (RateLimitStateSync)
        {
            var candidate = now + cooldown;
            if (_rateLimitCooldownUntilUtc is null || candidate > _rateLimitCooldownUntilUtc.Value)
            {
                _rateLimitCooldownUntilUtc = candidate;
            }
        }
    }

    private static TimeSpan ComputeRateLimitCooldown(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
        {
            return response.Headers.RetryAfter!.Delta!.Value;
        }

        if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var unixSeconds))
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return resetTime - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Constants.RateLimit.ResetSafetySeconds);
        }

        return TimeSpan.Zero;
    }

    private static bool TryGetRateLimitCooldownRemaining(out TimeSpan remaining)
    {
        lock (RateLimitStateSync)
        {
            if (_rateLimitCooldownUntilUtc is null)
            {
                remaining = TimeSpan.Zero;
                return false;
            }

            remaining = _rateLimitCooldownUntilUtc.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                _rateLimitCooldownUntilUtc = null;
                remaining = TimeSpan.Zero;
                return false;
            }

            return true;
        }
    }

    private static async Task<bool> HasAuthenticatedGitHubAccessAsync()
    {
        await AnonymousContributorLimiter.WaitAsync();
        try
        {
            var token = await AuthService.GetAccessTokenAsync(refreshIfNeeded: true);
            return !string.IsNullOrWhiteSpace(token);
        }
        catch
        {
            return false;
        }
        finally
        {
            AnonymousContributorLimiter.Release();
        }
    }

    private static string? NormalizeApiUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var sanitized = url.Split('{', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return sanitized.TrimEnd('/');
    }

    private static async Task<Bitmap?> TryLoadBitmapAsync(string? url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            var imageBytes = await TryLoadImageBytesFromDiskAsync(url, allowExpired: false);
            if (imageBytes is not null)
            {
                return CreateBitmapFromBytes(imageBytes);
            }

            imageBytes = await Http.GetByteArrayAsync(url);
            await TrySaveImageBytesToDiskAsync(url, imageBytes);
            return CreateBitmapFromBytes(imageBytes);
        }
        catch (Exception ex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(url))
                {
                    var staleBytes = await TryLoadImageBytesFromDiskAsync(url, allowExpired: true);
                    if (staleBytes is not null)
                    {
                        return CreateBitmapFromBytes(staleBytes);
                    }
                }
            }
            catch
            {
                // ignore stale image fallback failures
            }

            Log.Debug(ex, "Banner load failed for {Url}", url);
            return null;
        }
    }

    private static Bitmap CreateBitmapFromBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return new Bitmap(stream);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
        return client;
    }

    private static async Task<RepositoryDetails?> TryLoadRepositoryDetailsFromDiskAsync(
        string owner,
        string name,
        bool allowExpired,
        string fallbackHtmlUrl,
        string fallbackBannerUrl)
    {
        var path = GetRepositoryDetailsCachePath(owner, name);
        if (!File.Exists(path))
        {
            return null;
        }

        string json;
        await DiskCacheLock.WaitAsync();
        try
        {
            json = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to read repository-details cache for {Owner}/{Name}", owner, name);
            return null;
        }
        finally
        {
            DiskCacheLock.Release();
        }

        var cached = JsonSerializer.Deserialize<RepositoryDetailsCacheRecord>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (cached is null)
        {
            return null;
        }

        var age = DateTimeOffset.UtcNow - cached.CachedAtUtc;
        if (!allowExpired && age > Constants.Trending.RepositoryDetailsCacheTtl)
        {
            return null;
        }

        var bannerUrl = string.IsNullOrWhiteSpace(cached.BannerUrl) ? fallbackBannerUrl : cached.BannerUrl;
        var contributors = new List<GithubContributorPreview>();
        foreach (var contributor in cached.Contributors)
        {
            if (string.IsNullOrWhiteSpace(contributor.Login))
            {
                continue;
            }

            contributors.Add(new GithubContributorPreview(
                contributor.Login,
                contributor.AvatarUrl,
                await TryLoadBitmapAsync(contributor.AvatarUrl)));
        }

        return new RepositoryDetails
        {
            HtmlUrl = string.IsNullOrWhiteSpace(cached.HtmlUrl) ? fallbackHtmlUrl : cached.HtmlUrl,
            BannerUrl = bannerUrl,
            BannerImage = await TryLoadBitmapAsync(bannerUrl),
            License = cached.License,
            Contributors = contributors,
            ContributorsTotalCount = cached.ContributorsTotalCount,
            Topics = cached.Topics.Where(topic => !string.IsNullOrWhiteSpace(topic)).ToList(),
            UpdatedAt = cached.UpdatedAt
        };
    }

    private static async Task TrySaveRepositoryDetailsToDiskAsync(string owner, string name, RepositoryDetails details)
    {
        var path = GetRepositoryDetailsCachePath(owner, name);
        var payload = new RepositoryDetailsCacheRecord
        {
            HtmlUrl = details.HtmlUrl,
            BannerUrl = details.BannerUrl,
            License = details.License,
            ContributorsTotalCount = details.ContributorsTotalCount,
            Topics = details.Topics.ToList(),
            UpdatedAt = details.UpdatedAt,
            CachedAtUtc = DateTimeOffset.UtcNow,
            Contributors = details.Contributors
                .Where(contributor => !string.IsNullOrWhiteSpace(contributor.Login))
                .Select(contributor => new ContributorCacheRecord
                {
                    Login = contributor.Login,
                    AvatarUrl = contributor.AvatarUrl
                })
                .ToList()
        };

        await DiskCacheLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(payload);
            await File.WriteAllTextAsync(path, json);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to write repository-details cache for {Owner}/{Name}", owner, name);
        }
        finally
        {
            DiskCacheLock.Release();
        }
    }

    private static async Task<byte[]?> TryLoadImageBytesFromDiskAsync(string url, bool allowExpired)
    {
        var path = GetImageCachePath(url);
        if (!File.Exists(path))
        {
            return null;
        }

        var age = DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(path);
        if (!allowExpired && age > Constants.Trending.ImageCacheTtl)
        {
            return null;
        }

        await DiskCacheLock.WaitAsync();
        try
        {
            return await File.ReadAllBytesAsync(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to read image cache {Path}", path);
            return null;
        }
        finally
        {
            DiskCacheLock.Release();
        }
    }

    private static async Task TrySaveImageBytesToDiskAsync(string url, byte[] bytes)
    {
        var path = GetImageCachePath(url);
        await DiskCacheLock.WaitAsync();
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Unable to write image cache {Path}", path);
        }
        finally
        {
            DiskCacheLock.Release();
        }
    }

    private static string CreateCacheDirectory(string suffix)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend",
            suffix);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string GetRepositoryDetailsCachePath(string owner, string name)
    {
        var key = $"{owner}/{name}".ToLowerInvariant();
        return Path.Combine(RepositoryDetailsCacheDirectory, HashToFileName(key, "json"));
    }

    private static string GetImageCachePath(string url)
        => Path.Combine(ImageCacheDirectory, HashToFileName(url, "bin"));

    private static string HashToFileName(string value, string extension)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hash}.{extension}";
    }

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

    private sealed record GitHubRepositoryResponse
    {
        [JsonPropertyName("full_name")]
        public string? FullName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("contributors_url")]
        public string? ContributorsUrl { get; init; }

        [JsonPropertyName("topics")]
        public List<string>? Topics { get; init; }

        [JsonPropertyName("updated_at")]
        public string? UpdatedAt { get; init; }

        [JsonPropertyName("license")]
        public GitHubLicenseInfo? License { get; init; }
    }

    private sealed class GitHubLicenseInfo
    {
        [JsonPropertyName("spdx_id")]
        public string? SpdxId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private sealed record GitHubContributorResponse
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; init; }
    }

    private sealed record ContributorFetchResult(IReadOnlyList<GithubContributorPreview> Contributors, int TotalCount);

    private sealed class RateLimitCooldownException : Exception
    {
        public RateLimitCooldownException(TimeSpan remaining)
            : base($"GitHub rate-limit cooldown active for {Math.Max(1, (int)remaining.TotalSeconds)}s")
        {
        }
    }
}
