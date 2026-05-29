using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace Github_Trend;

public sealed class GitHubApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly GitHubAuthenticationService _authService;

    private readonly HttpClient _httpClient;
    private readonly GitHubAuthOptions _options;
    private readonly Random _random = new();

    public GitHubApiClient(
        GitHubAuthenticationService authService,
        GitHubAuthOptions? options = null,
        HttpClient? httpClient = null
    )
    {
        _authService = authService;
        _options = options ?? new GitHubAuthOptions();
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<GitHubUserProfile?> GetAuthenticatedUserAsync()
    {
        return await SendJsonAsync<GitHubUserProfile>(() =>
            new HttpRequestMessage(HttpMethod.Get, $"{Constants.GitHub.ApiBaseUrl}/user")
        );
    }

    public async Task<T?> SendJsonAsync<T>(Func<HttpRequestMessage> requestFactory)
    {
        using var response = await SendAsync(requestFactory);
        if (!response.IsSuccessStatusCode)
            return default;

        var payload = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(payload, JsonOptions);
    }

    public async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory)
    {
        var personalAccessToken = _options.PersonalAccessToken;
        var usePat = !string.IsNullOrWhiteSpace(personalAccessToken);

        for (var attempt = 0; attempt <= Constants.RateLimit.MaxRetries; attempt++)
        {
            var token = usePat ? personalAccessToken : await _authService.GetAccessTokenAsync(true);

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("User is not authenticated with GitHub.");

            using var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(Constants.GitHub.ApiAccept)
            );
            request.Headers.UserAgent.ParseAdd(_options.UserAgent);
            request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", _options.ApiVersion);

            var response = await _httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                await _authService.RefreshCurrentAsync();
                continue;
            }

            if (IsRateLimited(response))
            {
                if (attempt >= Constants.RateLimit.MaxRetries)
                    return response;

                var delay = GetRetryDelay(response, attempt);
                Log.Warning(
                    "GitHub rate-limit hit. Retry attempt {Attempt} in {DelayMs}ms",
                    attempt + 1,
                    (int)delay.TotalMilliseconds
                );
                response.Dispose();
                await Task.Delay(delay);
                continue;
            }

            return response;
        }

        throw new InvalidOperationException("GitHub request failed after retry.");
    }

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is not null)
            return response.Headers.RetryAfter!.Delta!.Value
                + TimeSpan.FromMilliseconds(
                    _random.Next(
                        Constants.RateLimit.RetryJitterMinMilliseconds,
                        Constants.RateLimit.RetryJitterMaxMilliseconds
                    )
                );

        if (
            response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
            && long.TryParse(resetValues.FirstOrDefault(), out var unixSeconds)
        )
        {
            var resetTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            var delay =
                resetTime
                - DateTimeOffset.UtcNow
                + TimeSpan.FromSeconds(Constants.RateLimit.ResetSafetySeconds);
            if (delay > TimeSpan.Zero)
                return delay
                    + TimeSpan.FromMilliseconds(
                        _random.Next(
                            Constants.RateLimit.RetryJitterMinMilliseconds,
                            Constants.RateLimit.RetryJitterMaxMilliseconds
                        )
                    );
        }

        var exponential = Constants.RateLimit.BaseBackoffMilliseconds * Math.Pow(2, attempt);
        var bounded = Math.Min(exponential, Constants.RateLimit.MaxBackoffMilliseconds);
        return TimeSpan.FromMilliseconds(bounded + _random.Next(0, 250));
    }

    private static bool IsRateLimited(HttpResponseMessage response)
    {
        return response.StatusCode == HttpStatusCode.TooManyRequests
            || (
                response.StatusCode == HttpStatusCode.Forbidden
                && response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining)
                && string.Equals(
                    remaining.FirstOrDefault(),
                    "0",
                    StringComparison.OrdinalIgnoreCase
                )
            );
    }
}
