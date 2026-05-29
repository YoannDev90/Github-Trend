using System.Text.Json.Serialization;

namespace Github_Trend;

public sealed class GitHubUserProfile
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name! : Login ?? "GitHub user";
}
