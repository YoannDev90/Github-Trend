using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Serilog;

namespace Github_Trend;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Debug.CopyLogsRequested += OnCopyLogsRequested;
    }

    private async void OnCopyLogsRequested(object? sender, EventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel vm)
                return;

            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(vm.Debug.LogsContent);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy logs to clipboard");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
