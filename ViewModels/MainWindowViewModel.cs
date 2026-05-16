using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly (string Query, string Label)[] TimeRanges =
    {
        ("daily", "Daily"),
        ("weekly", "Weekly"),
        ("monthly", "Monthly"),
        ("all", "All")
    };

    // Constructor to initialize commands
    public MainWindowViewModel()
    {
        RefreshCommand = new RelayCommand(_ => ExecuteRefreshColors(), _ => !_isInitializing && !_isRefreshing);
        GitHubSignInCommand = new RelayCommand(_ => _ = SignInWithGitHubAsync(), _ => !_isInitializing && !_isGitHubAuthenticating);
        GitHubSignOutCommand = new RelayCommand(_ => _ = SignOutGitHubAsync(), _ => IsGitHubConnected && !_isGitHubAuthenticating);
        GitHubRefreshCommand = new RelayCommand(_ => _ = RefreshGitHubSessionAsync(), _ => IsGitHubConnected && !_isGitHubAuthenticating);
        _githubAuthService.SessionChanged += (_, _) => UpdateGitHubAuthState();
        _selectedTimeRangeIndex = 0;
        _githubAuthStatus = "Connexion GitHub non configurée";
        _githubAccountSummary = "Aucun compte connecté";
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
    private string _statusMessage = "Chargement des couleurs...";
    private string _githubAuthStatus;
    private string _githubAccountSummary;

    private readonly ObservableCollection<GithubTrendingRepository> _trendingRepositories = new();
    private List<GithubTrendingRepository>? _trendingData;

    private int _selectedTimeRangeIndex;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ICommand GitHubSignInCommand { get; }

    public ICommand GitHubSignOutCommand { get; }

    public ICommand GitHubRefreshCommand { get; }


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
            Console.WriteLine($"[Trending] timerange changed => {SelectedTimeRangeLabel}");
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

    public string SelectedTimeRangeLabel => TimeRanges[Math.Max(0, Math.Min(_selectedTimeRangeIndex, TimeRanges.Length - 1))].Label;

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

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

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

    public string GitHubAuthStatus
    {
        get => _githubAuthStatus;
        private set
        {
            if (_githubAuthStatus == value)
            {
                return;
            }

            _githubAuthStatus = value;
            OnPropertyChanged();
        }
    }

    public string GitHubAccountSummary
    {
        get => _githubAccountSummary;
        private set
        {
            if (_githubAccountSummary == value)
            {
                return;
            }

            _githubAccountSummary = value;
            OnPropertyChanged();
        }
    }

    public int ColorCount => GithubColors?.Colors.Count ?? 0;

    public int SelectedCount => _selectedLanguages.Count;

    public int VisibleCount => _filteredLanguages.Count;

    public string MatchingLanguagesLabel => _filteredLanguages.Count == 0
        ? "Aucun langage trouvé"
        : $"{_filteredLanguages.Count} suggestion(s)";

    public string SelectionSummary => _selectedLanguages.Count == 0
        ? "Aucune sélection enregistrée"
        : $"{_selectedLanguages.Count} langage(s) choisi(s)";

    public int TrendingCount => _trendingData?.Count ?? 0;

    public string TrendingLabel => TrendingCount == 0 ? "Aucun repo trending" : $"{TrendingCount} repo(s) trending";

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
            StatusMessage = $"Couleurs chargées: {ColorCount}";
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(VisibleCount));
            await _githubAuthService.InitializeAsync();
            UpdateGitHubAuthState();
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur chargement couleurs: {ex.Message}";
        }
        finally
        {
            _isInitializing = false;
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
            RaiseGitHubCommandStateChanged();
        }
        Console.WriteLine("[Trending] initial trending refresh queued");
        _ = RefreshTrendingRepositoriesAsync();
    }

    private async Task SignInWithGitHubAsync()
    {
        if (_isInitializing || _isGitHubAuthenticating)
        {
            return;
        }

        try
        {
            IsGitHubAuthenticating = true;
            GitHubAuthStatus = "Initialisation de la connexion GitHub...";
            var session = await _githubAuthService.BeginInteractiveSignInAsync(message => GitHubAuthStatus = message);
            UpdateGitHubAuthState(session);
            StatusMessage = $"Connexion GitHub réussie: {session?.Summary}";
        }
        catch (Exception ex)
        {
            GitHubAuthStatus = $"Connexion GitHub échouée: {ex.Message}";
            StatusMessage = GitHubAuthStatus;
        }
        finally
        {
            IsGitHubAuthenticating = false;
            RaiseGitHubCommandStateChanged();
        }
    }

    private async Task RefreshGitHubSessionAsync()
    {
        try
        {
            IsGitHubAuthenticating = true;
            GitHubAuthStatus = "Rafraîchissement de la session GitHub...";
            await _githubAuthService.RefreshCurrentAsync();
            UpdateGitHubAuthState();
            StatusMessage = GitHubAuthStatus;
        }
        catch (Exception ex)
        {
            GitHubAuthStatus = $"Rafraîchissement GitHub impossible: {ex.Message}";
            StatusMessage = GitHubAuthStatus;
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
        StatusMessage = "Session GitHub supprimée.";
    }

    private void UpdateGitHubAuthState(GitHubAuthSession? session = null)
    {
        session ??= _githubAuthService.CurrentSession;

        if (session is null)
        {
            GitHubAuthStatus = "Non connecté à GitHub";
            GitHubAccountSummary = "Aucun compte lié";
        }
        else
        {
            GitHubAuthStatus = $"Connecté à GitHub: {session.Summary}";
            GitHubAccountSummary = $"Compte lié: {session.DisplayName} ({session.Login})";
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

            Console.WriteLine($"[Trending] refresh start since=[{string.Join(',', sinceValues)}] languages=[{string.Join(',', languages.Select(x => x ?? "<all>"))}]");

            var fetchTasks = sinceValues
                .SelectMany(since => languages.Select(language => GithubTrendingService.FetchAsync(
                    force: false,
                    since: since,
                    language: language)))
                .ToArray();

            Console.WriteLine($"[Trending] planned requests: {fetchTasks.Length}");

            var results = await Task.WhenAll(fetchTasks);
            Console.WriteLine($"[Trending] completed requests: {results.Length}");

            var merged = MergeTrendingResults(results);
            var visualRepositories = ApplyLanguageBrushes(merged);
            _trendingData = visualRepositories;

            Console.WriteLine($"[Trending] merged repos: {merged.Count}");

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
            Console.WriteLine($"[Trending] error loading trending: {ex.Message}");
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
            StatusMessage = "Rafraîchissement des couleurs...";
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

            StatusMessage = $"Couleurs rafraîchies: {ColorCount}";
            OnPropertyChanged(nameof(ColorCount));
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(SelectedCount));
            (RefreshCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur rafraîchissement: {ex.Message}";
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
            Console.WriteLine("[Trending] selection changed -> refresh queued");
            _ = RefreshTrendingRepositoriesAsync();
        }
    }

    private async Task PersistSelectionsAsync()
    {
        try
        {
            await _selectedLanguagesStore.SaveAsync(_allLanguages.Where(language => language.IsSelected).Select(language => language.Language));
            StatusMessage = $"Sélection enregistrée: {_selectedLanguages.Count} langage(s)";
            OnPropertyChanged(nameof(SelectionSummary));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur sauvegarde sélection: {ex.Message}";
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
}

