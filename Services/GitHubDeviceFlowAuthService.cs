using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Github_Trend;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend.Services;

public sealed class GitHubDeviceFlowAuthService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly GitHubAuthOptions _options;

    public GitHubDeviceFlowAuthService(string clientId, GitHubAuthOptions options)
    {
        _clientId = clientId;
        _options = options;
        _httpClient = HttpClientFactory.Create();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<DeviceCodeResponse> RequestDeviceCodeAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
        {
            Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("scope", _options.Scope)
            })
        };

        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("User-Agent", _options.UserAgent);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var deviceCode = JsonSerializer.Deserialize<DeviceCodeResponse>(content);
        return deviceCode ?? throw new InvalidOperationException("Failed to parse device code response");
    }

    public async Task<(bool Success, AccessTokenResponse? Token, string? Error)> PollForTokenAsync(
        string deviceCode,
        int intervalSeconds,
        int timeoutSeconds
    )
    {
        var startTime = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        while (DateTime.UtcNow - startTime < timeout)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
                {
                    Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string, string>("client_id", _clientId),
                        new KeyValuePair<string, string>("device_code", deviceCode),
                        new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                    })
                };

                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", _options.UserAgent);

                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();

                var tokenResponse = JsonSerializer.Deserialize<AccessTokenResponse>(content);

                if (tokenResponse?.Error == "authorization_pending")
                {
                    await Task.Delay(intervalSeconds * 1000);
                    continue;
                }

                if (tokenResponse?.Error == "slow_down")
                {
                    intervalSeconds += 5;
                    await Task.Delay(intervalSeconds * 1000);
                    continue;
                }

                if (tokenResponse?.Error != null)
                    return (false, null, tokenResponse.Error);

                if (string.IsNullOrWhiteSpace(tokenResponse?.AccessToken))
                    return (false, null, "No access token in response");

                return (true, tokenResponse, null);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Device flow poll failed");
                return (false, null, ex.Message);
            }
        }

        return (false, null, "Device code verification timed out");
    }

    public sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")] public string? DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public string? UserCode { get; set; }
        [JsonPropertyName("verification_uri")] public string? VerificationUri { get; set; }
        [JsonPropertyName("verification_uri_complete")] public string? VerificationUriComplete { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    public sealed class AccessTokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
        [JsonPropertyName("error_uri")] public string? ErrorUri { get; set; }
    }
}
