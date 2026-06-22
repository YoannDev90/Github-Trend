using System.Collections.Generic;

namespace Github_Trend.Services;

public sealed class RepositoryEnrichmentData
{
    public string HtmlUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? License { get; set; }
    public List<string> Topics { get; set; } = new();
    public string? UpdatedAt { get; set; }
}

public sealed class ContributorData(string login, string? avatarUrl)
{
    public string Login { get; } = login;
    public string? AvatarUrl { get; } = avatarUrl;
}

public sealed class ContributorFetchData(IReadOnlyList<ContributorData> contributors, int totalCount)
{
    public IReadOnlyList<ContributorData> Contributors { get; } = contributors;
    public int TotalCount { get; } = totalCount;
}
