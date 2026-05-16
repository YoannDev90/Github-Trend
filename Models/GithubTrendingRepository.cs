using System.Collections.Generic;

namespace Github_Trend;

public sealed record GithubTrendingRepository
{
    public List<GithubTrendingAuthor>? Builders { get; init; }
    public string? Repository { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Language { get; init; }
    public string? Stars { get; init; }
    public string? Forks { get; init; }
    public string? Increased { get; init; }
}

