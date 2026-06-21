using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Github_Trend.Database;
using Github_Trend.Localization;
using Github_Trend.Services;
using Github_Trend.Services.GraphQL;
using Github_Trend.Utils;
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
    private readonly GitHubAuthenticationService _authService;
    private readonly GitHubGraphQlService _graphQlService;
    private readonly AppDatabase _db;
    private readonly GitHubRateLimitService _rateLimitService;

    private string? _statusMessageOverride;
    private string _statusMessageKey;
    private object?[] _statusMessageArgs = Array.Empty<object?>();

    private int _selectedTimeRangeIndex;
    private System.Collections.Generic.List<GithubTrendingRepository>? _trendingData;
    private CancellationTokenSource? _trendingCts;
    private bool _isInitializing;
    private bool _isRefreshing;
    private bool _isTrendingLoading;

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

        _db = new AppDatabase();
        _rateLimitService = new GitHubRateLimitService();
        _authService = new GitHubAuthenticationService(new GitHubAuthOptions(), _db);
        _graphQlService = new GitHubGraphQlService(_authService, _rateLimitService, _db);

        Auth = new GitHubAuthViewModel(_authService, SetStatusMessageFromKey);
        Filter = new LanguageFilterViewModel(
            new SelectedLanguagesStore(_db),
            SetStatusMessageFromKey,
            () => _ = RefreshTrendingRepositoriesAsync()
        );
        Debug = new DebugViewModel();

        _statusMessageKey = nameof(LocalizationService.StatusLoadingColors);
        _selectedTimeRangeIndex = 0;

        SelectDailyCommand = new RelayCommand(_ => { Log.Debug("TimeRange: Daily selected"); IsDailySelected = true; });
        SelectWeeklyCommand = new RelayCommand(_ => { Log.Debug("TimeRange: Weekly selected"); IsWeeklySelected = true; });
        SelectMonthlyCommand = new RelayCommand(_ => { Log.Debug("TimeRange: Monthly selected"); IsMonthlySelected = true; });
        SelectAllCommand = new RelayCommand(_ => { Log.Debug("TimeRange: All selected"); IsAllSelected = true; });

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
        SaveRepositoryCommand = new RelayCommand(
            p => _ = ExecuteSaveRepositoryAsync(p),
            p => p is GithubTrendingRepository r && !string.IsNullOrWhiteSpace(r.Repository)
        );
        DismissRepositoryCommand = new RelayCommand(
            p => _ = ExecuteDismissRepositoryAsync(p),
            p => p is GithubTrendingRepository r && !string.IsNullOrWhiteSpace(r.Repository)
        );
        ShowDismissedCommand = new RelayCommand(_ => _ = ExecuteShowDismissedAsync());
        UndismissAllCommand = new RelayCommand(_ => _ = ExecuteUndismissAllAsync());

        Auth.DeviceCodeCopyRequested += (_, _) =>
            DeviceCodeCopyRequested?.Invoke(this, EventArgs.Empty);
        Debug.CopyLogsRequested += (_, _) => CopyLogsRequested?.Invoke(this, EventArgs.Empty);
    }

    public GitHubAuthViewModel Auth { get; }
    public LanguageFilterViewModel Filter { get; }
    public DebugViewModel Debug { get; }

    public bool IsTrendingLoading
    {
        get => _isTrendingLoading;
        set
        {
            if (_isTrendingLoading == value) return;
            _isTrendingLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowTrendingLoading));
            OnPropertyChanged(nameof(ShowTrendingEmpty));
        }
    }

    public bool ShowTrendingContent => TrendingRepositories.Count > 0;
    public bool ShowTrendingLoading => _isTrendingLoading && TrendingRepositories.Count == 0;
    public bool ShowTrendingEmpty => TrendingRepositories.Count == 0 && !_isTrendingLoading;

    public ICommand SelectDailyCommand { get; }
    public ICommand SelectWeeklyCommand { get; }
    public ICommand SelectMonthlyCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand StarRepositoryCommand { get; }
    public ICommand WatchRepositoryCommand { get; }
    public ICommand SaveRepositoryCommand { get; }
    public ICommand DismissRepositoryCommand { get; }
    public ICommand ShowDismissedCommand { get; }
    public ICommand UndismissAllCommand { get; }

    public Func<Task<bool>>? ConfirmUnstarAsync { get; set; }
    public Func<Task<bool>>? ConfirmUnwatchAsync { get; set; }

    public Func<List<StarListNode>, Func<string, Task<StarListNode?>>, Task<List<string>?>>? ShowSaveToStarListDialogAsync { get; set; }

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
        set { if (value) SelectedTimeRangeIndex = 0; }
    }
    public bool IsWeeklySelected
    {
        get => _selectedTimeRangeIndex == 1;
        set { if (value) SelectedTimeRangeIndex = 1; }
    }
    public bool IsMonthlySelected
    {
        get => _selectedTimeRangeIndex == 2;
        set { if (value) SelectedTimeRangeIndex = 2; }
    }
    public bool IsAllSelected
    {
        get => _selectedTimeRangeIndex == 3;
        set { if (value) SelectedTimeRangeIndex = 3; }
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

            // Initialize database
            await _db.InitializeAsync();
            GithubTrendingService.SetDatabase(_db);
            GithubRepositoryDetailsService.Initialize(_graphQlService, _authService, _rateLimitService, _db);

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
            Log.Error(ex, "Initialization failed");
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

        IsTrendingLoading = true;

        try
        {
            var sinceValues = GetSinceValues();
            var languages = Filter.SelectedLanguageFilters;

            Log.Information(
                "Trending refresh start since=[{Since}] languages=[{Languages}]",
                string.Join(',', sinceValues),
                string.Join(',', languages.Select(x => x ?? "<all>"))
            );

            // Dispose old bitmaps before clearing
            foreach (var repo in TrendingRepositories)
                repo.Dispose();
            TrendingRepositories.Clear();
            OnPropertyChanged(nameof(ShowTrendingContent));
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

            var channel = Channel.CreateBounded<GithubTrendingRepository>(
                new BoundedChannelOptions(100)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    AllowSynchronousContinuations = false,
                }
            );

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

            // Load starred/watched/dismissed from DB + GraphQL
            var starredSlugs = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var watchedSlugs = new System.Collections.Generic.HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();

            if (Auth.IsConnected)
            {
                try
                {
                    // Load from DB first
                    starredSlugs = await _db.GetStarredSlugsAsync();
                    watchedSlugs = await _db.GetWatchedSlugsAsync();

                    // Sync from GitHub via GraphQL
                    var freshStarred = await _graphQlService.GetAllStarredSlugsAsync(token);
                    if (freshStarred.Count > 0)
                    {
                        starredSlugs = freshStarred;
                        await _db.SetStarredSlugsAsync(freshStarred);
                    }

                    // Watched still uses REST (no GraphQL support)
                    var freshWatched = await FetchWatchedSlugsFromRestAsync(token);
                    if (freshWatched.Count > 0)
                    {
                        watchedSlugs = freshWatched;
                        await _db.SetWatchedSlugsAsync(freshWatched);
                    }

                    Log.Information(
                        "Loaded {StarCount} starred and {WatchCount} watched repos",
                        starredSlugs.Count,
                        watchedSlugs.Count
                    );
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to fetch starred/watched repos");
                }
            }

            var batch = new System.Collections.Generic.List<GithubTrendingRepository>(
                Constants.Trending.MaxParallelEnrichmentRequests * 5
            );
            await foreach (var repo in channel.Reader.ReadAllAsync(token))
            {
                var key =
                    !string.IsNullOrWhiteSpace(repo.Repository) ? repo.Repository!
                    : !string.IsNullOrWhiteSpace(repo.Name) ? repo.Name!
                    : null;

                if (key != null && !seen.Add(key))
                    continue;

                var slug = RepositoryUrlParser.GetSlug(repo.Repository);
                if (!string.IsNullOrWhiteSpace(slug) && dismissedSlugs.Contains(slug))
                    continue;

                var visual = ApplyLanguageBrush(repo);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    visual.IsStarred = starredSlugs.Contains(slug);
                    visual.IsWatched = watchedSlugs.Contains(slug);
                    visual.IsDismissed = dismissedSlugs.Contains(slug);
                }
                _trendingData!.Add(visual);
                batch.Add(visual);

                // Flush batch when threshold reached
                if (batch.Count >= 10)
                {
                    FlushBatch(batch, TrendingRepositories);
                }
            }

            // Flush remaining batch
            if (batch.Count > 0)
                FlushBatch(batch, TrendingRepositories);

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
            IsTrendingLoading = false;
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingEmpty));
            if (ReferenceEquals(_trendingCts, cts))
            {
                cts.Dispose();
                _trendingCts = null;
            }
        }
    }

    private async Task RefreshColorsAsync()
    {
        Log.Debug("RefreshColors: starting");
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
            Log.Error(ex, "Colors refresh failed");
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
        {
            Log.Debug("OpenRepository: invalid parameter");
            return;
        }
        Log.Debug("OpenRepository: {Url}", repo.RepositoryLink);
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
        {
            Log.Debug("StarRepository: invalid parameter");
            return;
        }
        Log.Debug("StarRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("StarRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToStar),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            // Check current star status via GraphQL
            Log.Debug("StarRepository: checking current star status for {Owner}/{Name}", owner, name);
            var isStarred = await _graphQlService.IsStarredAsync(owner, name) ?? false;

            if (isStarred)
            {
                if (ConfirmUnstarAsync is not null)
                {
                    var confirmed = await ConfirmUnstarAsync();
                    if (!confirmed) return;
                }

                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: true);
                repo.IsStarred = false;
                RefreshRepoItem(repo);

                await _db.SetStarredAsync(slug, false);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.UnstarRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Unstarred repository: {Slug}", slug);
            }
            else
            {
                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: false);
                repo.IsStarred = true;
                RefreshRepoItem(repo);

                await _db.SetStarredAsync(slug, true);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.StarRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Starred repository: {Slug}", slug);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to star/unstar repository: {Repo}", repo.Repository);
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
        {
            Log.Debug("WatchRepository: invalid parameter");
            return;
        }
        Log.Debug("WatchRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("WatchRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToWatch),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            // Use local DB state for current watch status
            Log.Debug("WatchRepository: checking current watch status for {Slug}", slug);
            var isWatched = await _db.IsWatchedAsync(slug);

            if (isWatched)
            {
                if (ConfirmUnwatchAsync is not null)
                {
                    var confirmed = await ConfirmUnwatchAsync();
                    if (!confirmed) return;
                }

                await _graphQlService.ToggleWatchAsync(owner, name, currentlyWatched: true);
                repo.IsWatched = false;
                RefreshRepoItem(repo);
                await _db.SetWatchedAsync(slug, false);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.UnwatchRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Unwatched repository: {Slug}", slug);
            }
            else
            {
                await _graphQlService.ToggleWatchAsync(owner, name, currentlyWatched: false);
                repo.IsWatched = true;
                RefreshRepoItem(repo);
                await _db.SetWatchedAsync(slug, true);

                SetStatusMessageFromKey(
                    nameof(LocalizationService.WatchRepositorySuccess),
                    new object?[] { repo.DisplayTitle }
                );
                Log.Information("Watched repository: {Slug}", slug);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to watch/unwatch repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.WatchRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task<System.Collections.Generic.HashSet<string>> FetchWatchedSlugsFromRestAsync(
        CancellationToken ct
    )
    {
        var slugs = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var apiClient = new GitHubApiClient(_authService);
            slugs = await apiClient.GetWatchedRepositorySlugsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch watched repos from REST");
        }
        return slugs;
    }

    private async Task ExecuteSaveRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("SaveRepository: invalid parameter");
            return;
        }
        Log.Debug("SaveRepository: {Repo} starting", repo.Repository);

        if (!Auth.IsConnected)
        {
            Log.Debug("SaveRepository: not authenticated, aborting");
            SetStatusMessageFromKey(
                nameof(LocalizationService.ConnectGitHubToStar),
                Array.Empty<object?>()
            );
            return;
        }

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            if (!RepositoryUrlParser.TryParse(repo.Repository, out var owner, out var name))
            {
                Log.Warning("Cannot parse repository URL: {Repo}", repo.Repository);
                return;
            }

            Log.Debug("SaveRepository: checking star status for {Owner}/{Name}", owner, name);
            var isStarred = await _graphQlService.IsStarredAsync(owner, name) ?? false;

            if (!isStarred)
            {
                await _graphQlService.ToggleStarAsync(owner, name, currentlyStarred: false);
                repo.IsStarred = true;
                RefreshRepoItem(repo);
                await _db.SetStarredAsync(slug, true);
            }

            // Prompt user to select a star list
            if (ShowSaveToStarListDialogAsync is not null)
            {
                Log.Debug("SaveRepository: fetching star lists");
                var lists = await _graphQlService.GetStarListsAsync();
                var selectedListIds = await ShowSaveToStarListDialogAsync(
                    lists,
                    async name => await _graphQlService.CreateStarListAsync(name, null, false)
                );

                if (selectedListIds is { Count: > 0 })
                {
                    var nodeId = await _graphQlService.GetRepositoryNodeIdAsync(owner, name, CancellationToken.None);
                    if (nodeId is not null)
                    {
                        var existingIds = await _graphQlService.GetItemListMembershipsAsync(nodeId);
                        var mergedIds = existingIds.Union(selectedListIds).ToList();
                        await _graphQlService.AddRepositoryToStarListsAsync(nodeId, mergedIds);
                        Log.Information("Added {Slug} to {Count} star list(s)", slug, selectedListIds.Count);
                    }
                }
                else
                {
                    Log.Debug("SaveRepository: no list selected by user");
                }
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.SaveRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Saved repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.SaveRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteDismissRepositoryAsync(object? parameter)
    {
        if (
            parameter is not GithubTrendingRepository repo
            || string.IsNullOrWhiteSpace(repo.Repository)
        )
        {
            Log.Debug("DismissRepository: invalid parameter");
            return;
        }
        Log.Debug("DismissRepository: {Repo} starting", repo.Repository);

        try
        {
            var slug = RepositoryUrlParser.GetSlug(repo.Repository);
            Log.Debug("DismissRepository: dismissing {Slug}", slug);
            await _db.SetDismissedAsync(slug, true);
            repo.IsDismissed = true;

            TrendingRepositories.Remove(repo);
            _trendingData?.Remove(repo);

            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingEmpty));

            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissRepositorySuccess),
                new object?[] { repo.DisplayTitle }
            );
            Log.Information("Dismissed repository: {Slug}", slug);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to dismiss repository: {Repo}", repo.Repository);
            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissRepositoryFailure),
                Array.Empty<object?>()
            );
        }
    }

    private async Task ExecuteShowDismissedAsync()
    {
        Log.Debug("ShowDismissed: starting");
        try
        {
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();
            if (dismissedSlugs.Count == 0)
            {
                Log.Debug("ShowDismissed: no dismissed repos");
                SetStatusMessageFromKey(
                    nameof(LocalizationService.NoDismissedRepositories),
                    Array.Empty<object?>()
                );
                return;
            }

            SetStatusMessageFromKey(
                nameof(LocalizationService.DismissedCount),
                new object?[] { dismissedSlugs.Count }
            );
            Log.Information("Dismissed repos: {Count}", dismissedSlugs.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load dismissed repos");
        }
    }

    private async Task ExecuteUndismissAllAsync()
    {
        Log.Debug("UndismissAll: starting");
        try
        {
            var dismissedSlugs = await _db.GetDismissedSlugsAsync();
            Log.Debug("UndismissAll: clearing {Count} dismissed repos", dismissedSlugs.Count);
            foreach (var slug in dismissedSlugs)
                await _db.SetDismissedAsync(slug, false);

            SetStatusMessageFromKey(
                nameof(LocalizationService.AllDismissedCleared),
                new object?[] { dismissedSlugs.Count }
            );
            Log.Information("Undismissed all repos: {Count}", dismissedSlugs.Count);

            _ = RefreshTrendingRepositoriesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to undismiss all repos");
        }
    }

    private System.Collections.Generic.IReadOnlyList<string?> GetSinceValues()
    {
        var q = SelectedTimeRangeQuery;
        return q == "all" ? new[] { "daily", "weekly", "monthly" } : new[] { q };
    }

    private void FlushBatch(
        System.Collections.Generic.List<GithubTrendingRepository> batch,
        ObservableCollection<GithubTrendingRepository> target
    )
    {
        var wasEmpty = target.Count == 0;
        foreach (var item in batch)
            target.Add(item);
        batch.Clear();
        OnPropertyChanged(nameof(TrendingCount));
        OnPropertyChanged(nameof(TrendingLabel));
        if (wasEmpty)
        {
            OnPropertyChanged(nameof(ShowTrendingContent));
            OnPropertyChanged(nameof(ShowTrendingLoading));
        }
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

    private void RefreshRepoItem(GithubTrendingRepository repo)
    {
        var index = TrendingRepositories.IndexOf(repo);
        if (index < 0) return;
        TrendingRepositories.RemoveAt(index);
        TrendingRepositories.Insert(index, repo);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
