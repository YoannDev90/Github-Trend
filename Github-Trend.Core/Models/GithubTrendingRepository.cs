using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Github_Trend.Localization;
using Github_Trend.Utils;

namespace Github_Trend;

public sealed class GithubTrendingRepository : IDisposable, INotifyPropertyChanged
{
    private static readonly IBrush DefaultLanguageBrush = new SolidColorBrush(
        Color.Parse("#FF3B82F6")
    );

    private Bitmap? _bannerImage;

    [JsonIgnore]
    internal bool IsEnriched { get; set; }

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
        string? updatedAt
    )
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
        _bannerImage = bannerImage;
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
        IBrush? languageBrush
    )
        : this(
            builders,
            repository,
            name,
            description,
            language,
            stars,
            forks,
            increased,
            htmlUrl,
            bannerUrl,
            bannerImage,
            license,
            contributors,
            contributorsTotalCount,
            topics,
            updatedAt
        )
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
    public string? License { get; }
    public List<GithubContributorPreview>? Contributors { get; }
    public int ContributorsTotalCount { get; }
    public List<string>? Topics { get; }
    public string? UpdatedAt { get; }
    public IBrush LanguageBrush { get; }
    public bool IsStarred { get; set; }
    public bool IsWatched { get; set; }
    public bool IsDismissed { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public Bitmap? BannerImage
    {
        get => _bannerImage;
        set
        {
            if (ReferenceEquals(_bannerImage, value)) return;
                    var old = _bannerImage;
            _bannerImage = value;
            OnPropertyChanged();
            old?.Dispose();
        }
    }

    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Name) ? Name!
        : !string.IsNullOrWhiteSpace(Repository) ? GetRepositorySlug(Repository)
        : string.Empty;

    public string RepositoryLink =>
        !string.IsNullOrWhiteSpace(HtmlUrl) ? HtmlUrl!
        : !string.IsNullOrWhiteSpace(Repository) ? Repository!
        : string.Empty;

    public string LicenseBadgeText =>
        string.IsNullOrWhiteSpace(License)
            ? Localization.Localization.Instance.GetString(
                nameof(LocalizationService.UnknownLicense)
            )
            : License!;

    public int StarsCount => ParseCount(Stars);

    public int ForksCount => ParseCount(Forks);

    public string StarsLabel => StarsCount.ToString("N0", CultureInfo.CurrentCulture);

    public string ForksLabel => ForksCount.ToString("N0", CultureInfo.CurrentCulture);

    public string LanguageBadgeText =>
        string.IsNullOrWhiteSpace(Language)
            ? Localization.Localization.Instance.GetString(
                nameof(LocalizationService.UnknownLanguage)
            )
            : Language!;

    public bool HasContributors => Contributors is { Count: > 0 };

    public int DisplayedContributorsCount => Contributors?.Count ?? 0;

    public int RemainingContributorsCount =>
        Math.Max(0, ContributorsTotalCount - DisplayedContributorsCount);

    public bool HasRemainingContributors => RemainingContributorsCount > 0;

    public string RemainingContributorsLabel =>
        HasRemainingContributors ? $"+{RemainingContributorsCount}" : string.Empty;

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
        IBrush? languageBrush = null
    )
    {
        return new GithubTrendingRepository(
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
            languageBrush ?? LanguageBrush
        );
    }

    private static string GetRepositorySlug(string repositoryUrl) =>
        RepositoryUrlParser.GetSlug(repositoryUrl);

    public static int ParseCount(string? value)
    {
        return int.TryParse(value?.Replace(",", string.Empty), out var parsed) ? parsed : 0;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose()
    {
        BannerImage?.Dispose();
        if (Contributors is not null)
        {
            foreach (var c in Contributors)
                c.Dispose();
        }
    }

    private static string FormatLastUpdatedBadge(string? updatedAt) =>
        TimeFormattingHelper.FormatLastUpdatedBadge(updatedAt);
}
