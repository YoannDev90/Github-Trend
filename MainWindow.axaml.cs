using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Github_Trend.Utils;
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
        ViewModel.ConfirmUnstarAsync = () =>
        {
            if (!Services.AppSettings.Default.ConfirmUnstar)
                return Task.FromResult(true);
            return DialogHelper.ShowConfirmActionAsync(
                this,
                loc.GetString("ConfirmUnstar"),
                loc.GetString("ActionUnstar")
            );
        };
        ViewModel.ConfirmUnwatchAsync = () =>
        {
            if (!Services.AppSettings.Default.ConfirmUnwatch)
                return Task.FromResult(true);
            return DialogHelper.ShowConfirmActionAsync(
                this,
                loc.GetString("ConfirmUnwatch"),
                loc.GetString("ActionUnwatch")
            );
        };
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (this.FindControl<Views.Main.SidebarPanel>("Sidebar") is { } sidebar)
            sidebar.SettingsClicked += OnSettingsButtonClick;
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
            _settingsWindow = new SettingsWindow { DataContext = new SettingsWindowViewModel(ViewModel) };
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
                    Sidebar?.FocusSearch();
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
