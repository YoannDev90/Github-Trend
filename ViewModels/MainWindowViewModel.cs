using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Github_Trend.Localization;
using Serilog;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly (string Query, string Label)[] TimeRanges =
    {
        ("daily", nameof(LocalizationService.TimeRangeDaily)),
        ("weekly", nameof(LocalizationService.TimeRangeWeekly)),
        ("monthly", nameof(LocalizationService.TimeRangeMonthly)),
        ("all", nameof(LocalizationService.TimeRangeAll))
    };

    // Constructor to initialize commands
    public MainWindowViewModel()
    {
        RefreshCommand = new RelayCommand(_ => ExecuteRefreshColors(), _ => !_isInitializing && !_isRefreshing);
        GitHubSignInCommand = new RelayCommand(_ => ExecuteGitHubSignIn(), _ => !_isInitializing && !_isGitHubAuthenticating);
        GitHubSignOutCommand = new RelayCommand(_ => ExecuteGitHubSignOut(), _ => IsGitHubConnected && !_isGitHubAuthenticating);
        GitHubRefreshCommand = new RelayCommand(_ => ExecuteGitHubRefresh(), _ => IsGitHubConnected && !_isGitHubAuthenticating);
        CopyGitHubCodeCommand = new RelayCommand(_ => RaiseGitHubDeviceCodeCopyRequested(), _ => HasGitHubDeviceCode);
        OpenRepositoryCommand = new RelayCommand(ExecuteOpenRepository, parameter => parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.RepositoryLink));
        StarRepositoryCommand = new RelayCommand(ExecuteStarRepository, parameter => parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.Repository));
        WatchRepositoryCommand = new RelayCommand(ExecuteWatchRepository, parameter => parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.Repository));
        _githubAuthService.SessionChanged += (_, _) => UpdateGitHubAuthState();
        _selectedTimeRangeIndex = 0;
        _githubAuthStatusKey = nameof(LocalizationService.GitHubAuthNotConfigured);
        _githubAuthStatusArgs = Array.Empty<object?>();
        _githubAccountSummaryKey = nameof(LocalizationService.GitHubAuthNoAccountConnected);
        _githubAccountSummaryArgs = Array.Empty<object?>();
        _statusMessageKey = nameof(LocalizationService.StatusLoadingColors);
        _statusMessageArgs = Array.Empty<object?>();
        LoadDebugInfo();
    }

    private bool _isRefreshing;
    private bool _isGitHubAuthenticating;
    private readonly SelectedLanguagesStore _selectedLanguagesStore = new();
    private readonly GitHubAuthenticationService _githubAuthService = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _filteredLanguages = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _selectedLanguages = new();
    private readonly List<LanguageOptionViewModel> _allLanguages = new();
    private bool _isInitializing;
    private GithubColorsCatalog? _githubColors;
    private string _searchText = string.Empty;
    private string _statusMessageKey;
    private object?[] _statusMessageArgs = Array.Empty<object?>();
    private string? _statusMessageOverride;
    private string _githubAuthStatusKey;
    private object?[] _githubAuthStatusArgs = Array.Empty<object?>();
    private string? _githubAuthStatusOverride;
    private string _githubAccountSummaryKey;
    private object?[] _githubAccountSummaryArgs = Array.Empty<object?>();
    private string _githubDeviceCode = string.Empty;
    private string _appLogs = string.Empty;
    private string _debugInfo = string.Empty;

    private readonly ObservableCollection<GithubTrendingRepository> _trendingRepositories = new();
    private List<GithubTrendingRepository>? _trendingData;

    private int _selectedTimeRangeIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand GitHubSignInCommand { get; }

    public ICommand GitHubSignOutCommand { get; }

    public ICommand GitHubRefreshCommand { get; }

    public ICommand CopyGitHubCodeCommand { get; }
    
    public ICommand OpenRepositoryCommand { get; }

    public ICommand StarRepositoryCommand { get; }

    public ICommand WatchRepositoryCommand { get; }

    public event EventHandler? GitHubDeviceCodeCopyRequested;

    public string GitHubDeviceCode
    {
        get => _githubDeviceCode;
        set
        {
            if (_githubDeviceCode == value)
            {
                return;
            }

            _githubDeviceCode = value;
            OnPropertyChanged();
            (CopyGitHubCodeCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public bool HasGitHubDeviceCode => !string.IsNullOrWhiteSpace(_githubDeviceCode);

    public string AppLogs
    {
        get => _appLogs;
        private set
        {
            if (_appLogs == value)
                return;
            _appLogs = value;
            OnPropertyChanged();
        }
    }

    public string DebugInfo
    {
        get => _debugInfo;
        private set
        {
            if (_debugInfo == value)
                return;
            _debugInfo = value;
            OnPropertyChanged();
        }
    }


    public ObservableCollection<GithubTrendingRepository> TrendingRepositories => _trendingRepositories;

    public int SelectedTimeRangeIndex
    {
        get => _selectedTimeRangeIndex;
        set
        {
            if (_selectedTimeRangeIndex == value)
                return;

            _selectedTimeRangeIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedTimeRangeLabel));
            OnPropertyChanged(nameof(IsDailySelected));
            OnPropertyChanged(nameof(IsWeeklySelected));
            OnPropertyChanged(nameof(IsMonthlySelected));
            OnPropertyChanged(nameof(IsAllSelected));
            Log.Information("Trending time range changed => {TimeRange}", SelectedTimeRangeLabel);
            _ = RefreshTrendingRepositoriesAsync();
        }
    }

    public bool IsDailySelected
    {
        get => _selectedTimeRangeIndex == 0;
        set
        {
            if (value)
            {
                SelectedTimeRangeIndex = 0;
            }
        }
    }

    public bool IsWeeklySelected
    {
        get => _selectedTimeRangeIndex == 1;
        set
        {
            if (value)
            {
                SelectedTimeRangeIndex = 1;
            }
        }
    }

    public bool IsMonthlySelected
    {
        get => _selectedTimeRangeIndex == 2;
        set
        {
            if (value)
            {
                SelectedTimeRangeIndex = 2;
            }
        }
    }

    public bool IsAllSelected
    {
        get => _selectedTimeRangeIndex == 3;
        set
        {
            if (value)
            {
                SelectedTimeRangeIndex = 3;
            }
        }
    }

    public string SelectedTimeRangeLabel => Github_Trend.Localization.Localization.Instance.GetString(TimeRanges[Math.Max(0, Math.Min(_selectedTimeRangeIndex, TimeRanges.Length - 1))].Label);

    public string SelectedTimeRangeQuery => TimeRanges[Math.Max(0, Math.Min(_selectedTimeRangeIndex, TimeRanges.Length - 1))].Query;

    public GithubColorsCatalog? GithubColors
    {
        get => _githubColors;
        private set
        {
            if (ReferenceEquals(_githubColors, value))
            {
                return;
            }

            _githubColors = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ColorCount));
        }
    }

    public ObservableCollection<LanguageOptionViewModel> FilteredLanguages => _filteredLanguages;

    public ObservableCollection<LanguageOptionViewModel> SelectedLanguages => _selectedLanguages;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public string StatusMessage => _statusMessageOverride ?? Github_Trend.Localization.Localization.Instance.GetString(_statusMessageKey, _statusMessageArgs);

    public bool IsGitHubConnected => _githubAuthService.IsConnected;

    public bool IsGitHubAuthenticating
    {
        get => _isGitHubAuthenticating;
        private set
        {
            if (_isGitHubAuthenticating == value)
            {
                return;
            }

            _isGitHubAuthenticating = value;
            OnPropertyChanged();
            RaiseGitHubCommandStateChanged();
        }
    }

    public string GitHubAuthStatus => _githubAuthStatusOverride ?? Github_Trend.Localization.Localization.Instance.GetString(_githubAuthStatusKey, _githubAuthStatusArgs);

    public string GitHubAccountSummary => Github_Trend.Localization.Localization.Instance.GetString(_githubAccountSummaryKey, _githubAccountSummaryArgs);

    public int ColorCount => GithubColors?.Colors.Count ?? 0;

    public int SelectedCount => _selectedLanguages.Count;

    public int VisibleCount => _filteredLanguages.Count;

    public string MatchingLanguagesLabel => _filteredLanguages.Count switch
    {
        0 => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.NoLanguagesFound)),
        1 => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.SuggestionCountOne)),
        _ => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.SuggestionCountMany), _filteredLanguages.Count)
    };

    public string SelectionSummary => _selectedLanguages.Count == 0
        ? Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.SelectionSummaryZero))
        : _selectedLanguages.Count == 1
            ? Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.SelectionSummaryOne))
            : Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.SelectionSummaryMany), _selectedLanguages.Count);

    public int TrendingCount => _trendingData?.Count ?? 0;

    public string TrendingLabel => TrendingCount switch
    {
        0 => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.TrendingCountZero)),
        1 => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.TrendingCountOne)),
        _ => Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.TrendingCountMany), TrendingCount)
    };

    public async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;

            var colorsTask = GithubColorsService.FetchAsync();
            var selectionsTask = _selectedLanguagesStore.LoadAsync();

            await Task.WhenAll(colorsTask, selectionsTask);

            GithubColors = colorsTask.Result;
            RebuildLanguages(GithubColors, selectionsTask.Result);
            RefreshSelectedLanguages();
            ApplyFilter();
            SetStatusMessage(nameof(LocalizationService.StatusColorsLoaded), ColorCount);
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(VisibleCount));
            await _githubAuthService.InitializeAsync();
            UpdateGitHubAuthState();
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetStatusMessage(nameof(LocalizationService.StatusColorsLoadError), ex.Message);
        }
        finally
        {
            _isInitializing = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RaiseGitHubCommandStateChanged();
        }
        Log.Information("Initial trending refresh queued");
        _ = RefreshTrendingRepositoriesAsync();
    }

    private void ExecuteGitHubSignIn() => _ = SignInWithGitHubAsync();

    private void ExecuteGitHubSignOut() => _ = SignOutGitHubAsync();

    private void ExecuteGitHubRefresh() => _ = RefreshGitHubSessionAsync();

    private void RaiseGitHubDeviceCodeCopyRequested() => GitHubDeviceCodeCopyRequested?.Invoke(this, EventArgs.Empty);

    private void ExecuteStarRepository(object? parameter) => _ = ExecuteStarRepositoryAsync(parameter);

    private void ExecuteWatchRepository(object? parameter) => _ = ExecuteWatchRepositoryAsync(parameter);

    private async Task SignInWithGitHubAsync()
    {
        if (_isInitializing || _isGitHubAuthenticating)
        {
            return;
        }

        try
        {
            IsGitHubAuthenticating = true;
            GitHubDeviceCode = string.Empty;
            SetGitHubAuthStatusRaw(Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.StatusGitHubSignInStarting)));
            var session = await _githubAuthService.BeginInteractiveSignInAsync(
                message => SetGitHubAuthStatusRaw(message),
                code =>
                {
                    GitHubDeviceCode = code;
                    RaiseGitHubDeviceCodeCopyRequested();
                });
            UpdateGitHubAuthState(session);
            SetStatusMessage(nameof(LocalizationService.StatusGitHubSignInSuccess), session?.Summary ?? string.Empty);
        }
        catch (Exception ex)
        {
            ClearGitHubAuthStatusOverride();
            SetGitHubAuthStatus(nameof(LocalizationService.StatusGitHubSignInFailed), ex.Message);
            SetStatusMessage(nameof(LocalizationService.StatusGitHubSignInFailed), ex.Message);
        }
        finally
        {
            IsGitHubAuthenticating = false;
            GitHubDeviceCode = string.Empty;
            ClearGitHubAuthStatusOverride();
            RaiseGitHubCommandStateChanged();
        }
    }

    private async Task RefreshGitHubSessionAsync()
    {
        try
        {
            IsGitHubAuthenticating = true;
            SetGitHubAuthStatus(nameof(LocalizationService.StatusGitHubRefreshStarting));
            await _githubAuthService.RefreshCurrentAsync();
            UpdateGitHubAuthState();
            SetStatusMessage(nameof(LocalizationService.StatusGitHubRefreshSuccess));
        }
        catch (Exception ex)
        {
            SetGitHubAuthStatus(nameof(LocalizationService.StatusGitHubRefreshFailed), ex.Message);
            SetStatusMessage(nameof(LocalizationService.StatusGitHubRefreshFailed), ex.Message);
        }
        finally
        {
            IsGitHubAuthenticating = false;
            RaiseGitHubCommandStateChanged();
        }
    }

    private async Task SignOutGitHubAsync()
    {
        await _githubAuthService.SignOutAsync();
        UpdateGitHubAuthState();
        SetStatusMessage(nameof(LocalizationService.StatusGitHubSignedOut));
    }

    private void UpdateGitHubAuthState(GitHubAuthSession? session = null)
    {
        session ??= _githubAuthService.CurrentSession;

        if (session is null)
        {
            SetGitHubAuthStatus(nameof(LocalizationService.GitHubAuthNotConnected));
            SetGitHubAccountSummary(nameof(LocalizationService.GitHubAuthNoAccountLinked));
        }
        else
        {
            SetGitHubAuthStatus(nameof(LocalizationService.GitHubAuthConnected), session.Summary);
            SetGitHubAccountSummary(nameof(LocalizationService.GitHubAuthLinkedAccount), session.DisplayName, session.Login);
        }

        OnPropertyChanged(nameof(IsGitHubConnected));
        RaiseGitHubCommandStateChanged();
    }

    private void RaiseGitHubCommandStateChanged()
    {
        (GitHubSignInCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GitHubSignOutCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (GitHubRefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ExecuteRefreshColors()
    {
        _ = RefreshColorsAsync();
    }


    private async Task RefreshTrendingRepositoriesAsync()
    {
        try
        {
            var sinceValues = GetSinceValues();
            var languages = GetSelectedLanguageFilters();

            Log.Information("Trending refresh start since=[{Since}] languages=[{Languages}]",
                string.Join(',', sinceValues),
                string.Join(',', languages.Select(x => x ?? "<all>")));

            var fetchTasks = sinceValues
                .SelectMany(since => languages.Select(language => GithubTrendingService.FetchAsync(
                    force: false,
                    since: since,
                    language: language)))
                .ToArray();

            Log.Information("Trending planned requests: {Count}", fetchTasks.Length);

            var results = await Task.WhenAll(fetchTasks);
            Log.Information("Trending completed requests: {Count}", results.Length);

            var merged = MergeTrendingResults(results);
            var visualRepositories = ApplyLanguageBrushes(merged);
            _trendingData = visualRepositories;

            Log.Information("Trending merged repos: {Count}", merged.Count);

            _trendingRepositories.Clear();
            foreach (var repo in visualRepositories)
            {
                _trendingRepositories.Add(repo);
            }

            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Trending loading failed");
        }
    }

    private IReadOnlyList<string?> GetSinceValues()
    {
        var selected = SelectedTimeRangeQuery;
        return selected == "all"
            ? new[] { "daily", "weekly", "monthly" }
            : new[] { selected };
    }

    private IReadOnlyList<string?> GetSelectedLanguageFilters()
    {
        var languages = _selectedLanguages
            .Select(language => language.Language)
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return languages.Count == 0
            ? new string?[] { null }
            : languages.Cast<string?>().ToArray();
    }


    private static List<GithubTrendingRepository> MergeTrendingResults(IEnumerable<IEnumerable<GithubTrendingRepository>> results)
    {
        var merged = new Dictionary<string, GithubTrendingRepository>(StringComparer.OrdinalIgnoreCase);

        foreach (var repo in results.SelectMany(result => result))
        {
            var key = !string.IsNullOrWhiteSpace(repo.Repository)
                ? repo.Repository!
                : !string.IsNullOrWhiteSpace(repo.Name)
                    ? repo.Name!
                    : Guid.NewGuid().ToString("N");

            if (!merged.ContainsKey(key))
            {
                merged[key] = repo;
            }
        }

        return merged.Values
            .OrderByDescending(repo => ParseStars(repo.Stars))
            .ThenBy(repo => repo.Repository ?? repo.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int ParseStars(string? value)
        => int.TryParse(value?.Replace(",", string.Empty), out var parsed) ? parsed : 0;

    private List<GithubTrendingRepository> ApplyLanguageBrushes(IEnumerable<GithubTrendingRepository> repositories)
    {
        var defaultBrush = new SolidColorBrush(Color.Parse("#FF3B82F6"));

        return repositories.Select(repository =>
        {
            if (GithubColors?.Colors != null
                && !string.IsNullOrWhiteSpace(repository.Language)
                && GithubColors.Colors.TryGetValue(repository.Language, out var colorEntry)
                && !string.IsNullOrWhiteSpace(colorEntry.Color)
                && Color.TryParse(colorEntry.Color, out var color))
            {
                return repository.CloneWith(languageBrush: new SolidColorBrush(color));
            }

            return repository.CloneWith(languageBrush: defaultBrush);
        }).ToList();
    }


    private async Task RefreshColorsAsync()
    {
        if (_isInitializing || _isRefreshing)
            return;

        try
        {
            _isRefreshing = true;
            SetStatusMessage(nameof(LocalizationService.StatusColorsRefreshing));
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();

            var newCatalog = await GithubColorsService.FetchAsync(force: true);
            GithubColors = newCatalog;

            // preserve current selections
            var currentSelected = _allLanguages.Where(l => l.IsSelected).Select(l => l.Language).ToArray();
            RebuildLanguages(GithubColors, currentSelected);
            RefreshSelectedLanguages();
            ApplyFilter();

            if (_trendingRepositories.Count > 0)
            {
                var refreshedRepositories = ApplyLanguageBrushes(_trendingRepositories).ToList();
                _trendingRepositories.Clear();

                foreach (var repo in refreshedRepositories)
                {
                    _trendingRepositories.Add(repo);
                }
            }

            SetStatusMessage(nameof(LocalizationService.StatusColorsRefreshed), ColorCount);
            OnPropertyChanged(nameof(ColorCount));
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(SelectedCount));
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetStatusMessage(nameof(LocalizationService.StatusColorsRefreshError), ex.Message);
        }
        finally
        {
            _isRefreshing = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void RebuildLanguages(GithubColorsCatalog githubColors, IReadOnlyCollection<string> selectedLanguages)
    {
        _allLanguages.Clear();

        var selectedSet = new HashSet<string>(selectedLanguages, StringComparer.OrdinalIgnoreCase);

        foreach (var language in githubColors.Colors.Keys
                     .Where(language => !string.IsNullOrWhiteSpace(language))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(language => language, StringComparer.OrdinalIgnoreCase))
        {
            _allLanguages.Add(new LanguageOptionViewModel(
                language,
                githubColors.Colors.TryGetValue(language, out var colorEntry) ? colorEntry.Color : null,
                selectedSet.Contains(language),
                OnLanguageSelectionChanged));
        }
    }

    private void OnLanguageSelectionChanged()
    {
        if (_isInitializing)
        {
            return;
        }

        RefreshSelectedLanguages();
        _ = PersistSelectionsAsync();
    }

    private void RefreshSelectedLanguages()
    {
        _selectedLanguages.Clear();

        foreach (var language in _allLanguages
                     .Where(language => language.IsSelected)
                     .OrderBy(language => language.Language, StringComparer.OrdinalIgnoreCase))
        {
            _selectedLanguages.Add(language);
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));

        if (!_isInitializing)
        {
            Log.Information("Trending selection changed -> refresh queued");
            _ = RefreshTrendingRepositoriesAsync();
        }
    }

    private async Task PersistSelectionsAsync()
    {
        try
        {
            await _selectedLanguagesStore.SaveAsync(_allLanguages.Where(language => language.IsSelected).Select(language => language.Language));
            SetStatusMessage(nameof(LocalizationService.StatusSelectionSaved), _selectedLanguages.Count);
            OnPropertyChanged(nameof(SelectionSummary));
        }
        catch (Exception ex)
        {
            SetStatusMessage(nameof(LocalizationService.StatusSelectionSaveError), ex.Message);
        }
    }

    private void ApplyFilter()
    {
        var search = _searchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allLanguages
            : _allLanguages.Where(language => language.Language.Contains(search, StringComparison.OrdinalIgnoreCase));

        var ordered = filtered
            .OrderByDescending(language => language.IsSelected)
            .ThenBy(language => language.Language, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _filteredLanguages.Clear();

        foreach (var language in ordered)
        {
            _filteredLanguages.Add(language);
        }

        OnPropertyChanged(nameof(FilteredLanguages));
        OnPropertyChanged(nameof(MatchingLanguagesLabel));
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void ExecuteOpenRepository(object? parameter)
    {
        if (parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.RepositoryLink))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = repo.RepositoryLink,
                    UseShellExecute = true
                });
                Log.Information("Opened repository: {Url}", repo.RepositoryLink);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open repository: {Url}", repo.RepositoryLink);
                SetStatusMessage(nameof(LocalizationService.OpenRepositoryFailure));
            }
        }
    }

    private async Task ExecuteStarRepositoryAsync(object? parameter)
    {
        if (parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.Repository))
        {
            if (!IsGitHubConnected)
            {
                SetStatusMessage(nameof(LocalizationService.ConnectGitHubToStar));
                return;
            }

            try
            {
                var apiClient = new GitHubApiClient(_githubAuthService);
                var slug = GetRepositorySlug(repo.Repository);
                var response = await apiClient.SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"{Constants.GitHub.ApiBaseUrl}/user/starred/{slug}"));

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to star repository {Slug}. Status={StatusCode} Body={Body}", slug, (int)response.StatusCode, error);
                    SetStatusMessageFromTemplate(BuildRepoActionFailureMessage(Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.ActionStar)), repo.DisplayTitle, response.StatusCode, error));
                    return;
                }

                SetStatusMessage(nameof(LocalizationService.StarRepositorySuccess), repo.DisplayTitle);
                Log.Information("Starred repository on GitHub: {Slug}", slug);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to star repository: {Repo}", repo.Repository);
                SetStatusMessage(nameof(LocalizationService.StarRepositoryFailure));
            }
        }
    }

    private async Task ExecuteWatchRepositoryAsync(object? parameter)
    {
        if (parameter is GithubTrendingRepository repo && !string.IsNullOrWhiteSpace(repo.Repository))
        {
            if (!IsGitHubConnected)
            {
                SetStatusMessage(nameof(LocalizationService.ConnectGitHubToWatch));
                return;
            }

            try
            {
                var apiClient = new GitHubApiClient(_githubAuthService);
                var slug = GetRepositorySlug(repo.Repository);
                var response = await apiClient.SendAsync(() => new HttpRequestMessage(HttpMethod.Put, $"{Constants.GitHub.ApiBaseUrl}/repos/{slug}/subscription")
                {
                    Content = new StringContent("{\"subscribed\":true,\"ignored\":false}", System.Text.Encoding.UTF8, "application/json")
                });

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Log.Warning("Failed to watch repository {Slug}. Status={StatusCode} Body={Body}", slug, (int)response.StatusCode, error);
                    SetStatusMessageFromTemplate(BuildRepoActionFailureMessage(Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.ActionWatch)), repo.DisplayTitle, response.StatusCode, error));
                    return;
                }

                SetStatusMessage(nameof(LocalizationService.WatchRepositorySuccess), repo.DisplayTitle);
                Log.Information("Watched repository on GitHub: {Slug}", slug);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to watch repository: {Repo}", repo.Repository);
                SetStatusMessage(nameof(LocalizationService.WatchRepositoryFailure));
            }
        }
    }

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

    private static string BuildRepoActionFailureMessage(string action, string repoName, System.Net.HttpStatusCode statusCode, string errorBody)
    {
        if ((statusCode == System.Net.HttpStatusCode.Forbidden || statusCode == System.Net.HttpStatusCode.NotFound)
            && errorBody.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase))
        {
            return Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.RepoActionBlockedByIntegration), action, repoName);
        }

        if (string.Equals(action, Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.ActionWatch)), StringComparison.OrdinalIgnoreCase)
            && statusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.RepoActionWatchRequiresNotificationsScope), repoName);
        }

        return Github_Trend.Localization.Localization.Instance.GetString(nameof(LocalizationService.RepoActionFailedHttp), action, repoName, (int)statusCode);
    }

    public void NotifyGitHubCodeCopied()
    {
        SetStatusMessage(nameof(LocalizationService.GitHubDeviceCodeCopied));
    }

    private void SetStatusMessage(string resourceKey, params object?[] args)
    {
        _statusMessageOverride = null;
        _statusMessageKey = resourceKey;
        _statusMessageArgs = args;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetStatusMessageFromTemplate(string message)
    {
        _statusMessageOverride = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetGitHubAuthStatus(string resourceKey, params object?[] args)
    {
        _githubAuthStatusOverride = null;
        _githubAuthStatusKey = resourceKey;
        _githubAuthStatusArgs = args;
        OnPropertyChanged(nameof(GitHubAuthStatus));
    }

    private void SetGitHubAuthStatusRaw(string message)
    {
        _githubAuthStatusOverride = message;
        OnPropertyChanged(nameof(GitHubAuthStatus));
    }

    private void ClearGitHubAuthStatusOverride()
    {
        if (_githubAuthStatusOverride is null)
        {
            return;
        }

        _githubAuthStatusOverride = null;
        OnPropertyChanged(nameof(GitHubAuthStatus));
    }

    private void SetGitHubAccountSummary(string resourceKey, params object?[] args)
    {
        _githubAccountSummaryKey = resourceKey;
        _githubAccountSummaryArgs = args;
        OnPropertyChanged(nameof(GitHubAccountSummary));
    }

    private void LoadDebugInfo()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DEBUG INFORMATION ===");
            sb.AppendLine();
            
            // OS Info
            sb.AppendLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}");
            sb.AppendLine();
            
            // .NET Info
            sb.AppendLine($".NET Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            
            // ProcessorAffinity only available on Windows/Linux
            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                try
                {
                    sb.AppendLine($"Process Architecture: {Process.GetCurrentProcess().ProcessorAffinity}");
                }
                catch
                {
                    sb.AppendLine("Process Architecture: N/A");
                }
            }
            sb.AppendLine();
            
            // App Info
            sb.AppendLine($"Application Path: {AppContext.BaseDirectory}");
            sb.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine();
            
            // Environment
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"Available Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            
            DebugInfo = sb.ToString();
            
            // Load logs
            var logDir = Path.Combine(AppContext.BaseDirectory, Constants.Logging.LogDirectoryName);
            if (Directory.Exists(logDir))
            {
                var logFiles = Directory.GetFiles(logDir, "*.log").OrderByDescending(f => f).ToList();
                if (logFiles.Any())
                {
                    var latestLog = logFiles.First();
                    try
                    {
                        AppLogs = File.ReadAllText(latestLog);
                    }
                    catch
                    {
                        AppLogs = "Unable to read log file.";
                    }
                }
                else
                {
                    AppLogs = "No log files found.";
                }
            }
            else
            {
                AppLogs = "Log directory not found.";
            }
        }
        catch (Exception ex)
        {
            DebugInfo = $"Error loading debug info: {ex.Message}";
            AppLogs = $"Error loading logs: {ex.Message}";
        }
    }
}
