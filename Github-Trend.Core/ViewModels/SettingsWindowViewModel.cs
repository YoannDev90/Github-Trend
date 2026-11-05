using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Github_Trend.Database;
using Github_Trend.Localization;
using Github_Trend.Services;
using Serilog;

namespace Github_Trend;

public sealed class SettingsWindowViewModel : ViewModelBase, IDisposable
{
    private readonly MainWindowViewModel _mainVm;
    private readonly PropertyChangedEventHandler _onMainVmChanged;

    public Func<Task<string?>>? PickExportFilePath { get; set; }
    public Func<Task<string?>>? PickImportFilePath { get; set; }

    public MainWindowViewModel SourceViewModel => _mainVm;

    public SettingsWindowViewModel(MainWindowViewModel mainVm)
    {
        _mainVm = mainVm;
        Auth = mainVm.Auth;
        Debug = mainVm.Debug;
        Filter = mainVm.Filter;
        Settings = AppSettings.Default;

        Pages =
        [
            new(SettingsPageType.Appearance, "Appearance", "avares://Github-Trend/Assets/Icons/theme-light-dark.svg", "Theme, language & display"),
            new(SettingsPageType.General, "General", "avares://Github-Trend/Assets/Icons/tune.svg", "Behavior & preferences"),
            new(SettingsPageType.Account, "Account", "avares://Github-Trend/Assets/Icons/github.svg", "GitHub authentication & session"),
            new(SettingsPageType.Trending, "Trending", "avares://Github-Trend/Assets/Icons/star.svg", "Default filters & trending options"),
            new(SettingsPageType.Cache, "Cache", "avares://Github-Trend/Assets/Icons/database.svg", "Storage & cached data management"),
            new(SettingsPageType.Logs, "Logs", "avares://Github-Trend/Assets/Icons/code-braces.svg", "Application logs & diagnostics"),
            new(SettingsPageType.Backup, "Backup", "avares://Github-Trend/Assets/Icons/backup-restore.svg", "Export & import settings"),
            new(SettingsPageType.About, "About", "avares://Github-Trend/Assets/Icons/information.svg", "Version & system information"),
        ];

        SelectedPage = Pages[0];
        SelectedPage.IsSelected = true;

        SelectPageCommand = new RelayCommand(ExecuteSelectPage);

        SetDarkThemeCommand = new RelayCommand(_ =>
        {
            Log.Information("Theme changed to Dark");
            ThemeService.Default.SetDark(true);
            UpdateThemeUI();
        });
        SetLightThemeCommand = new RelayCommand(_ =>
        {
            Log.Information("Theme changed to Light");
            ThemeService.Default.SetDark(false);
            UpdateThemeUI();
        });

        SelectDailyCommand = new RelayCommand(_ => _mainVm.SelectDailyCommand.Execute(null));
        SelectWeeklyCommand = new RelayCommand(_ => _mainVm.SelectWeeklyCommand.Execute(null));
        SelectMonthlyCommand = new RelayCommand(_ => _mainVm.SelectMonthlyCommand.Execute(null));
        SelectAllCommand = new RelayCommand(_ => _mainVm.SelectAllCommand.Execute(null));

        ClearTrendingCacheCommand = new RelayCommand(_ => _ = ExecuteClearTrendingCacheAsync());
        ClearDetailsCacheCommand = new RelayCommand(_ => _ = ExecuteClearDetailsCacheAsync());
        ClearImageCacheCommand = new RelayCommand(_ => _ = ExecuteClearImageCacheAsync());
        ClearAllCacheCommand = new RelayCommand(_ => _ = ExecuteClearAllCacheAsync());

        OpenLinkCommand = new RelayCommand(p =>
        {
            if (p is string url && !string.IsNullOrWhiteSpace(url))
            {
                Log.Debug("OpenLink: {Url}", url);
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Log.Warning(ex, "Failed to open link: {Url}", url); }
            }
        });

        ExternalLinks = LoadExternalLinks();

        ExportSettingsCommand = new RelayCommand(_ => _ = ExecuteExportSettingsAsync());
        ImportSettingsCommand = new RelayCommand(_ => _ = ExecuteImportSettingsAsync());

        _onMainVmChanged = (_, e) =>
        {
            if (e.PropertyName is nameof(MainWindowViewModel.IsDailySelected)
                or nameof(MainWindowViewModel.IsWeeklySelected)
                or nameof(MainWindowViewModel.IsMonthlySelected)
                or nameof(MainWindowViewModel.IsAllSelected))
                NotifyTimeRangeChanged();
        };
        _mainVm.PropertyChanged += _onMainVmChanged;

        _ = Settings.LoadAsync();

        UpdateThemeUI();
    }

    public void Dispose()
    {
        _mainVm.PropertyChanged -= _onMainVmChanged;
    }

    public AppSettings Settings { get; }
    public List<SettingsPage> Pages { get; }
    public GitHubAuthViewModel Auth { get; }
    public DebugViewModel Debug { get; }
    public LanguageFilterViewModel Filter { get; }

    private SettingsPage _selectedPage = null!;
    public SettingsPage SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (SetProperty(ref _selectedPage, value))
            {
                OnPropertyChanged(nameof(IsAppearancePage));
                OnPropertyChanged(nameof(IsGeneralPage));
                OnPropertyChanged(nameof(IsAccountPage));
                OnPropertyChanged(nameof(IsTrendingPage));
                OnPropertyChanged(nameof(IsCachePage));
                OnPropertyChanged(nameof(IsLogsPage));
                OnPropertyChanged(nameof(IsBackupPage));
                OnPropertyChanged(nameof(IsAboutPage));
            }
        }
    }

    public bool IsDarkTheme { get; private set; }

    public bool IsAppearancePage => SelectedPage?.Type == SettingsPageType.Appearance;
    public bool IsGeneralPage => SelectedPage?.Type == SettingsPageType.General;
    public bool IsAccountPage => SelectedPage?.Type == SettingsPageType.Account;
    public bool IsTrendingPage => SelectedPage?.Type == SettingsPageType.Trending;
    public bool IsCachePage => SelectedPage?.Type == SettingsPageType.Cache;
    public bool IsLogsPage => SelectedPage?.Type == SettingsPageType.Logs;
    public bool IsBackupPage => SelectedPage?.Type == SettingsPageType.Backup;
    public bool IsAboutPage => SelectedPage?.Type == SettingsPageType.About;

    public ICommand SelectPageCommand { get; }
    public ICommand SetDarkThemeCommand { get; }
    public ICommand SetLightThemeCommand { get; }
    public ICommand SelectDailyCommand { get; }
    public ICommand SelectWeeklyCommand { get; }
    public ICommand SelectMonthlyCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearTrendingCacheCommand { get; }
    public ICommand ClearDetailsCacheCommand { get; }
    public ICommand ClearImageCacheCommand { get; }
    public ICommand ClearAllCacheCommand { get; }
    public ICommand OpenLinkCommand { get; }
    public ICommand ExportSettingsCommand { get; }
    public ICommand ImportSettingsCommand { get; }

    public List<ExternalLink> ExternalLinks { get; }

    public string LocalizationLabel => LocalizationService.CurrentLanguage switch
    {
        "fr" => "Français",
        _ => "English"
    };

    public string AppVersion =>
        typeof(SettingsWindowViewModel).Assembly.GetName()?.Version?.ToString() ?? "1.0.0";
    public string OsVersion => RuntimeInformation.OSDescription;
    public string OsArchitecture => RuntimeInformation.ProcessArchitecture.ToString();
    public string DotNetVersion => RuntimeInformation.FrameworkDescription;

    public bool IsLanguageFrench
    {
        get => LocalizationService.CurrentLanguage == "fr";
        set
        {
            Log.Information("Language changed to {Lang}", value ? "fr" : "en");
            LocalizationService.SetLanguage(value ? "fr" : null);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLanguageEnglish));
        }
    }

    public bool IsLanguageEnglish
    {
        get => LocalizationService.CurrentLanguage != "fr";
        set
        {
            if (value) IsLanguageFrench = false;
        }
    }

    public string TrendingTimeRangeLabel => _mainVm.SelectedTimeRangeLabel;
    public int TrendingCount => _mainVm.TrendingCount;

    public bool IsDailySelected => _mainVm.IsDailySelected;
    public bool IsWeeklySelected => _mainVm.IsWeeklySelected;
    public bool IsMonthlySelected => _mainVm.IsMonthlySelected;
    public bool IsAllSelected => _mainVm.IsAllSelected;

    public string CacheTrendingCount { get; private set; } = "—";
    public string CacheDetailsCount { get; private set; } = "—";
    public string CacheImagesCount { get; private set; } = "—";

    private void ExecuteSelectPage(object? param)
    {
        if (param is SettingsPage page)
        {
            Log.Debug("Settings page: {Page}", page.Label);
            foreach (var p in Pages)
                p.IsSelected = p == page;
            SelectedPage = page;
            if (page.Type == SettingsPageType.Cache)
                _ = RefreshCacheStatsAsync();
        }
    }

    private void UpdateThemeUI()
    {
        IsDarkTheme = ThemeService.Default.IsDark;
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    public void NotifyTimeRangeChanged()
    {
        OnPropertyChanged(nameof(IsDailySelected));
        OnPropertyChanged(nameof(IsWeeklySelected));
        OnPropertyChanged(nameof(IsMonthlySelected));
        OnPropertyChanged(nameof(IsAllSelected));
        OnPropertyChanged(nameof(TrendingTimeRangeLabel));
        OnPropertyChanged(nameof(TrendingCount));
    }

    private async Task RefreshCacheStatsAsync()
    {
        try
        {
            var db = _mainVm.Database;
            var trending = await db.GetTrendingCacheCountAsync();
            var details = await db.GetRepoDetailsCacheCountAsync();
            var images = await db.GetImageCacheCountAsync();
            var trendingSize = await db.GetTrendingCacheSizeAsync();
            var detailsSize = await db.GetRepoDetailsCacheSizeAsync();
            var imagesSize = await db.GetImageCacheSizeAsync();
            CacheTrendingCount = $"{trending} entries ({FormatSize(trendingSize)})";
            CacheDetailsCount = $"{details} entries ({FormatSize(detailsSize)})";
            CacheImagesCount = $"{images} entries ({FormatSize(imagesSize)})";
            OnPropertyChanged(nameof(CacheTrendingCount));
            OnPropertyChanged(nameof(CacheDetailsCount));
            OnPropertyChanged(nameof(CacheImagesCount));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to refresh cache stats");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F0} KB";
        return $"{bytes} B";
    }

    private async Task ExecuteClearTrendingCacheAsync()
    {
        Log.Debug("ClearTrendingCache: starting");
        await _mainVm.Database.ClearAllTrendingCacheAsync();
        Log.Information("Trending cache cleared");
        await RefreshCacheStatsAsync();
    }

    private async Task ExecuteClearDetailsCacheAsync()
    {
        Log.Debug("ClearDetailsCache: starting");
        await _mainVm.Database.ClearAllRepoDetailsCacheAsync();
        Log.Information("Repository details cache cleared");
        await RefreshCacheStatsAsync();
    }

    private async Task ExecuteClearImageCacheAsync()
    {
        Log.Debug("ClearImageCache: starting");
        await _mainVm.Database.ClearAllImageCacheAsync();
        Log.Information("Image cache cleared");
        await RefreshCacheStatsAsync();
    }

    private async Task ExecuteClearAllCacheAsync()
    {
        Log.Debug("ClearAllCache: starting");
        await _mainVm.Database.ClearAllTrendingCacheAsync();
        await _mainVm.Database.ClearAllRepoDetailsCacheAsync();
        await _mainVm.Database.ClearAllImageCacheAsync();
        Log.Information("All caches cleared");
        await RefreshCacheStatsAsync();
    }

    private static List<ExternalLink> LoadExternalLinks()
    {
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(configPath))
                return GetDefaultLinks();

            var json = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("Infos", out var infos))
                return GetDefaultLinks();

            var links = new List<ExternalLink>();

            if (TryGetString(infos, "Repository", out var repoUrl))
                links.Add(new ExternalLink("GitHub Repository", repoUrl, "avares://Github-Trend/Assets/Icons/github.svg"));
            if (TryGetString(infos, "Issues", out var issuesUrl))
                links.Add(new ExternalLink("Report an Issue", issuesUrl, "avares://Github-Trend/Assets/Icons/open.svg"));
            if (TryGetString(infos, "Releases", out var releasesUrl))
                links.Add(new ExternalLink("Releases", releasesUrl, "avares://Github-Trend/Assets/Icons/update.svg"));
            if (TryGetString(infos, "AuthorUrl", out var authorUrl))
                links.Add(new ExternalLink("Author", authorUrl, "avares://Github-Trend/Assets/Icons/github.svg"));

            return links.Count > 0 ? links : GetDefaultLinks();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load external links from config");
            return GetDefaultLinks();
        }
    }

    private static List<ExternalLink> GetDefaultLinks()
    {
        return
        [
            new("GitHub Repository", "https://github.com/YoannDev90/Github-Trend", "avares://Github-Trend/Assets/Icons/github.svg"),
            new("Report an Issue", "https://github.com/YoannDev90/Github-Trend/issues", "avares://Github-Trend/Assets/Icons/open.svg"),
            new("Releases", "https://github.com/YoannDev90/Github-Trend/releases", "avares://Github-Trend/Assets/Icons/update.svg"),
        ];
    }

    private static bool TryGetString(JsonElement obj, string key, out string value)
    {
        if (obj.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
        value = string.Empty;
        return false;
    }

    private static string UserPreferencesPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend",
            "user_preferences.json"
        );

    private async Task ExecuteExportSettingsAsync()
    {
        Log.Debug("ExportSettings: starting");
        if (PickExportFilePath is null) return;

        var path = await PickExportFilePath();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var prefsPath = UserPreferencesPath;
            if (File.Exists(prefsPath))
            {
                var json = File.ReadAllText(prefsPath);

                var tmpPath = path + ".tmp";
                await File.WriteAllTextAsync(tmpPath, json);
                File.Move(tmpPath, path, overwrite: true);

                Log.Information("User preferences exported to {Path}", path);
            }
            else
            {
                Log.Warning("No user preferences file found at {Path}", prefsPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export preferences to {Path}", path);
        }
    }

    private async Task ExecuteImportSettingsAsync()
    {
        Log.Debug("ImportSettings: starting");
        if (PickImportFilePath is null) return;

        var path = await PickImportFilePath();
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var json = await File.ReadAllTextAsync(path);

            JsonDocument.Parse(json);

            if (!json.Contains("ShowBanners") && !json.Contains("AutoRefresh"))
            {
                Log.Warning("Import file does not look like a valid preferences file");
                return;
            }

            var prefsPath = UserPreferencesPath;
            var backupPath = prefsPath + ".bak";
            if (File.Exists(prefsPath))
                File.Copy(prefsPath, backupPath, overwrite: true);

            var tmpPath = prefsPath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json);
            File.Move(tmpPath, prefsPath, overwrite: true);

            Log.Information("User preferences imported from {Path}", path);

            await AppSettings.Default.LoadAsync();
        }
        catch (JsonException)
        {
            Log.Error("Import failed: the selected file is not valid JSON");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to import preferences from {Path}", path);
        }
    }
}

public sealed record ExternalLink(string Title, string Url, string Icon);
