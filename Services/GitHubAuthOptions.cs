using System;

namespace Github_Trend;

public sealed class GitHubAuthOptions
{
    public string ClientId { get; init; } = Environment.GetEnvironmentVariable("GITHUB_APP_CLIENT_ID") ?? "Iv23liKpprNhWdrap9RQ";

    public string ClientSecret { get; init; } = Environment.GetEnvironmentVariable("GITHUB_APP_CLIENT_SECRET") ?? string.Empty;

    public string CallbackUrl { get; init; } = Environment.GetEnvironmentVariable("GITHUB_APP_CALLBACK_URL") ?? "http://localhost:25885/callback";

    public string LocalBaseUrl { get; init; } = Environment.GetEnvironmentVariable("GITHUB_APP_LOCAL_BASE_URL") ?? "http://localhost:25885";

    public string UserAgent { get; init; } = Constants.GitHub.UserAgent;

    public string ApiVersion { get; init; } = Constants.GitHub.ApiVersion;

    public bool PrivateRepoAccessEnabled { get; init; } = string.Equals(
        Environment.GetEnvironmentVariable("GITHUB_APP_PRIVATE_REPO_ACCESS"),
        "true",
        StringComparison.OrdinalIgnoreCase);

    public string Scope => PrivateRepoAccessEnabled ? "read:user user:email repo" : "read:user user:email";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Missing GITHUB_APP_CLIENT_ID environment variable.");
        }
    }
}

