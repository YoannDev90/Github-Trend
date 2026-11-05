using System;

namespace Github_Trend;

public sealed class TimeRangeOptionViewModel : ViewModelBase
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
            if (SetProperty(ref _isSelected, value) && value)
                _onSelected?.Invoke(Index);
        }
    }
}