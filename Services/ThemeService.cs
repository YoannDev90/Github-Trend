using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;

namespace Github_Trend.Services;

public sealed class ThemeService
{
    public static ThemeService Default { get; } = new();

    private const string PreferenceKey = "theme_preference";
    private readonly string _configPath;

    public ThemeService()
    {
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Github_Trend",
            PreferenceKey
        );
    }

    public bool IsDark { get; private set; } = true;

    public async Task InitializeAsync()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var value = (await File.ReadAllTextAsync(_configPath)).Trim();
                IsDark = value != "Light";
            }
        }
        catch { }

        Apply();
    }

    public void Toggle()
    {
        IsDark = !IsDark;
        Apply();
        _ = SaveAsync();
    }

    public void SetDark(bool dark)
    {
        IsDark = dark;
        Apply();
        _ = SaveAsync();
    }

    private void Apply()
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_configPath, IsDark ? "Dark" : "Light");
        }
        catch { }
    }
}
