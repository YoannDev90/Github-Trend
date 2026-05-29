using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Serilog;

namespace Github_Trend.Localization;

public static class Localization
{
    public static LocalizationService Instance { get; } = new();
}

public sealed class LocalizationService
{
    private static readonly ILogger Logger = Log.ForContext<LocalizationService>();

    // ResourceManager for accessing compiled .resx files
    private static readonly ResourceManager ResourceManager = new(
        "Github-Trend.Localization.Resources",
        typeof(LocalizationService).Assembly
    );

    private static readonly IReadOnlyList<CultureInfo> SupportedCultures = new[]
    {
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("fr-FR"),
    };

    public CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    public string WindowTitle => GetString(nameof(WindowTitle));
    public string GitHubAccountSectionTitle => GetString(nameof(GitHubAccountSectionTitle));
    public string GitHubDeviceCodeTitle => GetString(nameof(GitHubDeviceCodeTitle));
    public string CopyCodeButtonText => GetString(nameof(CopyCodeButtonText));
    public string GitHubSignInButtonText => GetString(nameof(GitHubSignInButtonText));
    public string GitHubRefreshButtonText => GetString(nameof(GitHubRefreshButtonText));
    public string GitHubSignOutButtonText => GetString(nameof(GitHubSignOutButtonText));
    public string SearchTitle => GetString(nameof(SearchTitle));
    public string SearchPlaceholderText => GetString(nameof(SearchPlaceholderText));
    public string TrendingPeriodTitle => GetString(nameof(TrendingPeriodTitle));
    public string TrendingPeriodSubtitle => GetString(nameof(TrendingPeriodSubtitle));
    public string TimeRangeDaily => GetString(nameof(TimeRangeDaily));
    public string TimeRangeWeekly => GetString(nameof(TimeRangeWeekly));
    public string TimeRangeMonthly => GetString(nameof(TimeRangeMonthly));
    public string TimeRangeAll => GetString(nameof(TimeRangeAll));
    public string SelectedLanguagesTitle => GetString(nameof(SelectedLanguagesTitle));
    public string SuggestionsTitle => GetString(nameof(SuggestionsTitle));
    public string RefreshButtonText => GetString(nameof(RefreshButtonText));
    public string TrendingRepositoriesTitle => GetString(nameof(TrendingRepositoriesTitle));
    public string ContributorsTitle => GetString(nameof(ContributorsTitle));
    public string StatusLoadingColors => GetString(nameof(StatusLoadingColors));
    public string StatusColorsLoaded => GetString(nameof(StatusColorsLoaded));
    public string StatusColorsLoadError => GetString(nameof(StatusColorsLoadError));
    public string StatusColorsRefreshing => GetString(nameof(StatusColorsRefreshing));
    public string StatusColorsRefreshed => GetString(nameof(StatusColorsRefreshed));
    public string StatusColorsRefreshError => GetString(nameof(StatusColorsRefreshError));
    public string StatusSelectionSaved => GetString(nameof(StatusSelectionSaved));
    public string StatusSelectionSaveError => GetString(nameof(StatusSelectionSaveError));
    public string StatusGitHubSignInStarting => GetString(nameof(StatusGitHubSignInStarting));
    public string StatusGitHubSignInSuccess => GetString(nameof(StatusGitHubSignInSuccess));
    public string StatusGitHubSignInFailed => GetString(nameof(StatusGitHubSignInFailed));
    public string StatusGitHubRefreshStarting => GetString(nameof(StatusGitHubRefreshStarting));
    public string StatusGitHubRefreshSuccess => GetString(nameof(StatusGitHubRefreshSuccess));
    public string StatusGitHubRefreshFailed => GetString(nameof(StatusGitHubRefreshFailed));
    public string StatusGitHubSignedOut => GetString(nameof(StatusGitHubSignedOut));
    public string GitHubAuthNotConfigured => GetString(nameof(GitHubAuthNotConfigured));
    public string GitHubAuthNotConnected => GetString(nameof(GitHubAuthNotConnected));
    public string GitHubAuthConnected => GetString(nameof(GitHubAuthConnected));
    public string GitHubAuthNoAccountConnected => GetString(nameof(GitHubAuthNoAccountConnected));
    public string GitHubAuthNoAccountLinked => GetString(nameof(GitHubAuthNoAccountLinked));
    public string GitHubAuthLinkedAccount => GetString(nameof(GitHubAuthLinkedAccount));
    public string GitHubDeviceCodeCopied => GetString(nameof(GitHubDeviceCodeCopied));
    public string NoLanguagesFound => GetString(nameof(NoLanguagesFound));
    public string SuggestionCountOne => GetString(nameof(SuggestionCountOne));
    public string SuggestionCountMany => GetString(nameof(SuggestionCountMany));
    public string SelectionSummaryZero => GetString(nameof(SelectionSummaryZero));
    public string SelectionSummaryOne => GetString(nameof(SelectionSummaryOne));
    public string SelectionSummaryMany => GetString(nameof(SelectionSummaryMany));
    public string TrendingCountZero => GetString(nameof(TrendingCountZero));
    public string TrendingCountOne => GetString(nameof(TrendingCountOne));
    public string TrendingCountMany => GetString(nameof(TrendingCountMany));
    public string UnknownLicense => GetString(nameof(UnknownLicense));
    public string UnknownLanguage => GetString(nameof(UnknownLanguage));
    public string UpdatedUnknown => GetString(nameof(UpdatedUnknown));
    public string UpdatedJustNow => GetString(nameof(UpdatedJustNow));
    public string UpdatedMinutesAgoOne => GetString(nameof(UpdatedMinutesAgoOne));
    public string UpdatedMinutesAgoMany => GetString(nameof(UpdatedMinutesAgoMany));
    public string UpdatedHoursAgoOne => GetString(nameof(UpdatedHoursAgoOne));
    public string UpdatedHoursAgoMany => GetString(nameof(UpdatedHoursAgoMany));
    public string UpdatedYesterday => GetString(nameof(UpdatedYesterday));
    public string UpdatedDaysAgoOne => GetString(nameof(UpdatedDaysAgoOne));
    public string UpdatedDaysAgoMany => GetString(nameof(UpdatedDaysAgoMany));
    public string UpdatedWeeksAgo => GetString(nameof(UpdatedWeeksAgo));
    public string UpdatedMonthsAgo => GetString(nameof(UpdatedMonthsAgo));
    public string UpdatedYearsAgo => GetString(nameof(UpdatedYearsAgo));
    public string OpenRepositoryFailure => GetString(nameof(OpenRepositoryFailure));
    public string ConnectGitHubToStar => GetString(nameof(ConnectGitHubToStar));
    public string StarRepositorySuccess => GetString(nameof(StarRepositorySuccess));
    public string StarRepositoryFailure => GetString(nameof(StarRepositoryFailure));
    public string ConnectGitHubToWatch => GetString(nameof(ConnectGitHubToWatch));
    public string WatchRepositorySuccess => GetString(nameof(WatchRepositorySuccess));
    public string WatchRepositoryFailure => GetString(nameof(WatchRepositoryFailure));
    public string RepoActionBlockedByIntegration =>
        GetString(nameof(RepoActionBlockedByIntegration));

    public string RepoActionWatchRequiresNotificationsScope =>
        GetString(nameof(RepoActionWatchRequiresNotificationsScope));

    public string RepoActionFailedHttp => GetString(nameof(RepoActionFailedHttp));
    public string GitHubAuthDeviceCodePromptOpen =>
        GetString(nameof(GitHubAuthDeviceCodePromptOpen));
    public string GitHubAuthDeviceCodePromptManual =>
        GetString(nameof(GitHubAuthDeviceCodePromptManual));
    public string InvalidDeviceCodeResponse => GetString(nameof(InvalidDeviceCodeResponse));
    public string MissingVerificationUrl => GetString(nameof(MissingVerificationUrl));
    public string DeviceFlowAuthenticationFailed =>
        GetString(nameof(DeviceFlowAuthenticationFailed));
    public string GitHubClientIdNotConfigured => GetString(nameof(GitHubClientIdNotConfigured));
    public string ActionStar => GetString(nameof(ActionStar));
    public string ActionWatch => GetString(nameof(ActionWatch));

    public void Initialize(CultureInfo? culture = null)
    {
        var targetCulture = culture ?? CultureInfo.CurrentUICulture;
        Logger.Information("Initializing localization with culture: {Culture}", targetCulture.Name);
        ApplyCulture(targetCulture);
    }

    public string GetString(string key, params object?[] args)
    {
        var template = key;

        try
        {
            var resourceTemplate = ResourceManager.GetString(key, CurrentCulture);
            if (resourceTemplate is not null)
                template = resourceTemplate;
            else
                Logger.Warning(
                    "GetString({Key}) not found in resources (Culture: {Culture})",
                    key,
                    CurrentCulture.Name
                );
        }
        catch (Exception ex)
        {
            Logger.Warning(
                ex,
                "GetString({Key}) failed with culture {Culture}",
                key,
                CurrentCulture.Name
            );
        }

        return args.Length == 0 ? template : string.Format(CurrentCulture, template, args);
    }

    private void ApplyCulture(CultureInfo culture)
    {
        var targetCulture = ResolveSupportedCulture(culture ?? CultureInfo.CurrentUICulture);
        Logger.Information(
            "Applying culture: {TargetCulture} (resolved from {RequestedCulture})",
            targetCulture.Name,
            culture?.Name ?? "default"
        );

        CurrentCulture = targetCulture;
        CultureInfo.CurrentCulture = targetCulture;
        CultureInfo.CurrentUICulture = targetCulture;
        CultureInfo.DefaultThreadCurrentCulture = targetCulture;
        CultureInfo.DefaultThreadCurrentUICulture = targetCulture;
        Logger.Information("Culture applied successfully: {Culture}", targetCulture.Name);
    }

    private static CultureInfo ResolveSupportedCulture(CultureInfo culture)
    {
        Logger.Debug("Attempting to resolve supported culture for: {Culture}", culture.Name);
        var exact = SupportedCultures.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, culture.Name, StringComparison.OrdinalIgnoreCase)
        );
        if (exact is not null)
        {
            Logger.Debug("Found exact culture match: {Culture}", exact.Name);
            return exact;
        }

        var byLanguage = SupportedCultures.FirstOrDefault(candidate =>
            string.Equals(
                candidate.TwoLetterISOLanguageName,
                culture.TwoLetterISOLanguageName,
                StringComparison.OrdinalIgnoreCase
            )
        );
        if (byLanguage is not null)
        {
            Logger.Debug(
                "Found language match: {Culture} for language {Language}",
                byLanguage.Name,
                culture.TwoLetterISOLanguageName
            );
            return byLanguage;
        }

        Logger.Warning("No supported culture found for {Culture}, using original", culture.Name);
        return byLanguage ?? culture;
    }
}
