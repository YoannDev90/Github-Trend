using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;
using Serilog;

namespace Github_Trend;

public sealed class DebugViewModel : INotifyPropertyChanged
{
    private string _appLogs = string.Empty;
    private string _debugInfo = string.Empty;

    public DebugViewModel()
    {
        CopyLogsCommand = new RelayCommand(_ => CopyLogsRequested?.Invoke(this, EventArgs.Empty));
        Load();
    }

    public ICommand CopyLogsCommand { get; }

    public event EventHandler? CopyLogsRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string DebugInfo
    {
        get => _debugInfo;
        private set
        {
            if (_debugInfo == value) return;
            _debugInfo = value;
            OnPropertyChanged();
        }
    }

    public string AppLogs
    {
        get => _appLogs;
        private set
        {
            if (_appLogs == value) return;
            _appLogs = value;
            OnPropertyChanged();
        }
    }

    public string LogsContent => $"{DebugInfo}\n\n=== APPLICATION LOGS ===\n\n{AppLogs}";

    private void Load()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DEBUG INFORMATION ===");
            sb.AppendLine();
            sb.AppendLine($"OS: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Runtime: {RuntimeInformation.RuntimeIdentifier}");
            sb.AppendLine();
            sb.AppendLine($".NET Runtime: {RuntimeInformation.FrameworkDescription}");

            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
                try
                {
                    sb.AppendLine(
                        $"Process Architecture: {Process.GetCurrentProcess().ProcessorAffinity}"
                    );
                }
                catch
                {
                    sb.AppendLine("Process Architecture: N/A");
                }

            sb.AppendLine();
            sb.AppendLine($"Application Path: {AppContext.BaseDirectory}");
            sb.AppendLine($"Current Directory: {Environment.CurrentDirectory}");
            sb.AppendLine($"User: {Environment.UserName}");
            sb.AppendLine();
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"Available Memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");

            DebugInfo = sb.ToString();

            var logDir = Path.Combine(AppContext.BaseDirectory, Constants.Logging.LogDirectoryName);
            if (!Directory.Exists(logDir))
            {
                AppLogs = "Log directory not found.";
                return;
            }

            var latest = Directory
                .GetFiles(logDir, "*.log")
                .OrderByDescending(f => f)
                .FirstOrDefault();

            AppLogs = latest is null
                ? "No log files found."
                : TryReadFile(latest);
        }
        catch (Exception ex)
        {
            DebugInfo = $"Error loading debug info: {ex.Message}";
            AppLogs = $"Error loading logs: {ex.Message}";
        }
    }

    private static string TryReadFile(string path)
    {
        try { return File.ReadAllText(path); }
        catch { return "Unable to read log file."; }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
