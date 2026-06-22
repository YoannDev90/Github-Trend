using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Github_Trend.Services;
using Github_Trend.Utils;
using Serilog;

namespace Github_Trend;

public partial class SettingsWindow : Window
{
    private bool _subscribedToDebug;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Debug.CopyLogsRequested += OnCopyLogsRequested;
            vm.Debug.CopySelectedRequested += OnCopySelectedRequested;
            vm.Debug.CopyAllFilteredRequested += OnCopyAllFilteredRequested;
            vm.Debug.DeleteCurrentLogRequested += OnDeleteCurrentLogRequested;
            vm.Debug.DeleteAllLogsRequested += OnDeleteAllLogsRequested;
            vm.Auth.ConfirmSignOutAsync = ConfirmSignOutAsync;
            vm.Debug.LoadExtraInfo(vm);

            if (!_subscribedToDebug)
            {
                vm.Debug.PropertyChanged += OnDebugPropertyChanged;
                _subscribedToDebug = true;
            }

            UpdateFilterButtons();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.Debug.CopyLogsRequested -= OnCopyLogsRequested;
            vm.Debug.CopySelectedRequested -= OnCopySelectedRequested;
            vm.Debug.CopyAllFilteredRequested -= OnCopyAllFilteredRequested;
            vm.Debug.DeleteCurrentLogRequested -= OnDeleteCurrentLogRequested;
            vm.Debug.DeleteAllLogsRequested -= OnDeleteAllLogsRequested;
            if (_subscribedToDebug)
            {
                vm.Debug.PropertyChanged -= OnDebugPropertyChanged;
                _subscribedToDebug = false;
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            if (DataContext is MainWindowViewModel vm && vm.Debug.SelectedLogEntry is { } entry)
            {
                var text = FormatLogEntry(entry);
                _ = CopyToClipboardAsync(text);
                e.Handled = true;
            }
        }
    }

    private void OnDebugPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DebugViewModel.IsTrcActive)
            or nameof(DebugViewModel.IsDbgActive)
            or nameof(DebugViewModel.IsInfActive)
            or nameof(DebugViewModel.IsWrnActive)
            or nameof(DebugViewModel.IsErrActive)
            or nameof(DebugViewModel.IsFtlActive))
        {
            UpdateFilterButtons();
        }
    }

    private void UpdateFilterButtons()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var debug = vm.Debug;

        SetFilterActive("TrcButton", debug.IsTrcActive);
        SetFilterActive("DbgButton", debug.IsDbgActive);
        SetFilterActive("InfButton", debug.IsInfActive);
        SetFilterActive("WrnButton", debug.IsWrnActive);
        SetFilterActive("ErrButton", debug.IsErrActive);
        SetFilterActive("FtlButton", debug.IsFtlActive);
    }

    private void SetFilterActive(string buttonName, bool active)
    {
        var button = this.FindControl<Button>(buttonName);
        if (button is null) return;
        if (active)
            button.Classes.Add("active");
        else
            button.Classes.Remove("active");
    }

    private static string FormatLogEntry(LogEntry entry)
    {
        var parts = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(entry.Timestamp))
            parts.Append(entry.Timestamp).Append(' ');
        if (!string.IsNullOrEmpty(entry.Level))
            parts.Append('[').Append(entry.Level).Append("] ");
        parts.Append(entry.Message);
        return parts.ToString();
    }

    private async Task<bool> ConfirmSignOutAsync() =>
        await DialogHelper.ShowConfirmSignOutAsync(this);

    private async void OnCopyLogsRequested(object? sender, EventArgs e)
    {
        Log.Debug("Copy all logs requested");
        if (DataContext is not MainWindowViewModel vm) return;
        await CopyToClipboardAsync(vm.Debug.LogsContent);
    }

    private async void OnCopySelectedRequested(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (vm.Debug.SelectedLogEntry is { } entry)
        {
            Log.Debug("Copy selected log entry: {Entry}", FormatLogEntry(entry));
            await CopyToClipboardAsync(FormatLogEntry(entry));
        }
    }

    private async void OnCopyAllFilteredRequested(object? sender, EventArgs e)
    {
        Log.Debug("Copy all filtered logs requested");
        if (DataContext is not MainWindowViewModel vm) return;
        await CopyToClipboardAsync(vm.Debug.GetFilteredLogsAsText());
    }

    private async void OnDeleteCurrentLogRequested(object? sender, EventArgs e)
    {
        Log.Debug("Delete current log requested");
        if (DataContext is not MainWindowViewModel vm) return;
        var path = vm.Debug.CurrentLogPath;
        if (path is null) return;

        var confirmed = await DialogHelper.ShowConfirmAsync(
            this,
            "Delete log file",
            $"Delete the current log file \"{vm.Debug.CurrentLogFileName}\"?"
        );
        if (confirmed)
        {
            Log.Information("Deleting log file: {Path}", path);
            Serilog.Log.CloseAndFlush();
            vm.Debug.DeleteLogFile(path);
            Program.ConfigureLogging();
            vm.Debug.ReloadLogsCommand.Execute(null);
            Log.Information("Log file deleted: {Path}", path);
        }
    }

    private async void OnDeleteAllLogsRequested(object? sender, EventArgs e)
    {
        Log.Debug("Delete all logs requested");
        if (DataContext is not MainWindowViewModel vm) return;

        var confirmed = await DialogHelper.ShowConfirmAsync(
            this,
            "Delete all logs",
            "Delete ALL log files? This cannot be undone."
        );
        if (confirmed)
        {
            Log.Information("Deleting all log files");
            Serilog.Log.CloseAndFlush();
            vm.Debug.DeleteAllLogFiles();
            Program.ConfigureLogging();
            vm.Debug.ReloadLogsCommand.Execute(null);
            Log.Information("All log files deleted");
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(text);
                Log.Debug("Copied {Len} chars to clipboard", text.Length);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy to clipboard");
        }
    }

    private void OnDarkThemeClick(object? sender, RoutedEventArgs e)
    {
        Log.Information("Theme changed to Dark");
        ThemeService.Default.SetDark(true);
        UpdateThemeUI();
    }

    private void OnLightThemeClick(object? sender, RoutedEventArgs e)
    {
        Log.Information("Theme changed to Light");
        ThemeService.Default.SetDark(false);
        UpdateThemeUI();
    }

    private void UpdateThemeUI()
    {
        var isDark = ThemeService.Default.IsDark;

        if (isDark)
        {
            DarkThemeButton.Classes.Add("selected");
            LightThemeButton.Classes.Remove("selected");
        }
        else
        {
            LightThemeButton.Classes.Add("selected");
            DarkThemeButton.Classes.Remove("selected");
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        UpdateThemeUI();
    }
}
