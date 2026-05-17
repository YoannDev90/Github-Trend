using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Github_Trend;

public sealed class GithubTrendingRepository
{
    private static readonly IBrush DefaultLanguageBrush = new SolidColorBrush(Color.Parse("#FF3B82F6"));

    [JsonConstructor]
    public GithubTrendingRepository(
        List<GithubTrendingAuthor>? builders,
        string? repository,
        string? name,
        string? description,
        string? language,
        string? stars,
        string? forks,
        string? increased,
        string? htmlUrl,
        string? bannerUrl,
        Bitmap? bannerImage,
        string? license,
        List<GithubContributorPreview>? contributors,
        int contributorsTotalCount,
        List<string>? topics,
        string? updatedAt)
    {
        Builders = builders;
        Repository = repository;
        Name = name;
        Description = description;
        Language = language;
        Stars = stars;
        Forks = forks;
        Increased = increased;
        HtmlUrl = htmlUrl;
        BannerUrl = bannerUrl;
        BannerImage = bannerImage;
        License = license;
        Contributors = contributors;
        ContributorsTotalCount = contributorsTotalCount;
        Topics = topics;
        UpdatedAt = updatedAt;
        LanguageBrush = DefaultLanguageBrush;
    }

    private GithubTrendingRepository(
        List<GithubTrendingAuthor>? builders,
        string? repository,
        string? name,
        string? description,
        string? language,
        string? stars,
        string? forks,
        string? increased,
        string? htmlUrl,
        string? bannerUrl,
        Bitmap? bannerImage,
        string? license,
        List<GithubContributorPreview>? contributors,
        int contributorsTotalCount,
        List<string>? topics,
        string? updatedAt,
        IBrush? languageBrush)
        : this(builders, repository, name, description, language, stars, forks, increased, htmlUrl, bannerUrl, bannerImage, license, contributors, contributorsTotalCount, topics, updatedAt)
    {
        LanguageBrush = languageBrush ?? DefaultLanguageBrush;
    }

    public List<GithubTrendingAuthor>? Builders { get; }
    public string? Repository { get; }
    public string? Name { get; }
    public string? Description { get; }
    public string? Language { get; }
    public string? Stars { get; }
    public string? Forks { get; }
    public string? Increased { get; }
    public string? HtmlUrl { get; }
    public string? BannerUrl { get; }
    public Bitmap? BannerImage { get; }
    public string? License { get; }
    public List<GithubContributorPreview>? Contributors { get; }
    public int ContributorsTotalCount { get; }
    public List<string>? Topics { get; }
    public string? UpdatedAt { get; }
    public IBrush LanguageBrush { get; }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(Name)
        ? Name!
        : !string.IsNullOrWhiteSpace(Repository)
            ? GetRepositorySlug(Repository)
            : string.Empty;

    public string RepositoryLink => !string.IsNullOrWhiteSpace(HtmlUrl)
        ? HtmlUrl!
        : !string.IsNullOrWhiteSpace(Repository)
            ? Repository!
            : string.Empty;

    public string LicenseBadgeText => string.IsNullOrWhiteSpace(License)
        ? "License inconnue"
        : License!;

    public int StarsCount => ParseCount(Stars);

    public int ForksCount => ParseCount(Forks);

    public string StarsLabel => StarsCount.ToString("N0", CultureInfo.CurrentCulture);

    public string ForksLabel => ForksCount.ToString("N0", CultureInfo.CurrentCulture);

    public string LanguageBadgeText => string.IsNullOrWhiteSpace(Language)
        ? "Unknown"
        : Language!;

    public bool HasContributors => Contributors is { Count: > 0 };

    public int DisplayedContributorsCount => Contributors?.Count ?? 0;

    public int RemainingContributorsCount => Math.Max(0, ContributorsTotalCount - DisplayedContributorsCount);

    public bool HasRemainingContributors => RemainingContributorsCount > 0;

    public string RemainingContributorsLabel => HasRemainingContributors
        ? $"+{RemainingContributorsCount}"
        : string.Empty;

    public bool HasTopics => Topics is { Count: > 0 };

    public string LastUpdatedBadgeText => FormatLastUpdatedBadge(UpdatedAt);

    public GithubTrendingRepository CloneWith(
        List<GithubTrendingAuthor>? builders = null,
        string? repository = null,
        string? name = null,
        string? description = null,
        string? language = null,
        string? stars = null,
        string? forks = null,
        string? increased = null,
        string? htmlUrl = null,
        string? bannerUrl = null,
        Bitmap? bannerImage = null,
        string? license = null,
        List<GithubContributorPreview>? contributors = null,
        int? contributorsTotalCount = null,
        List<string>? topics = null,
        string? updatedAt = null,
        IBrush? languageBrush = null)
        => new(
            builders ?? Builders,
            repository ?? Repository,
            name ?? Name,
            description ?? Description,
            language ?? Language,
            stars ?? Stars,
            forks ?? Forks,
            increased ?? Increased,
            htmlUrl ?? HtmlUrl,
            bannerUrl ?? BannerUrl,
            bannerImage ?? BannerImage,
            license ?? License,
            contributors ?? Contributors,
            contributorsTotalCount ?? ContributorsTotalCount,
            topics ?? Topics,
            updatedAt ?? UpdatedAt,
            languageBrush ?? LanguageBrush);

    private static string GetRepositorySlug(string repositoryUrl)
    {
        var trimmed = repositoryUrl.Trim();

        if (trimmed.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                return $"{parts[^2]}/{parts[^1]}";
            }
        }

        return trimmed;
    }

    private static int ParseCount(string? value)
        => int.TryParse(value?.Replace(",", string.Empty), out var parsed) ? parsed : 0;

    private static string FormatLastUpdatedBadge(string? updatedAt)
    {
        if (!DateTimeOffset.TryParse(updatedAt, out var updated))
        {
            return "maj: inconnue";
        }

        var delta = DateTimeOffset.UtcNow - updated.ToUniversalTime();
        if (delta.TotalMinutes < 60)
        {
            return delta.TotalMinutes < 1 ? "maj: à l'instant" : $"maj: il y a {Math.Max(1, (int)delta.TotalMinutes)} min";
        }

        if (delta.TotalHours < 24)
        {
            return delta.TotalHours < 2 ? "maj: il y a 1 h" : $"maj: il y a {Math.Max(1, (int)delta.TotalHours)} h";
        }

        if (delta.TotalDays < 7)
        {
            return delta.TotalDays < 2 ? "maj: hier" : $"maj: il y a {Math.Max(1, (int)delta.TotalDays)} j";
        }

        if (delta.TotalDays < 30)
        {
            return $"maj: il y a {(int)Math.Round(delta.TotalDays / 7d)} sem";
        }

        if (delta.TotalDays < 365)
        {
            return $"maj: il y a {(int)Math.Round(delta.TotalDays / 30d)} mois";
        }

        return $"maj: il y a {(int)Math.Round(delta.TotalDays / 365d)} an";
    }
}

