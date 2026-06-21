using System;

namespace Github_Trend;

public static class Constants
{
    public const string GitHubColorsUrl =
        "https://raw.githubusercontent.com/ozh/github-colors/master/colors.json";
    public const string GitHubTrendingUrl = "https://githubtrending.lessx.xyz/trending";

    public static class GitHub
    {
        public const string ApiBaseUrl = "https://api.github.com";
        public const string ApiAccept = "application/vnd.github+json";
        public const string ApiVersion = "2022-11-28";
        public const string UserAgent = "Github-Trend/1.0";
    }

    public static class GitHubApp
    {
        public const string ClientId = "Ov23liF9LELIduw9N0kH";
        public const string CallbackUrl = "http://localhost:25885/callback";
        public const string LocalBaseUrl = "http://localhost:25885";
        public const bool PrivateRepoAccessEnabled = false;
    }

    public static class Trending
    {
        public const int MaxParallelEnrichmentRequests = 2;
        public const int InterEnrichmentDelayMilliseconds = 300;
        public const int MaxContributorPreviewCount = 15;
        public const int AnonymousContributorPreviewCount = 5;
        public static readonly TimeSpan TrendingCacheTtl = TimeSpan.FromHours(24);
        public static readonly TimeSpan RepositoryDetailsCacheTtl = TimeSpan.FromHours(1);
        public static readonly TimeSpan ImageCacheTtl = TimeSpan.FromHours(1);
    }

    public static class RateLimit
    {
        public const int MaxRetries = 3;
        public const int BaseBackoffMilliseconds = 700;
        public const int MaxBackoffMilliseconds = 30_000;
        public const int RetryJitterMinMilliseconds = 120;
        public const int RetryJitterMaxMilliseconds = 850;
        public const int ResetSafetySeconds = 2;
        public const int CooldownFallbackSeconds = 15;
        public const int WarningThreshold = 20;
        public const int CriticalThreshold = 5;
    }

    public static class Logging
    {
        public const string LogDirectoryName = "logs";
        public const string LogFilePattern = "app-.log";
    }
}
