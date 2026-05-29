using System;
using System.IO;
using Avalonia;
using Serilog;

namespace Github_Trend;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ConfigureLogging();

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

    private static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            Constants.Logging.LogDirectoryName
        );
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, Constants.Logging.LogFilePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7
            )
            .CreateLogger();
    }
}
