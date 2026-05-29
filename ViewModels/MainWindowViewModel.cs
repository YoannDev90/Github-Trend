using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Github_Trend.Localization;
using Github_Trend.Services;
using Serilog;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly (string Query, string Label)[] TimeRanges =
    {
        ("daily", nameof(LocalizationService.TimeRangeDaily)),
        ("weekly", nameof(LocalizationService.TimeRangeWeekly)),
        ("monthly", nameof(LocalizationService.TimeRangeMonthly)),
        ("all", nameof(LocalizationService.TimeRangeAll)),
    };

    private readonly IGithubTrendingService _trendingService;
    private readonly IGithubColorsService _colorsService;

    private string? _statusMessageOverride;
    private string _statusMessageKey;
    private object?[] _statusMessageArgs = Array.Empty<object?>();

    private int _selectedTimeRangeIndex;
    private System.Collections.Generic.List<GithubTrendingRepository>? _trendingData;
    private CancellationTokenSource? _trendingCts;
    private bool _isInitializing;
    private bool _isRefreshing;

    private static readonly SolidColorBrush DefaultLanguageBrush = new(Color.Parse("#FF3B82F6"));

    public MainWindowViewModel()
        : this(new GithubTrendingServiceWrapper(), new GithubColorsServiceWrapper()) { }

    internal MainWindowViewModel(
        IGithubTrendingService trendingService,
        IGithubColorsService colorsService
    )
    {
        _trendingService = trendingService;
        _colorsService = colorsService;

        var authService = new GitHubAuthenticationService();
        Auth = new GitHubAuthViewModel(authService, SetStatusMessageFromKey);
        Filter = new LanguageFilterViewModel(
            new SelectedLanguagesStore(),
            SetStatusMessageFromKey,
            () => _ = RefreshTrendingRepositoriesAsync()
        );
        Debug = new DebugViewModel();

        _statusMessageKey = nameof(LocalizationService.StatusLoadingColors);
        _selectedTimeRangeIndex = 0;

        SelectDailyCommand = new RelayCommand(_ => IsDailySelected = true);
        SelectWeeklyCommand = new RelayCommand(_ => IsWeeklySelected = true);
        SelectMonthlyCommand = new RelayCommand(_ => IsMonthlySelected = true);
        SelectAllCommand = new RelayCommand(_ => IsAllSelected = true);

        RefreshCommand = new RelayCommand(
            _ => _ = RefreshColorsAsync(),
            _ => !_isInitializing && !_isRefreshing
        );

        OpenRepositoryCommand = new RelayCommand(
            ExecuteOpenRepository,
            p => p is GithubTrendingRepository r && !string.IsNullOrWhiteSpace(r.RepositoryLink)
        );
        StarRepositoryCommand = new RelayCommand(
            p => _ = ExecuteStarRepositoryAsync(p),
            p => p is GithubTrendingRepository r && !string.IsNullOrWhiteSpace(r.Repository)
        );
        WatchRepositoryCommand = new RelayCommand(
            p => _ = ExecuteWatchRepositoryAsync(p),
            p => p is GithubTrendingRepository r && !string.IsNullOrWhiteSpace(r.Repository)
        );

        Auth.DeviceCodeCopyRequested += (_, _) =>
            DeviceCodeCopyRequested?.Invoke(this, EventArgs.Empty);
        Debug.CopyLogsRequested += (_, _) => CopyLogsRequested?.Invoke(this, EventArgs.Empty);
    }

    public GitHubAuthViewModel Auth { get; }
    public LanguageFilterViewModel Filter { get; }
    public DebugViewModel Debug { get; }

    public ICommand SelectDailyCommand { get; }
    public ICommand SelectWeeklyCommand { get; }
    public ICommand SelectMonthlyCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand StarRepositoryCommand { get; }
    public ICommand WatchRepositoryCommand { get; }

    public event EventHandler? DeviceCodeCopyRequested;
    public event EventHandler? CopyLogsRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string StatusMessage =>
        _statusMessageOverride
        ?? Localization.Localization.Instance.GetString(_statusMessageKey, _statusMessageArgs);

    public ObservableCollection<GithubTrendingRepository> TrendingRepositories { get; } = new();

    public int TrendingCount => _trendingData?.Count ?? 0;

    public string TrendingLabel =>
        TrendingCount switch
        {
            0 => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.TrendingCountZero)
            ),
            1 => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.TrendingCountOne)
            ),
            _ => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.TrendingCountMany),
                TrendingCount
            ),
        };

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
                SelectedTimeRangeIndex = 0;
        }
    }
    public bool IsWeeklySelected
    {
        get => _selectedTimeRangeIndex == 1;
        set
        {
            if (value)
                SelectedTimeRangeIndex = 1;
        }
    }
    public bool IsMonthlySelected
    {
        get => _selectedTimeRangeIndex == 2;
        set
        {
            if (value)
                SelectedTimeRangeIndex = 2;
        }
    }
    public bool IsAllSelected
    {
        get => _selectedTimeRangeIndex == 3;
        set
        {
            if (value)
                SelectedTimeRangeIndex = 3;
        }
    }

    public string SelectedTimeRangeLabel =>
        Localization.Localization.Instance.GetString(
            TimeRanges[Math.Max(0, Math.Min(_selectedTimeRangeIndex, TimeRanges.Length - 1))].Label
        );

    public string SelectedTimeRangeQuery =>
        TimeRanges[Math.Max(0, Math.Min(_selectedTimeRangeIndex, TimeRanges.Length - 1))].Query;

    public async Task InitializeAsync()
    {
        try
        {
            _isInitializing = true;
            Auth.SetInitializing(true);

            var colors = await _colorsService.FetchAsync();
            await Filter.LoadAsync(colors);

            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsLoaded),
                new object?[] { Filter.ColorCount }
            );
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();

            await Auth.InitializeAsync();
        }
        catch (Exception ex)
        {
            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsLoadError),
                new object?[] { ex.Message }
            );
        }
        finally
        {
            _isInitializing = false;
            Auth.SetInitializing(false);
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            Auth.RaiseCommandStateChanged();
        }

        Log.Information("Initial trending refresh queued");
        _ = RefreshTrendingRepositoriesAsync();
    }

    private async Task RefreshTrendingRepositoriesAsync()
    {
        var previousCts = _trendingCts;
        var cts = new CancellationTokenSource();
        _trendingCts = cts;
        previousCts?.Cancel();
        previousCts?.Dispose();

        try
        {
            var sinceValues = GetSinceValues();
            var languages = Filter.SelectedLanguageFilters;

            Log.Information(
                "Trending refresh start since=[{Since}] languages=[{Languages}]",
                string.Join(',', sinceValues),
                string.Join(',', languages.Select(x => x ?? "<all>"))
            );

            TrendingRepositories.Clear();
            _trendingData = new System.Collections.Generic.List<GithubTrendingRepository>();
            var seen = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));

            var token = cts.Token;

            var streams = sinceValues
                .SelectMany(since =>
                    languages.Select(language =>
                        _trendingService.StreamAsync(false, since, language, token)
                    )
                )
                .ToList();

            var channel = Channel.CreateUnbounded<GithubTrendingRepository>();

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await Task.WhenAll(
                            streams.Select(async stream =>
                            {
                                await foreach (var repo in stream.WithCancellation(token))
                                    await channel.Writer.WriteAsync(repo, token);
                            })
                        );
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Debug("Trending refresh cancelled");
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                },
                token
            );

            await foreach (var repo in channel.Reader.ReadAllAsync(token))
            {
                var key =
                    !string.IsNullOrWhiteSpace(repo.Repository) ? repo.Repository!
                    : !string.IsNullOrWhiteSpace(repo.Name) ? repo.Name!
                    : null;

                if (key != null && !seen.Add(key))
                    continue;

                var visual = ApplyLanguageBrush(repo);
                _trendingData!.Add(visual);

                var insertIndex = FindSortedInsertIndex(visual);
                TrendingRepositories.Insert(insertIndex, visual);
                OnPropertyChanged(nameof(TrendingCount));
                OnPropertyChanged(nameof(TrendingLabel));
            }

            Log.Information("Trending stream complete: {Count}", _trendingData!.Count);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Trending refresh superseded by newer request");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Trending loading failed");
        }
        finally
        {
            if (ReferenceEquals(_trendingCts, cts))
            {
                cts.Dispose();
                _trendingCts = null;
            }
        }
    }

    private async Task RefreshColorsAsync()
    {
        if (_isInitializing || _isRefreshing)
            return;
        try
        {
            _isRefreshing = true;
            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshing),
                Array.Empty<object?>()
            );
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();

            var catalog = await _colorsService.FetchAsync(force: true);
            Filter.RefreshColors(catalog);

            if (TrendingRepositories.Count > 0)
            {
                var refreshed = TrendingRepositories.Select(ApplyLanguageBrush).ToList();
                TrendingRepositories.Clear();
                foreach (var repo in refreshed)
                    TrendingRepositories.Add(repo);
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshed),
                new object?[] { Filter.ColorCount }
            );
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            SetStatusMessageFromKey(
                nameof(LocalizationService.StatusColorsRefreshError),
                new object?[] { ex.Message }
            );
        }
        finally
        {
            _isRefreshing = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteOpenRepository(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.RepositoryLink)
        )
            return;
        try
        {
            Process.Start(
                new ProcessStartInfo { FileName = repo.RepositoryLink, UseShellExecute = true }
            );
            Log.Information("Opened repository: {Url}", repo.RepositoryLink);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open repository: {Url}", repo.RepositoryLink);
            SetStatusMessageFromKey(
                nameof(LocalizationService.OpenRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteStarRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
            return;

        if (!Auth.IsConnected)
        {
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToStar),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var apiClient = new GitHubApiClient(new GitHubAuthenticationService());
            var slug = GetRepositorySlug(repo.Repository);
            var response = await apiClient.SendAsync(() =>
                new HttpRequestMessage(
                    HttpMethod.Put,
                    $"{Constants.GitHub.ApiBaseUrl}/user/starred/{slug}"
                )
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Log.Warning(
                    "Failed to star {Slug}. Status={Status} Body={Body}",
                    slug,
                    (int)response.StatusCode,
                    error
                );
                SetStatusMessageRaw(
                    BuildRepoActionFailureMessage(
                        Localization.Localization.Instance.GetString(
                            nameof(LocalizationService.ActionStar)
                        ),
                        repo.DisplayTitle,
                        response.StatusCode,
                        error
                    )
                );
                return;
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.StarRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Starred repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to star repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.StarRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteWatchRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
            return;

        if (!Auth.IsConnected)
        {
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToWatch),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var apiClient = new GitHubApiClient(new GitHubAuthenticationService());
            var slug = GetRepositorySlug(repo.Repository);
            var response = await apiClient.SendAsync(() =>
                new HttpRequestMessage(
                    HttpMethod.Put,
                    $"{Constants.GitHub.ApiBaseUrl}/repos/{slug}/subscription"
                )
                {
                    Content = new StringContent(
                        "{\"subscribed\":true,\"ignored\":false}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Log.Warning(
                    "Failed to watch {Slug}. Status={Status} Body={Body}",
                    slug,
                    (int)response.StatusCode,
                    error
                );
                SetStatusMessageRaw(
                    BuildRepoActionFailureMessage(
                        Localization.Localization.Instance.GetString(
                            nameof(LocalizationService.ActionWatch)
                        ),
                        repo.DisplayTitle,
                        response.StatusCode,
                        error
                    )
                );
                return;
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.WatchRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Watched repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to watch repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.WatchRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private System.Collections.Generic.IReadOnlyList<string?> GetSinceValues()
    {
        var q = SelectedTimeRangeQuery;
        return q == "all" ? new[] { "daily", "weekly", "monthly" } : new[] { q };
    }

    private int FindSortedInsertIndex(GithubTrendingRepository repo)
    {
        var stars = ParseStars(repo.Stars);
        for (var i = 0; i < TrendingRepositories.Count; i++)
            if (ParseStars(TrendingRepositories[i].Stars) < stars)
                return i;
        return TrendingRepositories.Count;
    }

    private static int ParseStars(string? value) =>
        int.TryParse(value?.Replace(",", string.Empty), out var n) ? n : 0;

    private GithubTrendingRepository ApplyLanguageBrush(GithubTrendingRepository repo)
    {
        if (
            Filter.Colors?.Colors != null
            && !string.IsNullOrWhiteSpace(repo.Language)
            && Filter.Colors.Colors.TryGetValue(repo.Language, out var entry)
            && !string.IsNullOrWhiteSpace(entry.Color)
            && Color.TryParse(entry.Color, out var color)
        )
            return repo.CloneWith(languageBrush: new SolidColorBrush(color));

        return repo.CloneWith(languageBrush: DefaultLanguageBrush);
    }

    private void SetStatusMessageFromKey(string key, object?[] args)
    {
        _statusMessageOverride = null;
        _statusMessageKey = key;
        _statusMessageArgs = args;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetStatusMessageRaw(string message)
    {
        _statusMessageOverride = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private static string BuildRepoActionFailureMessage(
        string action,
        string repoName,
        HttpStatusCode statusCode,
        string errorBody
    )
    {
        if (
            (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.NotFound)
            && errorBody.Contains(
                "Resource not accessible by integration",
                StringComparison.OrdinalIgnoreCase
            )
        )
            return Localization.Localization.Instance.GetString(
                nameof(LocalizationService.RepoActionBlockedByIntegration),
                action,
                repoName
            );

        if (
            string.Equals(
                action,
                Localization.Localization.Instance.GetString(
                    nameof(LocalizationService.ActionWatch)
                ),
                StringComparison.OrdinalIgnoreCase
            )
            && statusCode == HttpStatusCode.NotFound
        )
            return Localization.Localization.Instance.GetString(
                nameof(LocalizationService.RepoActionWatchRequiresNotificationsScope),
                repoName
            );

        return Localization.Localization.Instance.GetString(
            nameof(LocalizationService.RepoActionFailedHttp),
            action,
            repoName,
            (int)statusCode
        );
    }

    private static string GetRepositorySlug(string url)
    {
        var t = url.Trim();
        if (t.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            var parts = t.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );
            if (parts.Length >= 2)
                return $"{parts[^2]}/{parts[^1]}";
        }
        return t;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
