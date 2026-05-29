using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using System;
using System.Threading.Tasks;

namespace Github_Trend;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnCopyLogsClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is not MainWindowViewModel viewModel)
                return;

            var allInfo = $"{viewModel.DebugInfo}\n\n=== APPLICATION LOGS ===\n\n{viewModel.AppLogs}";
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
            {
                await clipboard.SetTextAsync(allInfo);
                viewModel.NotifyGitHubCodeCopied();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to copy logs to clipboard");
        }
    }
}
