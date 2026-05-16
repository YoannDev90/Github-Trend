using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Github_Trend;

public static class GithubRepositoryDetailsService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly ConcurrentDictionary<string, Lazy<Task<RepositoryDetails>>> Cache = new(StringComparer.OrdinalIgnoreCase);

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
        var details = new RepositoryDetails
        {
            HtmlUrl = htmlUrl,
            BannerUrl = bannerUrl,
            BannerImage = await TryLoadBitmapAsync(bannerUrl)
        };

        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{name}";
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd("Github-Trend/1.0");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var response = await Http.SendAsync(request);
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

            var contributorResult = await FetchContributorsAsync(repo.ContributorsUrl);
            details.Contributors = contributorResult.Contributors.ToList();
            details.ContributorsTotalCount = contributorResult.TotalCount;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trending] github details fetch failed for {owner}/{name}: {ex.Message}");
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

    private static async Task<ContributorFetchResult> FetchContributorsAsync(string? contributorsUrl)
    {
        var url = NormalizeApiUrl(contributorsUrl);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ContributorFetchResult(Array.Empty<GithubContributorPreview>(), 0);
        }

        try
        {
            var previewsTask = FetchContributorPreviewsAsync(url);
            var totalTask = FetchContributorTotalCountAsync(url);

            await Task.WhenAll(previewsTask, totalTask);

            return new ContributorFetchResult(previewsTask.Result, totalTask.Result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trending] contributors fetch failed for {url}: {ex.Message}");
            return new ContributorFetchResult(Array.Empty<GithubContributorPreview>(), 0);
        }
    }

    private static async Task<IReadOnlyList<GithubContributorPreview>> FetchContributorPreviewsAsync(string url)
    {
        var json = await SendGitHubJsonAsync(url + "?per_page=15&page=1");
        var contributors = JsonSerializer.Deserialize<List<GitHubContributorResponse>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<GitHubContributorResponse>();

        var previewTasks = contributors
            .Where(contributor => !string.IsNullOrWhiteSpace(contributor.Login))
            .Take(15)
            .Select(async contributor => new GithubContributorPreview(
                contributor.Login!,
                contributor.AvatarUrl,
                await TryLoadBitmapAsync(contributor.AvatarUrl)))
            .ToArray();


        return await Task.WhenAll(previewTasks);
    }

    private static async Task<int> FetchContributorTotalCountAsync(string url)
    {
        using var response = await SendGitHubResponseAsync(url + "?per_page=1&page=1");
        var json = await response.Content.ReadAsStringAsync();
        var contributors = JsonSerializer.Deserialize<List<GitHubContributorResponse>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new List<GitHubContributorResponse>();

        if (!response.Headers.TryGetValues("Link", out var linkValues))
        {
            return contributors.Count;
        }

        var lastPage = ParseLastPageNumber(string.Join(",", linkValues));
        return lastPage ?? contributors.Count;
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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Github-Trend/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        return await Http.SendAsync(request);
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

            using var stream = await Http.GetStreamAsync(url);
            await using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            memory.Position = 0;
            return new Bitmap(memory);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Trending] banner load failed for {url}: {ex.Message}");
            return null;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Github-Trend/1.0");
        return client;
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
}
