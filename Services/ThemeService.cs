using System;
using Avalonia;
using Avalonia.Styling;

namespace Github_Trend.Services;

public static class ThemeService
{
    private const string PreferenceKey = "theme_preference";
    private static readonly string ConfigPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Github_Trend",
        PreferenceKey
    );

    public static bool IsDark { get; private set; } = true;

    public static void Initialize()
    {
        try
        {
            if (System.IO.File.Exists(ConfigPath))
            {
                var value = System.IO.File.ReadAllText(ConfigPath).Trim();
                IsDark = value != "Light";
            }
        }
        catch { }

        Apply();
    }

    public static void Toggle()
    {
        IsDark = !IsDark;
        Apply();
        Save();
    }

    public static void SetDark(bool dark)
    {
        IsDark = dark;
        Apply();
        Save();
    }

    private static void Apply()
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void Save()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(ConfigPath, IsDark ? "Dark" : "Light");
        }
        catch { }
    }
}
