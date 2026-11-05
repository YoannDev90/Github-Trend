using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Github_Trend.Localization;
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
        KeyDown += OnKeyDown;
        Closed += OnClosed;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        WireDebugEvents();
        WireBackupDelegates();
        InitPageUI();
    }

    private void WireDebugEvents()
    {
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (_subscribedToDebug) return;
        _subscribedToDebug = true;

        vm.Debug.CopyLogsRequested += OnCopyLogsRequested;
        vm.Debug.CopySelectedRequested += OnCopySelectedRequested;
        vm.Debug.CopyAllFilteredRequested += OnCopyAllFilteredRequested;
        vm.Debug.DeleteCurrentLogRequested += OnDeleteCurrentLogRequested;
        vm.Debug.DeleteAllLogsRequested += OnDeleteAllLogsRequested;
        vm.Auth.ConfirmSignOutAsync = ConfirmSignOutAsync;
        vm.Debug.PropertyChanged += OnDebugPropertyChanged;
        var mainVm = GetMainVm();
        if (mainVm is not null)
            vm.Debug.LoadExtraInfo(mainVm);
    }

    private void UnwireDebugEvents()
    {
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (!_subscribedToDebug) return;
        _subscribedToDebug = false;

        vm.Debug.CopyLogsRequested -= OnCopyLogsRequested;
        vm.Debug.CopySelectedRequested -= OnCopySelectedRequested;
        vm.Debug.CopyAllFilteredRequested -= OnCopyAllFilteredRequested;
        vm.Debug.DeleteCurrentLogRequested -= OnDeleteCurrentLogRequested;
        vm.Debug.DeleteAllLogsRequested -= OnDeleteAllLogsRequested;
        vm.Debug.PropertyChanged -= OnDebugPropertyChanged;
    }

    private void InitPageUI()
    {
        AppearancePageControl?.UpdateThemeUI();
        AppearancePageControl?.UpdateLangUI();
        LogsPageControl?.UpdateFilterButtons();
    }

    private MainWindowViewModel? GetMainVm() =>
        (DataContext as SettingsWindowViewModel)?.SourceViewModel;

    private void OnClosed(object? sender, EventArgs e)
    {
        KeyDown -= OnKeyDown;
        UnwireDebugEvents();
        (DataContext as IDisposable)?.Dispose();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            if (DataContext is SettingsWindowViewModel vm && vm.Debug.SelectedLogEntry is { } entry)
            {
                var text = Views.Settings.LogsPage.FormatLogEntryForClipboard(entry);
                _ = CopyToClipboardAsync(text);
                e.Handled = true;
            }
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is SettingsPage page)
        {
            if (DataContext is SettingsWindowViewModel vm)
                vm.SelectPageCommand.Execute(page);
        }
    }

    private void OnDebugPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DebugViewModel.IsTrcActive)
            or nameof(DebugViewModel.IsDbgActive)
            or nameof(DebugViewModel.IsInfActive)
            or nameof(DebugViewModel.IsWrnActive)
            or nameof(DebugViewModel.IsErrActive)
            or nameof(DebugViewModel.IsFtlActive)
            && LogsPageControl is not null)
        {
            LogsPageControl.UpdateFilterButtons();
        }
    }

    private void WireBackupDelegates()
    {
        if (DataContext is not SettingsWindowViewModel vm) return;

        vm.PickExportFilePath = async () =>
        {
            var file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Settings",
                DefaultExtension = ".json",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            return file?.Path.LocalPath;
        };

        vm.PickImportFilePath = async () =>
        {
            var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Settings",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
            });
            return files.Count > 0 ? files[0].Path.LocalPath : null;
        };
    }

    private async Task<bool> ConfirmSignOutAsync() =>
        await DialogHelper.ShowConfirmSignOutAsync(this);

    private async void OnCopyLogsRequested(object? sender, EventArgs e)
    {
        Log.Debug("Copy all logs requested");
        if (DataContext is not SettingsWindowViewModel vm) return;
        await CopyToClipboardAsync(vm.Debug.LogsContent);
    }

    private async void OnCopySelectedRequested(object? sender, EventArgs e)
    {
        if (DataContext is not SettingsWindowViewModel vm) return;
        if (vm.Debug.SelectedLogEntry is { } entry)
        {
            var text = Views.Settings.LogsPage.FormatLogEntryForClipboard(entry);
            Log.Debug("Copy selected log entry: {Entry}", text);
            await CopyToClipboardAsync(text);
        }
    }

    private async void OnCopyAllFilteredRequested(object? sender, EventArgs e)
    {
        Log.Debug("Copy all filtered logs requested");
        if (DataContext is not SettingsWindowViewModel vm) return;
        await CopyToClipboardAsync(vm.Debug.GetFilteredLogsAsText());
    }

    private async void OnDeleteCurrentLogRequested(object? sender, EventArgs e)
    {
        Log.Debug("Delete current log requested");
        if (DataContext is not SettingsWindowViewModel vm) return;
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
        if (DataContext is not SettingsWindowViewModel vm) return;

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
}
