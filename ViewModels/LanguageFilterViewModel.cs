using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Github_Trend.Localization;
using Serilog;

namespace Github_Trend;

public sealed class LanguageFilterViewModel : INotifyPropertyChanged
{
    private System.Collections.Generic.HashSet<string> _popularLanguages = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly List<LanguageOptionViewModel> _allLanguages = new();
    private readonly SelectedLanguagesStore _store;
    private readonly Action<string, object?[]> _setStatusMessage;
    private readonly Action _onSelectionChanged;

    private bool _isInitializing;
    private bool _isBatchUpdating;
    private string _searchText = string.Empty;

    public LanguageFilterViewModel(
        SelectedLanguagesStore store,
        Action<string, object?[]> setStatusMessage,
        Action onSelectionChanged
    )
    {
        _store = store;
        _setStatusMessage = setStatusMessage;
        _onSelectionChanged = onSelectionChanged;

        SelectAllCommand = new RelayCommand(_ => SelectAllFiltered());
        DeselectAllCommand = new RelayCommand(_ => DeselectAllFiltered());
    }

    public ICommand SelectAllCommand { get; }
    public ICommand DeselectAllCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LanguageOptionViewModel> FilteredLanguages { get; } = new();
    public ObservableCollection<LanguageOptionViewModel> SelectedLanguages { get; } = new();

    public GithubColorsCatalog? Colors { get; private set; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public int ColorCount => Colors?.Colors.Count ?? 0;
    public int SelectedCount => SelectedLanguages.Count;
    public int VisibleCount => FilteredLanguages.Count;

    public IReadOnlyList<string?> SelectedLanguageFilters
    {
        get
        {
            var languages = SelectedLanguages
                .Select(l => l.Language)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return languages.Count == 0
                ? new string?[] { null }
                : languages.Cast<string?>().ToArray();
        }
    }

    public string MatchingLanguagesLabel =>
        FilteredLanguages.Count switch
        {
            0 => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.NoLanguagesFound)
            ),
            1 => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.SuggestionCountOne)
            ),
            _ => Localization.Localization.Instance.GetString(
                nameof(LocalizationService.SuggestionCountMany),
                FilteredLanguages.Count
            ),
        };

    public string SelectionSummary =>
        SelectedLanguages.Count == 0
            ? Localization.Localization.Instance.GetString(
                nameof(LocalizationService.SelectionSummaryZero)
            )
        : SelectedLanguages.Count == 1
            ? Localization.Localization.Instance.GetString(
                nameof(LocalizationService.SelectionSummaryOne)
            )
        : Localization.Localization.Instance.GetString(
            nameof(LocalizationService.SelectionSummaryMany),
            SelectedLanguages.Count
        );

    public async Task LoadAsync(GithubColorsCatalog catalog)
    {
        _isInitializing = true;
        try
        {
            Colors = catalog;
            _popularLanguages = await PopularLanguagesService.GetPopularLanguagesAsync();
            var savedSelections = await _store.LoadAsync();
            RebuildLanguages(catalog, savedSelections);
            RefreshSelectedLanguages();
            ApplyFilter();
            OnPropertyChanged(nameof(ColorCount));
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(SelectionSummary));
        }
        finally
        {
            _isInitializing = false;
        }
    }

    public async void RefreshColors(GithubColorsCatalog catalog)
    {
        Colors = catalog;
        _popularLanguages = await PopularLanguagesService.GetPopularLanguagesAsync();
        var currentSelected = _allLanguages
            .Where(l => l.IsSelected)
            .Select(l => l.Language)
            .ToArray();
        RebuildLanguages(catalog, currentSelected);
        RefreshSelectedLanguages();
        ApplyFilter();
        OnPropertyChanged(nameof(ColorCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(SelectedCount));
    }

    private void RebuildLanguages(GithubColorsCatalog catalog, IReadOnlyCollection<string> selected)
    {
        _allLanguages.Clear();
        var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        var popular = _popularLanguages;

        foreach (var language in catalog.Colors.Keys
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => !popular.Contains(l))
            .ThenBy(l => l, StringComparer.OrdinalIgnoreCase))
        {
            _allLanguages.Add(new LanguageOptionViewModel(
                language,
                catalog.Colors.TryGetValue(language, out var entry) ? entry.Color : null,
                selectedSet.Contains(language),
                OnLanguageToggled
            ));
        }
    }

    private void OnLanguageToggled()
    {
        if (_isInitializing || _isBatchUpdating) return;
        RefreshSelectedLanguages();
        _ = PersistAsync();
    }

    private void SelectAllFiltered()
    {
        Log.Debug("LanguageFilter: SelectAll ({Count} languages)", FilteredLanguages.Count);
        _isBatchUpdating = true;
        foreach (var lang in FilteredLanguages)
            lang.IsSelected = true;
        _isBatchUpdating = false;
        RefreshSelectedLanguages();
        _ = PersistAsync();
    }

    private void DeselectAllFiltered()
    {
        Log.Debug("LanguageFilter: DeselectAll ({Count} languages)", FilteredLanguages.Count);
        _isBatchUpdating = true;
        foreach (var lang in FilteredLanguages)
            lang.IsSelected = false;
        _isBatchUpdating = false;
        RefreshSelectedLanguages();
        _ = PersistAsync();
    }

    private void RefreshSelectedLanguages()
    {
        SelectedLanguages.Clear();
        foreach (var lang in _allLanguages
            .Where(l => l.IsSelected)
            .OrderBy(l => l.Language, StringComparer.OrdinalIgnoreCase))
            SelectedLanguages.Add(lang);

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));

        if (!_isInitializing)
        {
            Log.Information("Language selection changed -> refresh queued");
            _onSelectionChanged();
        }
    }

    private async Task PersistAsync()
    {
        try
        {
            await _store.SaveAsync(
                _allLanguages.Where(l => l.IsSelected).Select(l => l.Language)
            );
            _setStatusMessage(
                nameof(LocalizationService.StatusSelectionSaved),
                new object?[] { SelectedLanguages.Count }
            );
            OnPropertyChanged(nameof(SelectionSummary));
        }
        catch (Exception ex)
        {
            _setStatusMessage(
                nameof(LocalizationService.StatusSelectionSaveError),
                new object?[] { ex.Message }
            );
        }
    }

    private void ApplyFilter()
    {
        var search = _searchText.Trim();
        var filtered = string.IsNullOrWhiteSpace(search)
            ? _allLanguages
            : _allLanguages.Where(l =>
                l.Language.Contains(search, StringComparison.OrdinalIgnoreCase));

        FilteredLanguages.Clear();
        foreach (var lang in filtered
            .OrderByDescending(l => l.IsSelected)
            .ThenBy(l => !_popularLanguages.Contains(l.Language))
            .ThenBy(l => l.Language, StringComparer.OrdinalIgnoreCase))
            FilteredLanguages.Add(lang);

        OnPropertyChanged(nameof(FilteredLanguages));
        OnPropertyChanged(nameof(MatchingLanguagesLabel));
        OnPropertyChanged(nameof(VisibleCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
