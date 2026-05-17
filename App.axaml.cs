using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AppLocalization = Github_Trend.Localization.Localization;
using Serilog;

namespace Github_Trend;

public partial class App : Application
{
    public override void Initialize()
    {
        Log.Information("App.Initialize() called");
        AppLocalization.Instance.Initialize();
        Log.Information("Localization initialized");
        AvaloniaXamlLoader.Load(this);
        Log.Information("Avalonia XAML loaded");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}