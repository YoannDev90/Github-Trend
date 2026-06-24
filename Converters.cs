using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    private static readonly SolidColorBrush DarkErrBrush = new(DarkErr);
    private static readonly SolidColorBrush DarkWrnBrush = new(DarkWrn);
    private static readonly SolidColorBrush DarkInfBrush = new(DarkInf);
    private static readonly SolidColorBrush DarkDgbBrush = new(DarkDgb);
    private static readonly SolidColorBrush DarkVrbBrush = new(DarkVrb);
    private static readonly SolidColorBrush LightErrBrush = new(LightErr);
    private static readonly SolidColorBrush LightWrnBrush = new(LightWrn);
    private static readonly SolidColorBrush LightInfBrush = new(LightInf);
    private static readonly SolidColorBrush LightDgbBrush = new(LightDgb);
    private static readonly SolidColorBrush LightVrbBrush = new(LightVrb);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

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

public sealed class LogLevelToDisplayNameConverter : IValueConverter
{
    private static readonly Dictionary<string, string> LevelNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TRC"] = "Trace",
        ["DBG"] = "Debug",
        ["INF"] = "Info",
        ["WRN"] = "Warning",
        ["ERR"] = "Error",
        ["FTL"] = "Fatal",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string level && LevelNames.TryGetValue(level, out var name))
            return name;
        return value?.ToString() ?? "";
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

public sealed class LogLevelToBackgroundConverter : IValueConverter
{
    private static readonly Color DarkErrBg = Color.Parse("#26F85149");
    private static readonly Color DarkWrnBg = Color.Parse("#26D29922");
    private static readonly Color DarkInfBg = Color.Parse("#2658A6FF");
    private static readonly Color DarkDgbBg = Color.Parse("#268B949E");
    private static readonly Color DarkTrcBg = Color.Parse("#266E7681");
    private static readonly Color LightErrBg = Color.Parse("#18CF2222");
    private static readonly Color LightWrnBg = Color.Parse("#189A6700");
    private static readonly Color LightInfBg = Color.Parse("#180969DA");
    private static readonly Color LightDgbBg = Color.Parse("#18656D76");
    private static readonly Color LightTrcBg = Color.Parse("#188C959F");

    private static readonly SolidColorBrush DarkErrBgBrush = new(DarkErrBg);
    private static readonly SolidColorBrush DarkWrnBgBrush = new(DarkWrnBg);
    private static readonly SolidColorBrush DarkInfBgBrush = new(DarkInfBg);
    private static readonly SolidColorBrush DarkDgbBgBrush = new(DarkDgbBg);
    private static readonly SolidColorBrush DarkTrcBgBrush = new(DarkTrcBg);
    private static readonly SolidColorBrush LightErrBgBrush = new(LightErrBg);
    private static readonly SolidColorBrush LightWrnBgBrush = new(LightWrnBg);
    private static readonly SolidColorBrush LightInfBgBrush = new(LightInfBg);
    private static readonly SolidColorBrush LightDgbBgBrush = new(LightDgbBg);
    private static readonly SolidColorBrush LightTrcBgBrush = new(LightTrcBg);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        var level = value?.ToString()?.ToUpperInvariant();

        return (level, isDark) switch
        {
            ("ERR" or "FTL", true) => DarkErrBgBrush,
            ("WRN", true) => DarkWrnBgBrush,
            ("INF", true) => DarkInfBgBrush,
            ("DBG", true) => DarkDgbBgBrush,
            ("TRC" or "VRB", true) => DarkTrcBgBrush,
            ("ERR" or "FTL", false) => LightErrBgBrush,
            ("WRN", false) => LightWrnBgBrush,
            ("INF", false) => LightInfBgBrush,
            ("DBG" or "TRC" or "VRB", false) => LightDgbBgBrush,
            _ => isDark ? DarkDgbBgBrush : LightDgbBgBrush,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class BoolAndConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values?.All(v => v is true) ?? false;

    public object? ConvertBack(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public sealed class StringNotEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
