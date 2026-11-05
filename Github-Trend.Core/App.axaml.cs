using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Github_Trend.Services;
using Serilog;
using AppLocalization = Github_Trend.Localization.Localization;

namespace Github_Trend;

public class App : Application
{
    public override void Initialize()
    {
        Log.Debug("Initializing application");
        AppLocalization.Instance.Initialize();
        Log.Debug("Localization initialized");
        AvaloniaXamlLoader.Load(this);
        Log.Debug("Avalonia XAML loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Log.Debug("Framework initialization completed");
        _ = ThemeService.Default.InitializeAsync();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            Log.Debug("Main window created");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
