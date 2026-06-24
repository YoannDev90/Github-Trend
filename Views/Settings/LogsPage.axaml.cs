using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;

namespace Github_Trend.Views.Settings;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();
        Unloaded += (_, _) => KeyDown -= OnKeyDown;
        KeyDown += OnKeyDown;
    }

    public void UpdateFilterButtons()
    {
        var debug = (DataContext as SettingsWindowViewModel)?.Debug;
        if (debug is null) return;

        SetFilterActive(TrcButton, debug.IsTrcActive);
        SetFilterActive(DbgButton, debug.IsDbgActive);
        SetFilterActive(InfButton, debug.IsInfActive);
        SetFilterActive(WrnButton, debug.IsWrnActive);
        SetFilterActive(ErrButton, debug.IsErrActive);
        SetFilterActive(FtlButton, debug.IsFtlActive);
    }

    private static void SetFilterActive(Button button, bool active)
    {
        if (active)
            button.Classes.Add("active");
        else
            button.Classes.Remove("active");
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            if (LogListBox.SelectedItem is LogEntry entry)
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                    await clipboard.SetTextAsync(FormatLogEntryForClipboard(entry));
                e.Handled = true;
            }
        }
    }

    internal static string FormatLogEntryForClipboard(LogEntry entry)
    {
        var parts = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(entry.Timestamp))
            parts.Append(entry.Timestamp).Append(' ');
        if (!string.IsNullOrEmpty(entry.Level))
            parts.Append('[').Append(entry.Level).Append("] ");
        parts.Append(entry.Message);
        return parts.ToString();
    }
}
