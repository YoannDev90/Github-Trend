using System;
using System.IO;
using System.Text.Json;
using Github_Trend;

namespace Github_Trend.Services;

public sealed class GitHubAuthOptions
{
    private const string ConfigFileName = "appsettings.json";

    public string ClientId { get; init; } = string.Empty;
    public string CallbackUrl { get; init; } = string.Empty;
    public string LocalBaseUrl { get; init; } = string.Empty;
    public string UserAgent { get; init; } = Constants.GitHub.UserAgent;
    public string ApiVersion { get; init; } = Constants.GitHub.ApiVersion;
    public bool PrivateRepoAccessEnabled { get; init; } = false;

    public string Scope =>
        PrivateRepoAccessEnabled
            ? "read:user user:email repo notifications"
            : "read:user user:email public_repo notifications";

    // GitHub API endpoints
    public string ApiBaseUrl { get; init; } = Constants.GitHub.ApiBaseUrl;
    public string ApiAccept { get; init; } = Constants.GitHub.ApiAccept;
    public string ColorsUrl { get; init; } = Constants.GitHubColorsUrl;
    public string TrendingUrl { get; init; } = Constants.GitHubTrendingUrl;

    // Trending configuration
    public int MaxParallelEnrichmentRequests { get; init; } = 2;
    public int InterEnrichmentDelayMilliseconds { get; init; } = 300;
    public int MaxContributorPreviewCount { get; init; } = 15;
    public int AnonymousContributorPreviewCount { get; init; } = 5;
    public double TrendingCacheTtlHours { get; init; } = 24;
    public double RepositoryDetailsCacheTtlHours { get; init; } = 1;
    public double ImageCacheTtlHours { get; init; } = 1;

    // Rate limiting
    public int MaxRetries { get; init; } = 3;
    public int BaseBackoffMilliseconds { get; init; } = 700;
    public int MaxBackoffMilliseconds { get; init; } = 30_000;
    public int RetryJitterMinMilliseconds { get; init; } = 120;
    public int RetryJitterMaxMilliseconds { get; init; } = 850;
    public int ResetSafetySeconds { get; init; } = 2;
    public int CooldownFallbackSeconds { get; init; } = 15;
    public int WarningThreshold { get; init; } = 20;
    public int CriticalThreshold { get; init; } = 5;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("Missing GitHub App client id. Set it in appsettings.json or via GitHubAuthOptions.ClientId.");
    }

    public static GitHubAuthOptions LoadFromConfig()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, ConfigFileName);
        if (!File.Exists(configPath))
            configPath = ConfigFileName;

        try
        {
            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new GitHubAuthOptions
            {
                ClientId = GetString(root, "GitHub", "ClientId") ?? "",
                CallbackUrl = GetString(root, "GitHub", "CallbackUrl") ?? "",
                LocalBaseUrl = GetString(root, "GitHub", "LocalBaseUrl") ?? "",
                PrivateRepoAccessEnabled = GetBool(root, "GitHub", "PrivateRepoAccessEnabled") ?? false,
                ApiBaseUrl = GetString(root, "GitHub", "ApiBaseUrl") ?? Constants.GitHub.ApiBaseUrl,
                ApiAccept = GetString(root, "GitHub", "ApiAccept") ?? Constants.GitHub.ApiAccept,
                ApiVersion = GetString(root, "GitHub", "ApiVersion") ?? Constants.GitHub.ApiVersion,
                UserAgent = GetString(root, "GitHub", "UserAgent") ?? Constants.GitHub.UserAgent,
                ColorsUrl = GetString(root, "GitHub", "ColorsUrl") ?? Constants.GitHubColorsUrl,
                TrendingUrl = GetString(root, "GitHub", "TrendingUrl") ?? Constants.GitHubTrendingUrl,
                MaxParallelEnrichmentRequests = GetInt(root, "Trending", "MaxParallelEnrichmentRequests") ?? 2,
                InterEnrichmentDelayMilliseconds = GetInt(root, "Trending", "InterEnrichmentDelayMilliseconds") ?? 300,
                MaxContributorPreviewCount = GetInt(root, "Trending", "MaxContributorPreviewCount") ?? 15,
                AnonymousContributorPreviewCount = GetInt(root, "Trending", "AnonymousContributorPreviewCount") ?? 5,
                TrendingCacheTtlHours = GetDouble(root, "Trending", "TrendingCacheTtlHours") ?? 24,
                RepositoryDetailsCacheTtlHours = GetDouble(root, "Trending", "RepositoryDetailsCacheTtlHours") ?? 1,
                ImageCacheTtlHours = GetDouble(root, "Trending", "ImageCacheTtlHours") ?? 1,
                MaxRetries = GetInt(root, "RateLimit", "MaxRetries") ?? 3,
                BaseBackoffMilliseconds = GetInt(root, "RateLimit", "BaseBackoffMilliseconds") ?? 700,
                MaxBackoffMilliseconds = GetInt(root, "RateLimit", "MaxBackoffMilliseconds") ?? 30_000,
                RetryJitterMinMilliseconds = GetInt(root, "RateLimit", "RetryJitterMinMilliseconds") ?? 120,
                RetryJitterMaxMilliseconds = GetInt(root, "RateLimit", "RetryJitterMaxMilliseconds") ?? 850,
                ResetSafetySeconds = GetInt(root, "RateLimit", "ResetSafetySeconds") ?? 2,
                CooldownFallbackSeconds = GetInt(root, "RateLimit", "CooldownFallbackSeconds") ?? 15,
                WarningThreshold = GetInt(root, "RateLimit", "WarningThreshold") ?? 20,
                CriticalThreshold = GetInt(root, "RateLimit", "CriticalThreshold") ?? 5,
            };
        }
        catch
        {
            return new GitHubAuthOptions();
        }
    }

    private static string? GetString(JsonElement root, string section, string key)
    {
        if (root.TryGetProperty(section, out var sec) && sec.TryGetProperty(key, out var val))
            return val.GetString();
        return null;
    }

    private static bool? GetBool(JsonElement root, string section, string key)
    {
        if (root.TryGetProperty(section, out var sec) && sec.TryGetProperty(key, out var val))
            return val.GetBoolean();
        return null;
    }

    private static int? GetInt(JsonElement root, string section, string key)
    {
        if (root.TryGetProperty(section, out var sec) && sec.TryGetProperty(key, out var val))
            return val.GetInt32();
        return null;
    }

    private static double? GetDouble(JsonElement root, string section, string key)
    {
        if (root.TryGetProperty(section, out var sec) && sec.TryGetProperty(key, out var val))
            return val.GetDouble();
        return null;
    }
}
