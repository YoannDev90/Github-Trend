using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using Github_Trend.Services;
using Serilog;

namespace Github_Trend;

public sealed partial class DebugViewModel : INotifyPropertyChanged
{
    private string _appLogs = string.Empty;
    private string _debugInfo = string.Empty;
    private bool _loaded;
    private string? _currentLogPath;
    private LogEntry? _selectedLogEntry;

    [GeneratedRegex(@"\[(\w{3})\]\s?(.*)$", RegexOptions.Compiled)]
    private static partial Regex LogLineRegex();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+ [+-]\d{2}:\d{2})", RegexOptions.Compiled)]
    private static partial Regex TimestampRegex();

    private const int MaxLogEntries = 500;

    private readonly List<LogEntry> _allLogEntries = new();
    private readonly HashSet<string> _activeLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "DBG", "INF", "WRN", "ERR"
    };

    public DebugViewModel()
    {
        CopyLogsCommand = new RelayCommand(_ => CopyLogsRequested?.Invoke(this, EventArgs.Empty));
        ToggleLevelCommand = new RelayCommand(p => { Log.Debug("LogFilter: ToggleLevel {Level}", p); ToggleLevel(p as string); });
        CopySelectedLogCommand = new RelayCommand(
            p => CopySelectedRequested?.Invoke(this, EventArgs.Empty),
            _ => SelectedLogEntry is not null
        );
        CopyAllFilteredCommand = new RelayCommand(_ => CopyAllFilteredRequested?.Invoke(this, EventArgs.Empty));
        DeleteCurrentLogCommand = new RelayCommand(
            _ => DeleteCurrentLogRequested?.Invoke(this, EventArgs.Empty),
            _ => _currentLogPath is not null
        );
        DeleteAllLogsCommand = new RelayCommand(
            _ => DeleteAllLogsRequested?.Invoke(this, EventArgs.Empty),
            _ => Directory.Exists(LogDirPath)
        );
        ReloadLogsCommand = new RelayCommand(_ => Reload());
    }

    public ICommand CopyLogsCommand { get; }
    public ICommand ToggleLevelCommand { get; }
    public ICommand CopySelectedLogCommand { get; }
    public ICommand CopyAllFilteredCommand { get; }
    public ICommand DeleteCurrentLogCommand { get; }
    public ICommand DeleteAllLogsCommand { get; }
    public ICommand ReloadLogsCommand { get; }

    public event EventHandler? CopyLogsRequested;
    public event EventHandler? CopySelectedRequested;
    public event EventHandler? CopyAllFilteredRequested;
    public event EventHandler? DeleteCurrentLogRequested;
    public event EventHandler? DeleteAllLogsRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LogEntry> FilteredLogEntries { get; } = new();
    public ObservableCollection<DebugInfoItem> InfoItems { get; } = new();

    public LogEntry? SelectedLogEntry
    {
        get => _selectedLogEntry;
        set
        {
            if (_selectedLogEntry == value) return;
            _selectedLogEntry = value;
            OnPropertyChanged();
            (CopySelectedLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

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

    public string LogDirPath => Path.Combine(AppContext.BaseDirectory, Constants.Logging.LogDirectoryName);

    public string CurrentLogFileName => _currentLogPath is not null
        ? Path.GetFileName(_currentLogPath)
        : "none";

    public string? CurrentLogPath => _currentLogPath;

    public bool IsTrcActive => _activeLevels.Contains("TRC");
    public bool IsDbgActive => _activeLevels.Contains("DBG");
    public bool IsInfActive => _activeLevels.Contains("INF");
    public bool IsWrnActive => _activeLevels.Contains("WRN");
    public bool IsErrActive => _activeLevels.Contains("ERR");
    public bool IsFtlActive => _activeLevels.Contains("FTL");

    public string FilterSummary => $"{FilteredLogEntries.Count} / {_allLogEntries.Count}";

    public void LoadExtraInfo(MainWindowViewModel vm)
    {
        InfoItems.Add(new DebugInfoItem("Auth",
            vm.Auth.IsConnected ? $"Connected ({vm.Auth.AccountSummary})" : "Disconnected",
            vm.Auth.IsConnected ? "#238636" : "#F85149"));
        InfoItems.Add(new DebugInfoItem("Theme",
            ThemeService.Default.IsDark ? "Dark" : "Light",
            "#58A6FF"));
        InfoItems.Add(new DebugInfoItem("Languages",
            $"{vm.Filter.SelectedCount} selected",
            "#D29922"));

        if (!_loaded)
            Load();
    }

    private void Load()
    {
        try
        {
            _loaded = true;
            InfoItems.Clear();
            InfoItems.Add(new DebugInfoItem("OS", $"{RuntimeInformation.OSDescription}", null));
            InfoItems.Add(new DebugInfoItem("Architecture", RuntimeInformation.ProcessArchitecture.ToString(), null));
            InfoItems.Add(new DebugInfoItem(".NET", RuntimeInformation.FrameworkDescription, null));
            InfoItems.Add(new DebugInfoItem("Runtime ID", RuntimeInformation.RuntimeIdentifier, null));
            InfoItems.Add(new DebugInfoItem("App Path", AppContext.BaseDirectory, null));
            InfoItems.Add(new DebugInfoItem("Processors", Environment.ProcessorCount.ToString(), null));
            InfoItems.Add(new DebugInfoItem("Memory", $"{GC.GetTotalMemory(false) / 1024 / 1024} MB", null));

            LoadLogs();
        }
        catch (Exception ex)
        {
            DebugInfo = $"Error loading debug info: {ex.Message}";
            AppLogs = $"Error loading logs: {ex.Message}";
        }
    }

    private void LoadLogs()
    {
        var logDir = LogDirPath;
        if (!Directory.Exists(logDir))
        {
            _currentLogPath = null;
            AppLogs = "Log directory not found.";
            OnPropertyChanged(nameof(CurrentLogFileName));
            (DeleteCurrentLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteAllLogsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            return;
        }

        var latest = Directory
            .GetFiles(logDir, "*.log")
            .OrderByDescending(f => f)
            .FirstOrDefault();

        _currentLogPath = latest;
        AppLogs = latest is null
            ? "No log files found."
            : TryReadFile(latest);

        OnPropertyChanged(nameof(CurrentLogFileName));
        (DeleteCurrentLogCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteAllLogsCommand as RelayCommand)?.RaiseCanExecuteChanged();

        ParseLogEntries(AppLogs);
        ApplyFilter();
    }

    private void Reload()
    {
        Log.Debug("LogReload: starting");
        _loaded = false;
        InfoItems.Clear();
        Load();
    }

    private void ParseLogEntries(string logs)
    {
        _allLogEntries.Clear();
        var lines = logs.Split('\n');
        var start = Math.Max(0, lines.Length - MaxLogEntries);

        for (var i = start; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var match = LogLineRegex().Match(trimmed);
            if (match.Success)
            {
                var tsMatch = TimestampRegex().Match(trimmed);
                var timestamp = tsMatch.Success ? tsMatch.Groups[1].Value : "";
                _allLogEntries.Add(new LogEntry(
                    timestamp,
                    match.Groups[1].Value,
                    match.Groups[2].Value));
            }
            else
            {
                _allLogEntries.Add(new LogEntry("", "", trimmed));
            }
        }

        _appLogs = string.Empty;
    }

    private void ToggleLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return;

        if (_activeLevels.Contains(level))
            _activeLevels.Remove(level);
        else
            _activeLevels.Add(level);

        OnPropertyChanged(nameof(IsTrcActive));
        OnPropertyChanged(nameof(IsDbgActive));
        OnPropertyChanged(nameof(IsInfActive));
        OnPropertyChanged(nameof(IsWrnActive));
        OnPropertyChanged(nameof(IsErrActive));
        OnPropertyChanged(nameof(IsFtlActive));

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredLogEntries.Clear();
        foreach (var entry in _allLogEntries)
        {
            if (string.IsNullOrEmpty(entry.Level) || _activeLevels.Contains(entry.Level))
                FilteredLogEntries.Add(entry);
        }

        OnPropertyChanged(nameof(FilterSummary));
    }

    public string GetFilteredLogsAsText()
    {
        var sb = new StringBuilder();
        foreach (var entry in FilteredLogEntries)
        {
            if (!string.IsNullOrEmpty(entry.Timestamp))
                sb.Append(entry.Timestamp).Append(' ');
            if (!string.IsNullOrEmpty(entry.Level))
                sb.Append('[').Append(entry.Level).Append("] ");
            sb.AppendLine(entry.Message);
        }
        return sb.ToString();
    }

    public void DeleteLogFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete log file: {Path}", path);
        }
    }

    public void DeleteAllLogFiles()
    {
        try
        {
            var dir = LogDirPath;
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.log"))
                    File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete log files");
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

public sealed record LogEntry(string Timestamp, string Level, string Message);

public sealed record DebugInfoItem(string Label, string Value, string? AccentColor);
