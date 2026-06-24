using System;
using Avalonia.Media;

namespace Github_Trend;

public sealed class LanguageOptionViewModel : ViewModelBase
{
    private static readonly IBrush DefaultAccentBrush = new SolidColorBrush(
        Color.Parse("#FF3B82F6")
    );
    private readonly Action? _selectionChanged;
    private bool _isSelected;

    public LanguageOptionViewModel(
        string language,
        string? accentColorHex,
        bool isSelected,
        Action? selectionChanged
    )
    {
        Language = language;
        AccentBrush = CreateBrush(accentColorHex);
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string Language { get; }

    public IBrush AccentBrush { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value, _selectionChanged);
    }

    private static IBrush CreateBrush(string? accentColorHex)
    {
        if (
            !string.IsNullOrWhiteSpace(accentColorHex)
            && Color.TryParse(accentColorHex, out var color)
        )
            return new SolidColorBrush(color);

        return DefaultAccentBrush;
    }

}
