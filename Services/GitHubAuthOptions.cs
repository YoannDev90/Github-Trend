using System;

namespace Github_Trend;

public sealed class GitHubAuthOptions
{
    public string ClientId { get; init; } = Constants.GitHubApp.ClientId;

    public string ClientSecret { get; init; } = Constants.GitHubApp.ClientSecret;

    public string PersonalAccessToken { get; init; } = Constants.GitHubApp.PersonalAccessToken;

    public string CallbackUrl { get; init; } = Constants.GitHubApp.CallbackUrl;

    public string LocalBaseUrl { get; init; } = Constants.GitHubApp.LocalBaseUrl;

    public string UserAgent { get; init; } = Constants.GitHub.UserAgent;

    public string ApiVersion { get; init; } = Constants.GitHub.ApiVersion;

    public bool PrivateRepoAccessEnabled { get; init; } = Constants.GitHubApp.PrivateRepoAccessEnabled;

    public string Scope => PrivateRepoAccessEnabled
        ? "read:user user:email repo notifications"
        : "read:user user:email public_repo notifications";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("Missing GitHub App client id in Constants.cs.");
        }
    }
}

