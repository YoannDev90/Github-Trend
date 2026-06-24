using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Github_Trend.Services;

public static class AppConfig
{
    private static JsonDocument? _doc;
    private static readonly object Lock = new();

    public static void Load()
    {
        lock (Lock)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) path = "appsettings.json";
            if (!File.Exists(path)) return;
            try
            {
                var json = File.ReadAllText(path);
                _doc = JsonDocument.Parse(json);
            }
            catch { _doc = null; }
        }
    }

    private static string? GetString(string section, string key)
    {
        if (_doc == null) Load();
        if (_doc?.RootElement.TryGetProperty(section, out var sec) == true
            && sec.TryGetProperty(key, out var val)
            && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    private static int? GetInt(string section, string key)
    {
        if (_doc == null) Load();
        if (_doc?.RootElement.TryGetProperty(section, out var sec) == true
            && sec.TryGetProperty(key, out var val)
            && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return null;
    }

    private static double? GetDouble(string section, string key)
    {
        if (_doc == null) Load();
        if (_doc?.RootElement.TryGetProperty(section, out var sec) == true
            && sec.TryGetProperty(key, out var val)
            && val.ValueKind == JsonValueKind.Number)
            return val.GetDouble();
        return null;
    }

    private static bool? GetBool(string section, string key)
    {
        if (_doc == null) Load();
        if (_doc?.RootElement.TryGetProperty(section, out var sec) != true)
            return null;
        if (!sec.TryGetProperty(key, out var val))
            return null;
        if (val.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return val.GetBoolean();
        return null;
    }

    public static class GitHub
    {
        public static string ClientId => GetString("GitHub", "ClientId") ?? "";
        public static string CallbackUrl => GetString("GitHub", "CallbackUrl") ?? "http://localhost:25885/callback";
        public static string LocalBaseUrl => GetString("GitHub", "LocalBaseUrl") ?? "http://localhost:25885";
        public static bool PrivateRepoAccessEnabled => GetBool("GitHub", "PrivateRepoAccessEnabled") ?? false;
        public static string ApiBaseUrl => GetString("GitHub", "ApiBaseUrl") ?? "https://api.github.com";
        public static string ApiAccept => GetString("GitHub", "ApiAccept") ?? "application/vnd.github+json";
        public static string ApiVersion => GetString("GitHub", "ApiVersion") ?? "2022-11-28";
        public static string UserAgent => GetString("GitHub", "UserAgent") ?? "Github-Trend/1.0";
        public static string ColorsUrl => GetString("GitHub", "ColorsUrl") ?? "https://raw.githubusercontent.com/ozh/github-colors/master/colors.json";
        public static string TrendingUrl => GetString("GitHub", "TrendingUrl") ?? "https://githubtrending.lessx.xyz/trending";
        public static string GraphQlEndpoint => GetString("GitHub", "GraphQlEndpoint") ?? "https://api.github.com/graphql";
        public static string DeviceCodeEndpoint => GetString("GitHub", "DeviceCodeEndpoint") ?? "https://github.com/login/device/code";
        public static string AccessTokenEndpoint => GetString("GitHub", "AccessTokenEndpoint") ?? "https://github.com/login/oauth/access_token";
        public static string ContributorsEndpoint => GetString("GitHub", "ContributorsEndpoint") ?? "https://api.github.com/repos/{owner}/{name}/contributors";
        public static string OpenGraphBannerUrl => GetString("GitHub", "OpenGraphBannerUrl") ?? "https://opengraph.githubassets.com/1/{owner}/{name}";
        public static string ScopePublic => GetString("GitHub", "ScopePublic") ?? "read:user user:email public_repo notifications";
        public static string ScopePrivate => GetString("GitHub", "ScopePrivate") ?? "read:user user:email repo notifications";
    }

    public static class Trending
    {
        public static int MaxParallelEnrichmentRequests => GetInt("Trending", "MaxParallelEnrichmentRequests") ?? 2;
        public static int InterEnrichmentDelayMilliseconds => GetInt("Trending", "InterEnrichmentDelayMilliseconds") ?? 300;
        public static int MaxContributorPreviewCount => GetInt("Trending", "MaxContributorPreviewCount") ?? 15;
        public static int AnonymousContributorPreviewCount => GetInt("Trending", "AnonymousContributorPreviewCount") ?? 5;
        public static int TrendingCacheTtlHours => GetInt("Trending", "TrendingCacheTtlHours") ?? 24;
        public static int ColorsCacheTtlHours => GetInt("Trending", "ColorsCacheTtlHours") ?? 24;
        public static int RepositoryDetailsCacheTtlHours => GetInt("Trending", "RepositoryDetailsCacheTtlHours") ?? 1;
        public static int ImageCacheTtlHours => GetInt("Trending", "ImageCacheTtlHours") ?? 1;
        public static int StreamChannelCapacity => GetInt("Trending", "StreamChannelCapacity") ?? 20;

        public static TimeSpan TrendingCacheTtl => TimeSpan.FromHours(TrendingCacheTtlHours);
        public static TimeSpan RepositoryDetailsCacheTtl => TimeSpan.FromHours(RepositoryDetailsCacheTtlHours);
        public static TimeSpan ImageCacheTtl => TimeSpan.FromHours(ImageCacheTtlHours);
    }

    public static class RateLimit
    {
        public static int MaxRetries => GetInt("RateLimit", "MaxRetries") ?? 3;
        public static int BaseBackoffMilliseconds => GetInt("RateLimit", "BaseBackoffMilliseconds") ?? 700;
        public static int MaxBackoffMilliseconds => GetInt("RateLimit", "MaxBackoffMilliseconds") ?? 30000;
        public static int RetryJitterMinMilliseconds => GetInt("RateLimit", "RetryJitterMinMilliseconds") ?? 120;
        public static int RetryJitterMaxMilliseconds => GetInt("RateLimit", "RetryJitterMaxMilliseconds") ?? 850;
        public static int ResetSafetySeconds => GetInt("RateLimit", "ResetSafetySeconds") ?? 2;
        public static int CooldownFallbackSeconds => GetInt("RateLimit", "CooldownFallbackSeconds") ?? 15;
        public static int WarningThreshold => GetInt("RateLimit", "WarningThreshold") ?? 20;
        public static int CriticalThreshold => GetInt("RateLimit", "CriticalThreshold") ?? 5;
    }

    public static class Logging
    {
        public static string DirectoryName => GetString("Logging", "DirectoryName") ?? "logs";
        public static string FilePattern => GetString("Logging", "FilePattern") ?? "app-.log";
        public static int RetainedFileCount => GetInt("Logging", "RetainedFileCount") ?? 7;
        public static int MaxLogEntries => GetInt("Logging", "MaxLogEntries") ?? 500;
        public static string DefaultActiveLevels => GetString("Logging", "DefaultActiveLevels") ?? "DBG,INF,WRN,ERR";

        public static HashSet<string> DefaultActiveLevelSet
        {
            get
            {
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in DefaultActiveLevels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    set.Add(l);
                return set;
            }
        }
    }

    public static class Images
    {
        public static int BannerMaxWidth => GetInt("Images", "BannerMaxWidth") ?? 640;
        public static int AvatarMaxSize => GetInt("Images", "AvatarMaxSize") ?? 64;
    }

    public static class Auth
    {
        public static int TokenRefreshSkewMinutes => GetInt("Auth", "TokenRefreshSkewMinutes") ?? 5;
        public static int FallbackTokenExpiryHours => GetInt("Auth", "FallbackTokenExpiryHours") ?? 8;
    }

    public static class HttpClient
    {
        public static int PooledConnectionLifetimeMinutes => GetInt("HttpClient", "PooledConnectionLifetimeMinutes") ?? 5;
        public static int ConnectTimeoutSeconds => GetInt("HttpClient", "ConnectTimeoutSeconds") ?? 15;
        public static int RequestTimeoutSeconds => GetInt("HttpClient", "RequestTimeoutSeconds") ?? 30;
    }

    public static class AppData
    {
        public static string FolderName => GetString("AppData", "FolderName") ?? "Github_Trend";
        public static string DatabaseFileName => GetString("AppData", "DatabaseFileName") ?? "github-trend.db";
        public static string PreferencesFileName => GetString("AppData", "PreferencesFileName") ?? "user_preferences.json";
        public static string ThemePreferenceFileName => GetString("AppData", "ThemePreferenceFileName") ?? "theme_preference";
        public static string TokenKeyFileName => GetString("AppData", "TokenKeyFileName") ?? "github-token.key";
        public static string PopularLanguagesFileName => GetString("AppData", "PopularLanguagesFileName") ?? "popular-languages.json";
    }
}