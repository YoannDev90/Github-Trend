using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    // Constructor to initialize commands
    public MainWindowViewModel()
    {
        RefreshCommand = new RelayCommand(async _ => await RefreshColorsAsync(), _ => !_isInitializing && !_isRefreshing);
    }

    private bool _isRefreshing;
    private readonly SelectedLanguagesStore _selectedLanguagesStore = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _filteredLanguages = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _selectedLanguages = new();
    private readonly List<LanguageOptionViewModel> _allLanguages = new();
    private bool _isInitializing;
    private GithubColorsCatalog? _githubColors;
    private string _searchText = string.Empty;
    private string _statusMessage = "Chargement des couleurs...";

    private readonly ObservableCollection<GithubTrendingRepository> _trendingRepositories = new();
    private List<GithubTrendingRepository>? _trendingData;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand RefreshCommand { get; }

    public ObservableCollection<GithubTrendingRepository> TrendingRepositories => _trendingRepositories;

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
        }
        // Load trending repos in background (non-blocking)
        _ = LoadTrendingRepositoriesAsync();
    }

    private async Task LoadTrendingRepositoriesAsync()
    {
        try
        {
            var trending = await GithubTrendingService.FetchAsync();
            _trendingData = trending;

            _trendingRepositories.Clear();
            foreach (var repo in trending.Take(10))
            {
                _trendingRepositories.Add(repo);
            }

            OnPropertyChanged(nameof(TrendingCount));
            OnPropertyChanged(nameof(TrendingLabel));
        }
         catch (Exception ex)
         {
             // Log silently or optionally update status
             System.Diagnostics.Debug.WriteLine($"Erreur chargement trending: {ex.Message}");
         }
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

