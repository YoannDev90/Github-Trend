using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Github_Trend;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GitHubApiClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly GitHubAuthenticationService _authService;
    private readonly HttpClient _httpClient;
    private readonly GitHubAuthOptions _options;

    public GitHubApiClient(
        GitHubAuthenticationService authService,
        GitHubAuthOptions? options = null,
        HttpClient? httpClient = null
    )
    {
        _authService = authService;
        _options = options ?? new GitHubAuthOptions();
        _httpClient = httpClient ?? HttpClientFactory.Create();
    }

    public async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory)
    {
        for (var attempt = 0; attempt <= Constants.RateLimit.MaxRetries; attempt++)
        {
            var token = await _authService.GetAccessTokenAsync(true);
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("User is not authenticated with GitHub.");

            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(Constants.GitHub.ApiAccept)
            );
            request.Headers.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", Constants.GitHub.ApiVersion);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                await _authService.RefreshCurrentAsync();
                continue;
            }

            if (RetryHelper.IsRetriableRateLimit(response))
            {
                if (attempt >= Constants.RateLimit.MaxRetries)
                    return response;

                var delay = RetryHelper.ComputeDelay(response, attempt);
                Log.Warning("GitHub rate-limit. Retry {Attempt} in {Delay}ms", attempt + 1, (int)delay.TotalMilliseconds);
                response.Dispose();
                await Task.Delay(delay);
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("GitHub request failed after retries.");
    }

    public async Task<HashSet<string>> GetWatchedRepositorySlugsAsync()
    {
        return await FetchSlugsAsync($"{Constants.GitHub.ApiBaseUrl}/user/subscriptions");
    }

    private async Task<HashSet<string>> FetchSlugsAsync(string baseUrl)
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var page = 1;

        while (true)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await SendAsync(() =>
                    new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}?per_page=100&page={page}")
                );

                if (!response.IsSuccessStatusCode) break;

                var json = await response.Content.ReadAsStringAsync();
                var repos = JsonSerializer.Deserialize<List<GitHubRepoBriefResponse>>(json, JsonOptions);

                if (repos is null || repos.Count == 0) break;

                foreach (var repo in repos)
                {
                    if (!string.IsNullOrWhiteSpace(repo.FullName))
                        slugs.Add(repo.FullName);
                }

                if (repos.Count < 100) break;
                page++;
            }
            finally
            {
                response?.Dispose();
            }
        }

        return slugs;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed record GitHubRepoBriefResponse
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }
}
