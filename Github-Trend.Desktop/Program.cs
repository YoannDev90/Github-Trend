using System;
using System.IO;
using Avalonia;
using Github_Trend.Services;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppConfig.Load();
        LoggingConfigurator.Configure();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application crashed during startup/runtime");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    public static void ConfigureLogging() => LoggingConfigurator.Configure();
}
