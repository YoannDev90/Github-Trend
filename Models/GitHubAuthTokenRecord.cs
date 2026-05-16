using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Github_Trend;

public sealed class GitHubAuthTokenRecord
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("github_account_id")]
    public long GitHubAccountId { get; set; }

    [JsonPropertyName("access_token_encrypted")]
    public string AccessTokenEncrypted { get; set; } = string.Empty;

    [JsonPropertyName("refresh_token_encrypted")]
    public string? RefreshTokenEncrypted { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset ExpiresAt { get; set; }

    [JsonPropertyName("refresh_token_expires_at")]
    public DateTimeOffset? RefreshTokenExpiresAt { get; set; }

    [JsonPropertyName("scope_list")]
    public List<string> ScopeList { get; set; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("revoked_at")]
    public DateTimeOffset? RevokedAt { get; set; }

    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    public bool IsExpired(TimeSpan? skew = null)
    {
        var effectiveSkew = skew ?? TimeSpan.FromMinutes(5);
        return ExpiresAt <= DateTimeOffset.UtcNow.Add(effectiveSkew);
    }

    public bool IsRefreshTokenExpired(TimeSpan? skew = null)
    {
        if (RefreshTokenExpiresAt is null)
        {
            return false;
        }

        var effectiveSkew = skew ?? TimeSpan.FromMinutes(5);
        return RefreshTokenExpiresAt <= DateTimeOffset.UtcNow.Add(effectiveSkew);
    }
}

