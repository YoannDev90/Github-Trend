using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Github_Trend;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private GithubColorsCatalog? _githubColors;
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

    public async Task LoadGithubColorsAsync()
    {
        try
        {
            GithubColors = await GithubColorsService.FetchAsync();
            StatusMessage = $"Couleurs chargées: {ColorCount}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur chargement couleurs: {ex.Message}";
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

