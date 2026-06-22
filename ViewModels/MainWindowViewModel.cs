using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Github_Trend.Database;
using Github_Trend.Localization;
using Github_Trend.Services;
using Github_Trend.Services.GraphQL;
using Serilog;

namespace Github_Trend;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged, IAsyncDisposable
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
    private readonly GitHubApiClient _apiClient;

    private string? _statusMessageOverride;
    private string _statusMessageKey;
    private object?[] _statusMessageArgs = Array.Empty<object?>();

    private int _selectedTimeRangeIndex;
    private CancellationTokenSource? _trendingCts;
    private bool _isInitializing;
    private bool _isRefreshing;
    private bool _isTrendingLoading;

    private static readonly SolidColorBrush DefaultLanguageBrush = new(Color.Parse("#FF3B82F6"));

    public MainWindowViewModel()
    {
        _db = new AppDatabase();
        _rateLimitService = new GitHubRateLimitService();
        _authService = new GitHubAuthenticationService(GitHubAuthOptions.LoadFromConfig(), _db);
        _graphQlService = new GitHubGraphQlService(_authService, _rateLimitService);
        var detailsService = new GithubRepositoryDetailsService(_graphQlService, _authService, _rateLimitService, _db);
        _trendingService = new GithubTrendingService(_db, detailsService);
        _colorsService = new GithubColorsService(_db);
        _apiClient = new GitHubApiClient(_authService);

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

    public int TrendingCount => TrendingRepositories.Count;

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

            await _db.InitializeAsync();

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

    public async ValueTask DisposeAsync()
    {
        _trendingCts?.Cancel();
        _trendingCts?.Dispose();

        foreach (var repo in TrendingRepositories)
            repo.Dispose();

        if (_graphQlService is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();

        if (_trendingService is IAsyncDisposable trendingDisposable)
            await trendingDisposable.DisposeAsync();

        if (_colorsService is IAsyncDisposable colorsDisposable)
            await colorsDisposable.DisposeAsync();

        if (_apiClient is IDisposable apiDisposable)
            apiDisposable.Dispose();

        if (_authService is IDisposable authDisposable)
            authDisposable.Dispose();

        await _db.DisposeAsync();
    }
}
