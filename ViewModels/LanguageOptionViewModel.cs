using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Github_Trend;

public sealed class LanguageOptionViewModel : INotifyPropertyChanged
{
    private readonly Action<LanguageOptionViewModel>? _selectionChanged;
    private bool _isSelected;

    public LanguageOptionViewModel(string language, bool isSelected, Action<LanguageOptionViewModel>? selectionChanged)
    {
        Language = language;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _selectionChanged?.Invoke(this);
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

