using System;
using System.IO;
using Serilog;

namespace Github_Trend.Utils;

public static class LoggingConfigurator
{
    public static void Configure()
    {
        var logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            Github_Trend.Services.AppConfig.Logging.DirectoryName
        );
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
