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
    
    // Hardcoded strings mapping for default (English) - extracted from Resources.resx
    private static readonly Dictionary<string, string> DefaultStrings = new()
    {
        { "WindowTitle", "GitHub Trend" },
        { "GitHubAccountSectionTitle", "GitHub Account" },
        { "GitHubDeviceCodeTitle", "Login code" },
        { "CopyCodeButtonText", "Copy code" },
        { "GitHubSignInButtonText", "Sign in with GitHub" },
        { "GitHubRefreshButtonText", "Refresh token" },
        { "GitHubSignOutButtonText", "Sign out" },
        { "SearchTitle", "Search" },
        { "SearchPlaceholderText", "e.g. JavaScript, Rust..." },
        { "TrendingPeriodTitle", "Trending period" },
        { "TrendingPeriodSubtitle", "Choose the time window" },
        { "TimeRangeDaily", "Daily" },
        { "TimeRangeWeekly", "Weekly" },
        { "TimeRangeMonthly", "Monthly" },
        { "TimeRangeAll", "All" },
        { "SelectedLanguagesTitle", "Selected" },
        { "SuggestionsTitle", "Suggestions" },
        { "RefreshButtonText", "Refresh" },
        { "TrendingRepositoriesTitle", "GitHub Trending repositories" },
        { "ContributorsTitle", "Contributors" },
        { "StatusLoadingColors", "Loading colors..." },
        { "StatusColorsLoaded", "Colors loaded: {0}" },
        { "StatusColorsLoadError", "Failed to load colors: {0}" },
        { "StatusColorsRefreshing", "Refreshing colors..." },
        { "StatusColorsRefreshed", "Colors refreshed: {0}" },
        { "StatusColorsRefreshError", "Failed to refresh colors: {0}" },
        { "StatusSelectionSaved", "Selection saved: {0} language(s)" },
        { "StatusSelectionSaveError", "Failed to save selection: {0}" },
        { "StatusGitHubSignInStarting", "Initializing GitHub sign-in..." },
        { "StatusGitHubSignInSuccess", "GitHub sign-in succeeded: {0}" },
        { "StatusGitHubSignInFailed", "GitHub sign-in failed: {0}" },
        { "StatusGitHubRefreshStarting", "Refreshing GitHub session..." },
        { "StatusGitHubRefreshSuccess", "GitHub session refreshed." },
        { "StatusGitHubRefreshFailed", "GitHub refresh failed: {0}" },
        { "StatusGitHubSignedOut", "GitHub session removed." },
        { "GitHubAuthNotConfigured", "GitHub sign-in is not configured" },
        { "GitHubAuthNotConnected", "Not connected to GitHub" },
        { "GitHubAuthConnected", "Connected to GitHub: {0}" },
        { "GitHubAuthNoAccountConnected", "No account connected" },
        { "GitHubAuthNoAccountLinked", "No linked account" },
        { "GitHubAuthLinkedAccount", "Linked account: {0} ({1})" },
        { "GitHubDeviceCodeCopied", "GitHub code copied to clipboard." },
        { "NoLanguagesFound", "No language found" },
        { "SuggestionCountOne", "1 suggestion" },
        { "SuggestionCountMany", "{0} suggestions" },
        { "SelectionSummaryZero", "No saved selection" },
        { "SelectionSummaryOne", "1 language selected" },
        { "SelectionSummaryMany", "{0} languages selected" },
        { "TrendingCountZero", "No trending repositories" },
        { "TrendingCountOne", "1 trending repository" },
        { "TrendingCountMany", "{0} trending repositories" },
        { "UnknownLicense", "Unknown license" },
        { "UnknownLanguage", "Unknown" },
        { "UpdatedUnknown", "Updated: unknown" },
        { "UpdatedJustNow", "Updated: just now" },
        { "UpdatedMinutesAgoOne", "Updated: 1 minute ago" },
        { "UpdatedMinutesAgoMany", "Updated: {0} minutes ago" },
        { "UpdatedHoursAgoOne", "Updated: 1 hour ago" },
        { "UpdatedHoursAgoMany", "Updated: {0} hours ago" },
        { "UpdatedYesterday", "Updated: yesterday" },
        { "UpdatedDaysAgoOne", "Updated: 1 day ago" },
        { "UpdatedDaysAgoMany", "Updated: {0} days ago" },
        { "UpdatedWeeksAgo", "Updated: {0} week(s) ago" },
        { "UpdatedMonthsAgo", "Updated: {0} month(s) ago" },
        { "UpdatedYearsAgo", "Updated: {0} year(s) ago" },
        { "OpenRepositoryFailure", "Unable to open the link" },
        { "ConnectGitHubToStar", "Sign in to GitHub to star repositories." },
        { "StarRepositorySuccess", "Starred repository on GitHub: {0}" },
        { "StarRepositoryFailure", "Unable to star the repository" },
        { "ConnectGitHubToWatch", "Sign in to GitHub to watch repositories." },
        { "WatchRepositorySuccess", "Watched repository on GitHub: {0}" },
        { "WatchRepositoryFailure", "Unable to watch the repository" },
        { "RepoActionBlockedByIntegration", "GitHub blocks {0} {1} with this token. Sign in again with a full OAuth user token, then try again." },
        { "RepoActionWatchRequiresNotificationsScope", "GitHub does not allow watching {0} with the current session. Sign in again; the `notifications` scope may be missing." },
        { "RepoActionFailedHttp", "Unable to {0} {1} (HTTP {2})" },
        { "GitHubAuthDeviceCodePromptOpen", "GitHub code: {0}. Validate it in the opened browser." },
        { "GitHubAuthDeviceCodePromptManual", "GitHub code: {0}. Open: {1}" },
        { "InvalidDeviceCodeResponse", "Invalid device code response from GitHub" },
        { "MissingVerificationUrl", "Missing GitHub verification URL" },
        { "DeviceFlowAuthenticationFailed", "Device flow authentication failed: {0}" },
        { "GitHubClientIdNotConfigured", "GitHub App client id is not configured. Set GITHUB_APP_CLIENT_ID environment variable." },
        { "ActionStar", "star" },
        { "ActionWatch", "watch" }
    };

    // Hardcoded strings mapping for French - extracted from Resources.fr.resx
    private static readonly Dictionary<string, string> FrenchStrings = new()
    {
        { "WindowTitle", "GitHub Trend" },
        { "GitHubAccountSectionTitle", "Compte GitHub" },
        { "GitHubDeviceCodeTitle", "Code de connexion" },
        { "CopyCodeButtonText", "Copier le code" },
        { "GitHubSignInButtonText", "Se connecter avec GitHub" },
        { "GitHubRefreshButtonText", "Rafraîchir le token" },
        { "GitHubSignOutButtonText", "Se déconnecter" },
        { "SearchTitle", "Recherche" },
        { "SearchPlaceholderText", "Ex. JavaScript, Rust..." },
        { "TrendingPeriodTitle", "Période du trending" },
        { "TrendingPeriodSubtitle", "Choisis la fenêtre de temps" },
        { "TimeRangeDaily", "Quotidien" },
        { "TimeRangeWeekly", "Hebdomadaire" },
        { "TimeRangeMonthly", "Mensuel" },
        { "TimeRangeAll", "Tout" },
        { "SelectedLanguagesTitle", "Sélectionnés" },
        { "SuggestionsTitle", "Suggestions" },
        { "RefreshButtonText", "Rafraîchir" },
        { "TrendingRepositoriesTitle", "Dépôts GitHub tendance" },
        { "ContributorsTitle", "Contributeurs" },
        { "StatusLoadingColors", "Chargement des couleurs..." },
        { "StatusColorsLoaded", "Couleurs chargées: {0}" },
        { "StatusColorsLoadError", "Échec du chargement des couleurs: {0}" },
        { "StatusColorsRefreshing", "Rafraîchissement des couleurs..." },
        { "StatusColorsRefreshed", "Couleurs rafraîchies: {0}" },
        { "StatusColorsRefreshError", "Échec du rafraîchissement des couleurs: {0}" },
        { "StatusSelectionSaved", "Sélection sauvegardée: {0} langue(s)" },
        { "StatusSelectionSaveError", "Échec de la sauvegarde de la sélection: {0}" },
        { "StatusGitHubSignInStarting", "Initialisation de la connexion GitHub..." },
        { "StatusGitHubSignInSuccess", "Connexion GitHub réussie: {0}" },
        { "StatusGitHubSignInFailed", "Échec de la connexion GitHub: {0}" },
        { "StatusGitHubRefreshStarting", "Rafraîchissement de la session GitHub..." },
        { "StatusGitHubRefreshSuccess", "Session GitHub rafraîchie." },
        { "StatusGitHubRefreshFailed", "Échec du rafraîchissement de GitHub: {0}" },
        { "StatusGitHubSignedOut", "Session GitHub supprimée." },
        { "GitHubAuthNotConfigured", "La connexion au GitHub n'est pas configurée" },
        { "GitHubAuthNotConnected", "Non connecté à GitHub" },
        { "GitHubAuthConnected", "Connecté à GitHub: {0}" },
        { "GitHubAuthNoAccountConnected", "Aucun compte connecté" },
        { "GitHubAuthNoAccountLinked", "Aucun compte lié" },
        { "GitHubAuthLinkedAccount", "Compte lié: {0} ({1})" },
        { "GitHubDeviceCodeCopied", "Code GitHub copié dans le presse-papiers." },
        { "NoLanguagesFound", "Aucun langage trouvé" },
        { "SuggestionCountOne", "1 suggestion" },
        { "SuggestionCountMany", "{0} suggestions" },
        { "SelectionSummaryZero", "Aucune sélection enregistrée" },
        { "SelectionSummaryOne", "1 langage sélectionné" },
        { "SelectionSummaryMany", "{0} langages sélectionnés" },
        { "TrendingCountZero", "Aucun dépôt tendance" },
        { "TrendingCountOne", "1 dépôt tendance" },
        { "TrendingCountMany", "{0} dépôts tendance" },
        { "UnknownLicense", "Licence inconnue" },
        { "UnknownLanguage", "Inconnu" },
        { "UpdatedUnknown", "Mis à jour: inconnu" },
        { "UpdatedJustNow", "Mis à jour: à l'instant" },
        { "UpdatedMinutesAgoOne", "Mis à jour: il y a 1 minute" },
        { "UpdatedMinutesAgoMany", "Mis à jour: il y a {0} minutes" },
        { "UpdatedHoursAgoOne", "Mis à jour: il y a 1 heure" },
        { "UpdatedHoursAgoMany", "Mis à jour: il y a {0} heures" },
        { "UpdatedYesterday", "Mis à jour: hier" },
        { "UpdatedDaysAgoOne", "Mis à jour: il y a 1 jour" },
        { "UpdatedDaysAgoMany", "Mis à jour: il y a {0} jours" },
        { "UpdatedWeeksAgo", "Mis à jour: il y a {0} semaine(s)" },
        { "UpdatedMonthsAgo", "Mis à jour: il y a {0} mois" },
        { "UpdatedYearsAgo", "Mis à jour: il y a {0} an(s)" },
        { "OpenRepositoryFailure", "Impossible d'ouvrir le lien" },
        { "ConnectGitHubToStar", "Connecte-toi à GitHub pour mettre en étoile les dépôts." },
        { "StarRepositorySuccess", "Dépôt mis en étoile sur GitHub: {0}" },
        { "StarRepositoryFailure", "Impossible de mettre en étoile le dépôt" },
        { "ConnectGitHubToWatch", "Connecte-toi à GitHub pour surveiller les dépôts." },
        { "WatchRepositorySuccess", "Dépôt surveillé sur GitHub: {0}" },
        { "WatchRepositoryFailure", "Impossible de surveiller le dépôt" },
        { "RepoActionBlockedByIntegration", "GitHub bloque {0} {1} avec ce token. Reconnecte-toi avec un token OAuth utilisateur complet, puis réessaye." },
        { "RepoActionWatchRequiresNotificationsScope", "GitHub ne permet pas de surveiller {0} avec la session actuelle. Reconnecte-toi ; le scope `notifications` est peut-être manquant." },
        { "RepoActionFailedHttp", "Impossible de {0} {1} (HTTP {2})" },
        { "GitHubAuthDeviceCodePromptOpen", "Code GitHub : {0}. Valide-le dans le navigateur ouvert." },
        { "GitHubAuthDeviceCodePromptManual", "Code GitHub : {0}. Ouvre : {1}" },
        { "InvalidDeviceCodeResponse", "Réponse de code de connexion GitHub invalide" },
        { "MissingVerificationUrl", "URL de vérification GitHub manquante" },
        { "DeviceFlowAuthenticationFailed", "Échec de l'authentification par code de connexion : {0}" },
        { "GitHubClientIdNotConfigured", "L'identifiant client de l'application GitHub n'est pas configuré. Définis la variable d'environnement GITHUB_APP_CLIENT_ID." },
        { "ActionStar", "étoiler" },
        { "ActionWatch", "surveiller" }
    };

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;
    private static readonly IReadOnlyList<CultureInfo> SupportedCultures = new[]
    {
        CultureInfo.GetCultureInfo("en-US"),
        CultureInfo.GetCultureInfo("fr-FR")
    };



    public CultureInfo CurrentCulture => _currentCulture;

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
    public string RepoActionBlockedByIntegration => GetString(nameof(RepoActionBlockedByIntegration));
    public string RepoActionWatchRequiresNotificationsScope => GetString(nameof(RepoActionWatchRequiresNotificationsScope));
    public string RepoActionFailedHttp => GetString(nameof(RepoActionFailedHttp));
    public string GitHubAuthDeviceCodePromptOpen => GetString(nameof(GitHubAuthDeviceCodePromptOpen));
    public string GitHubAuthDeviceCodePromptManual => GetString(nameof(GitHubAuthDeviceCodePromptManual));
    public string InvalidDeviceCodeResponse => GetString(nameof(InvalidDeviceCodeResponse));
    public string MissingVerificationUrl => GetString(nameof(MissingVerificationUrl));
    public string DeviceFlowAuthenticationFailed => GetString(nameof(DeviceFlowAuthenticationFailed));
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
        var strings = _currentCulture.TwoLetterISOLanguageName == "fr" ? FrenchStrings : DefaultStrings;
        var template = strings.TryGetValue(key, out var value) ? value : key;
        
        Logger.Information("GetString({Key}, {ArgCount}) = {Value} (Culture: {Culture})", key, args.Length, template, _currentCulture.Name);
        
        return args.Length == 0
            ? template
            : string.Format(_currentCulture, template, args);
    }

    private void ApplyCulture(CultureInfo culture)
    {
        var targetCulture = ResolveSupportedCulture(culture ?? CultureInfo.CurrentUICulture);
        Logger.Information("Applying culture: {TargetCulture} (resolved from {RequestedCulture})", targetCulture.Name, culture?.Name ?? "default");
        
        _currentCulture = targetCulture;
        CultureInfo.CurrentCulture = targetCulture;
        CultureInfo.CurrentUICulture = targetCulture;
        CultureInfo.DefaultThreadCurrentCulture = targetCulture;
        CultureInfo.DefaultThreadCurrentUICulture = targetCulture;
        Logger.Information("Culture applied successfully: {Culture}", targetCulture.Name);
    }

    private static CultureInfo ResolveSupportedCulture(CultureInfo culture)
    {
        Logger.Debug("Attempting to resolve supported culture for: {Culture}", culture.Name);
        var exact = SupportedCultures.FirstOrDefault(candidate => string.Equals(candidate.Name, culture.Name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            Logger.Debug("Found exact culture match: {Culture}", exact.Name);
            return exact;
        }

        var byLanguage = SupportedCultures.FirstOrDefault(candidate => string.Equals(candidate.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase));
        if (byLanguage is not null)
        {
            Logger.Debug("Found language match: {Culture} for language {Language}", byLanguage.Name, culture.TwoLetterISOLanguageName);
            return byLanguage;
        }
        Logger.Warning("No supported culture found for {Culture}, using original", culture.Name);
        return byLanguage ?? culture;
    }

}

