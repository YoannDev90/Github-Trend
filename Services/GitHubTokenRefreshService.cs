using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class GitHubTokenRefreshService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly GitHubAuthOptions _options;

    public GitHubTokenRefreshService(GitHubAuthOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        _httpClient = httpClient ?? CreateHttpClient();
    }

    public async Task<GitHubTokenExchangeResult> ExchangeCodeAsync(string code, string state)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["state"] = state,
            ["redirect_uri"] = _options.CallbackUrl
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub token exchange failed: {(int)response.StatusCode} {response.ReasonPhrase} - {json}");

        var token = JsonSerializer.Deserialize<GitHubTokenResponse>(json, JsonOptions)
                    ?? throw new InvalidOperationException("GitHub token response was empty.");

        return BuildResult(token);
    }

    public async Task<GitHubTokenExchangeResult> RefreshAsync(string refreshToken)
    {
        var body = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["redirect_uri"] = _options.CallbackUrl
        };

        using HttpRequestMessage request = new(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        {
            Content = new FormUrlEncodedContent(body)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);

        using var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub refresh failed: {(int)response.StatusCode} {response.ReasonPhrase} - {json}");

        var token = JsonSerializer.Deserialize<GitHubTokenResponse>(json, JsonOptions)
                    ?? throw new InvalidOperationException("GitHub refresh response was empty.");

        return BuildResult(token);
    }

    public async Task<GitHubUserProfile> FetchUserProfileAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Constants.GitHub.ApiBaseUrl}/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(Constants.GitHub.ApiAccept));
        request.Headers.UserAgent.ParseAdd(_options.UserAgent);
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", (string?)_options.ApiVersion);

        using var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"GitHub user profile fetch failed: {(int)response.StatusCode} {response.ReasonPhrase} - {json}");

        return JsonSerializer.Deserialize<GitHubUserProfile>(json, JsonOptions)
               ?? throw new InvalidOperationException("GitHub user profile response was empty.");
    }

    private static GitHubTokenExchangeResult BuildResult(GitHubTokenResponse token)
    {
        var accessToken = token.AccessToken ??
                          throw new InvalidOperationException("GitHub token response missing access_token.");
        var scopeList = ParseScopes(token.Scope);
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn ?? 0));
        DateTimeOffset? refreshExpiresAt = null;
        if (token.RefreshTokenExpiresIn is > 0)
            refreshExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.RefreshTokenExpiresIn.Value);

        return new GitHubTokenExchangeResult(
            accessToken,
            token.RefreshToken,
            scopeList,
            expiresAt,
            refreshExpiresAt);
    }

    private static IReadOnlyList<string> ParseScopes(string? scope)
    {
        return string.IsNullOrWhiteSpace(scope)
            ? Array.Empty<string>()
            : scope.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Constants.GitHub.UserAgent);
        return client;
    }

    private sealed record GitHubTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; init; }

        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")] public int? ExpiresIn { get; init; }

        [JsonPropertyName("refresh_token_expires_in")]
        public int? RefreshTokenExpiresIn { get; init; }

        [JsonPropertyName("scope")] public string? Scope { get; init; }

        [JsonPropertyName("token_type")] public string? TokenType { get; init; }
    }
}

public sealed class GitHubTokenExchangeResult
{
    public GitHubTokenExchangeResult(
        string accessToken,
        string? refreshToken,
        IReadOnlyList<string> scopeList,
        DateTimeOffset expiresAt,
        DateTimeOffset? refreshTokenExpiresAt)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ScopeList = scopeList;
        ExpiresAt = expiresAt;
        RefreshTokenExpiresAt = refreshTokenExpiresAt;
    }

    public string AccessToken { get; }

    public string? RefreshToken { get; }

    public IReadOnlyList<string> ScopeList { get; }

    public DateTimeOffset ExpiresAt { get; }

    public DateTimeOffset? RefreshTokenExpiresAt { get; }
}