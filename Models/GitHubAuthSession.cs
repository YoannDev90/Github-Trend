using System;
using System.Collections.Generic;

namespace Github_Trend;

public sealed record GitHubAuthSession(
    string LocalUserId,
    long GitHubAccountId,
    string Login,
    string? Name,
    string? Email,
    string? AvatarUrl,
    IReadOnlyList<string> ScopeList,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RefreshTokenExpiresAt)
{
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! : Login;

    public string Summary => $"{DisplayName} (@{Login})";
}