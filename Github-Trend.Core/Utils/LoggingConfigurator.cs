using System;
using System.IO;
using Serilog;

namespace Github_Trend.Utils;

public static class LoggingConfigurator
{
    /// <summary>
    /// Writable log directory, shared by the writer and the debug UI.
    /// It must not sit next to the executable: installed system-wide
    /// (/opt/github-trend) that directory is root-owned and the app would
    /// crash with UnauthorizedAccessException on first launch.
    /// </summary>
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Github_Trend",
        Github_Trend.Services.AppConfig.Logging.DirectoryName
    );

    public static void Configure()
    {
        var logDirectory = LogDirectory;
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information)
            .WriteTo.File(
                Path.Combine(logDirectory, Github_Trend.Services.AppConfig.Logging.FilePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Github_Trend.Services.AppConfig.Logging.RetainedFileCount,
                restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Debug
            )
            .CreateLogger();
    }
}
