using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Github_Trend;

public sealed class TimeRangeOptionViewModel : INotifyPropertyChanged
{
    private readonly Action<int>? _onSelected;
    private bool _isSelected;

    public TimeRangeOptionViewModel(int index, string label, Action<int>? onSelected)
    {
        Index = index;
        Label = label;
        _onSelected = onSelected;
    }

    public int Index { get; }

    public string Label { get; }


    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;

            _isSelected = value;
            OnPropertyChanged();

            if (value) _onSelected?.Invoke(Index);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}