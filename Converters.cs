using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Github_Trend;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    // Pre-cached brushes for dark theme
    private static readonly SolidColorBrush DarkErrBrush = new(Color.Parse("#F85149"));
    private static readonly SolidColorBrush DarkWrnBrush = new(Color.Parse("#D29922"));
    private static readonly SolidColorBrush DarkInfBrush = new(Color.Parse("#58A6FF"));
    private static readonly SolidColorBrush DarkDgbBrush = new(Color.Parse("#8B949E"));
    private static readonly SolidColorBrush DarkVrbBrush = new(Color.Parse("#6E7681"));

    // Pre-cached brushes for light theme
    private static readonly SolidColorBrush LightErrBrush = new(Color.Parse("#CF2222"));
    private static readonly SolidColorBrush LightWrnBrush = new(Color.Parse("#9A6700"));
    private static readonly SolidColorBrush LightInfBrush = new(Color.Parse("#0969DA"));
    private static readonly SolidColorBrush LightDgbBrush = new(Color.Parse("#656D76"));
    private static readonly SolidColorBrush LightVrbBrush = new(Color.Parse("#8C959F"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var theme = Application.Current?.ActualThemeVariant;
        var isDark = theme == ThemeVariant.Dark;

        return (value?.ToString()?.ToUpperInvariant(), isDark) switch
        {
            ("ERR" or "FTL", true) => DarkErrBrush,
            ("WRN", true) => DarkWrnBrush,
            ("INF", true) => DarkInfBrush,
            ("DBG", true) => DarkDgbBrush,
            ("VRB", true) => DarkVrbBrush,
            ("ERR" or "FTL", false) => LightErrBrush,
            ("WRN", false) => LightWrnBrush,
            ("INF", false) => LightInfBrush,
            ("DBG" or "VRB", false) => LightDgbBrush,
            _ => isDark ? DarkDgbBrush : LightDgbBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class BoolToStarredBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var theme = Application.Current?.ActualThemeVariant;
        if (value is true && Application.Current?.Resources.TryGetResource("StarActiveBrush", theme, out var active) == true)
            return active;
        if (Application.Current?.Resources.TryGetResource("BackgroundTertiaryBrush", theme, out var def) == true)
            return def;
        return new SolidColorBrush(Color.Parse("#FF21262D"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class BoolToWatchedBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var theme = Application.Current?.ActualThemeVariant;
        if (value is true && Application.Current?.Resources.TryGetResource("WatchActiveBrush", theme, out var active) == true)
            return active;
        if (Application.Current?.Resources.TryGetResource("BackgroundTertiaryBrush", theme, out var def) == true)
            return def;
        return new SolidColorBrush(Color.Parse("#FF21262D"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
