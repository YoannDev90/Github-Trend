using System.Text.Json.Serialization;

namespace Github_Trend;

public sealed record GithubColorEntry
{
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

