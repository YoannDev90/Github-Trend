using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Serilog;

namespace Github_Trend;

public partial class MainWindow : Window
{
    private bool _initialized;
    private SettingsWindow? _settingsWindow;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = ViewModel;
        ViewModel.DeviceCodeCopyRequested += async (_, _) =>
            await CopyGitHubDeviceCodeToClipboardAsync();
        var loc = Github_Trend.Localization.Localization.Instance;
        ViewModel.ConfirmUnstarAsync = () => ConfirmRepoActionAsync(
            loc.GetString("ConfirmUnstar"),
            loc.GetString("ActionUnstar")
        );
        ViewModel.ConfirmUnwatchAsync = () => ConfirmRepoActionAsync(
            loc.GetString("ConfirmUnwatch"),
            loc.GetString("ActionUnwatch")
        );
        ViewModel.ShowSaveToStarListDialogAsync = (lists, createAsync) =>
            SaveToStarListDialog.ShowAsync(this, lists, createAsync);
        Loaded += async (_, _) =>
        {
            if (_initialized)
                return;

            _initialized = true;
            await ViewModel.InitializeAsync();
        };
    }

    private async Task<bool> ConfirmRepoActionAsync(string title, string action)
    {
        var confirm = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = this.FindResource("BackgroundPrimaryBrush") as IBrush,
        };

        var panel = new StackPanel { Spacing = 16, Margin = new(20) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
        });

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new(16, 8),
            Background = this.FindResource("BackgroundTertiaryBrush") as IBrush,
            Foreground = this.FindResource("TextPrimaryBrush") as IBrush,
            BorderThickness = new(0),
            CornerRadius = new(8),
        };
        var actionBtn = new Button
        {
            Content = action,
            Padding = new(16, 8),
            Background = this.FindResource("AccentPrimaryBrush") as IBrush,
            Foreground = this.FindResource("TextOnAccentBrush") as IBrush,
            BorderThickness = new(0),
            CornerRadius = new(8),
        };

        var tcs = new TaskCompletionSource<bool>();
        cancelBtn.Click += (_, _) => { Log.Debug("ConfirmDialog: cancelled"); tcs.TrySetResult(false); confirm.Close(); };
        actionBtn.Click += (_, _) => { Log.Debug("ConfirmDialog: confirmed ({Action})", action); tcs.TrySetResult(true); confirm.Close(); };

        buttonRow.Children.Add(cancelBtn);
        buttonRow.Children.Add(actionBtn);
        panel.Children.Add(buttonRow);
        confirm.Content = panel;

        await confirm.ShowDialog(this);
        return await tcs.Task;
    }

    public MainWindowViewModel ViewModel { get; } = new();

    private async Task CopyGitHubDeviceCodeToClipboardAsync()
    {
        var code = ViewModel.Auth.DeviceCode;
        if (string.IsNullOrWhiteSpace(code))
            return;

        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            await clipboard.SetTextAsync(code);
        }
        catch { }
    }

    private async void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        Log.Debug("Settings button clicked");
        if (_settingsWindow is null || !_settingsWindow.IsVisible)
        {
            _settingsWindow = new SettingsWindow { DataContext = ViewModel };
            await _settingsWindow.ShowDialog(this);
        }
        else
        {
            _settingsWindow.Activate();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        Log.Debug("Key down: {Key} modifiers={Modifiers}", e.Key, e.KeyModifiers);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.F:
                    Log.Debug("Ctrl+F: focusing search");
                    e.Handled = true;
                    var searchBox = this.FindControl<TextBox>("SearchTextBox");
                    searchBox?.Focus();
                    break;
                case Key.R:
                    Log.Debug("Ctrl+R: refreshing colors");
                    if (ViewModel.RefreshCommand.CanExecute(null))
                    {
                        e.Handled = true;
                        ViewModel.RefreshCommand.Execute(null);
                    }
                    break;
            }
        }
        else if (e.Key == Key.Escape)
        {
            Log.Debug("Escape: clearing search");
            e.Handled = true;
            ViewModel.Filter.SearchText = string.Empty;
        }
    }
}
