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

    public static void ConfigureLogging()
    {
        var logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            Constants.Logging.LogDirectoryName
        );
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(logDirectory, Constants.Logging.LogFilePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug
            )
            .CreateLogger();
    }
}
