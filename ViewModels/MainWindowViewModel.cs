using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly SelectedLanguagesStore _selectedLanguagesStore = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _filteredLanguages = new();
    private readonly ObservableCollection<LanguageOptionViewModel> _selectedLanguages = new();
    private readonly List<LanguageOptionViewModel> _allLanguages = new();
    private bool _isInitializing;
    private GithubColorsCatalog? _githubColors;
    private string _searchText = string.Empty;
    private string _statusMessage = "Chargement des couleurs...";

    public event PropertyChangedEventHandler? PropertyChanged;

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
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur chargement couleurs: {ex.Message}";
        }
        finally
        {
            _isInitializing = false;
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

