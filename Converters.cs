using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace Github_Trend;

public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly Color DarkErr = Color.Parse("#F85149");
    private static readonly Color DarkWrn = Color.Parse("#D29922");
    private static readonly Color DarkInf = Color.Parse("#58A6FF");
    private static readonly Color DarkDgb = Color.Parse("#8B949E");
    private static readonly Color DarkVrb = Color.Parse("#6E7681");
    private static readonly Color LightErr = Color.Parse("#CF2222");
    private static readonly Color LightWrn = Color.Parse("#9A6700");
    private static readonly Color LightInf = Color.Parse("#0969DA");
    private static readonly Color LightDgb = Color.Parse("#656D76");
    private static readonly Color LightVrb = Color.Parse("#8C959F");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

        var color = (value?.ToString()?.ToUpperInvariant(), isDark) switch
        {
            ("ERR" or "FTL", true) => DarkErr,
            ("WRN", true) => DarkWrn,
            ("INF", true) => DarkInf,
            ("DBG", true) => DarkDgb,
            ("VRB", true) => DarkVrb,
            ("ERR" or "FTL", false) => LightErr,
            ("WRN", false) => LightWrn,
            ("INF", false) => LightInf,
            ("DBG" or "VRB", false) => LightDgb,
            _ => isDark ? DarkDgb : LightDgb,
        };
        return new SolidColorBrush(color);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class BoolToStarredBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ConvertBoolToBrush(value, "StarActiveBrush");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();

    internal static object? ConvertBoolToBrush(object? value, string activeKey)
    {
        var theme = Application.Current?.ActualThemeVariant;
        if (value is true && Application.Current?.Resources.TryGetResource(activeKey, theme, out var active) == true)
            return active;
        object? def = null;
        Application.Current?.Resources.TryGetResource("BackgroundTertiaryBrush", theme, out def);
        return def ?? Brushes.Gray;
    }
}

public sealed class BoolToWatchedBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BoolToStarredBrushConverter.ConvertBoolToBrush(value, "WatchActiveBrush");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
