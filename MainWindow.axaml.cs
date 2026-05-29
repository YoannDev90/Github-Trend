using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Github_Trend;

public partial class MainWindow : Window
{
    private readonly Button? _allButton;
    private readonly Button? _dailyButton;
    private readonly Button? _monthlyButton;
    private readonly ItemsControl? _trendingItemsControl;
    private readonly Button? _weeklyButton;
    private double _cachedRepoBlockHeight = 260d;
    private bool _initialized;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = ViewModel;
        ViewModel.GitHubDeviceCodeCopyRequested += async (_, _) =>
            await CopyGitHubDeviceCodeToClipboardAsync();
        _dailyButton = this.FindControl<Button>("DailyTimeRangeButton");
        _weeklyButton = this.FindControl<Button>("WeeklyTimeRangeButton");
        _monthlyButton = this.FindControl<Button>("MonthlyTimeRangeButton");
        _allButton = this.FindControl<Button>("AllTimeRangeButton");
        _trendingItemsControl = this.FindControl<ItemsControl>("TrendingItemsControl");
        Loaded += async (_, _) =>
        {
            if (_initialized)
                return;

            _initialized = true;
            await ViewModel.InitializeAsync();
            UpdateTimeRangeButtonStyles();
            _ = GetRepositoryBlockHeight();
        };
    }

    public MainWindowViewModel ViewModel { get; } = new();

    private async Task CopyGitHubDeviceCodeToClipboardAsync()
    {
        var code = ViewModel.GitHubDeviceCode;
        if (string.IsNullOrWhiteSpace(code))
            return;

        try
        {
            var clipboard = GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return;

            await clipboard.SetTextAsync(code);
            ViewModel.NotifyGitHubCodeCopied();
        }
        catch
        {
            // best-effort only
        }
    }

    private void OnTrendingScrollWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
            return;

        if (ViewModel.TrendingRepositories.Count == 0)
            return;

        var deltaY = e.Delta.Y;
        if (Math.Abs(deltaY) < double.Epsilon)
            return;

        var step = GetRepositoryBlockHeight();
        if (step <= 1)
            return;

        var currentOffset = scrollViewer.Offset.Y;
        var targetOffset =
            deltaY < 0
                ? (Math.Floor(currentOffset / step) + 1) * step
                : (Math.Ceiling(currentOffset / step) - 1) * step;

        var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        targetOffset = Math.Clamp(targetOffset, 0, maxOffset);
        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffset);
        e.Handled = true;
    }

    private double GetRepositoryBlockHeight()
    {
        var itemsControl = _trendingItemsControl;
        if (itemsControl is null)
            return _cachedRepoBlockHeight;

        var firstCard = itemsControl
            .GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border =>
                border.Classes.Contains("repo-card") && border.Bounds.Height > 1
            );

        if (firstCard is null)
            return _cachedRepoBlockHeight;

        var measured = firstCard.Bounds.Height + firstCard.Margin.Top + firstCard.Margin.Bottom;
        if (measured > 1)
            _cachedRepoBlockHeight = measured;

        return _cachedRepoBlockHeight;
    }

    private void OnTimeRangeButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is null)
            return;

        if (!int.TryParse(button.Tag.ToString(), out var index))
            return;

        ViewModel.SelectedTimeRangeIndex = index;
        UpdateTimeRangeButtonStyles();
    }

    private async void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { DataContext = ViewModel };
        await settingsWindow.ShowDialog(this);
    }

    private void UpdateTimeRangeButtonStyles()
    {
        ApplyTimeRangeButtonStyle(_dailyButton, ViewModel.IsDailySelected);
        ApplyTimeRangeButtonStyle(_weeklyButton, ViewModel.IsWeeklySelected);
        ApplyTimeRangeButtonStyle(_monthlyButton, ViewModel.IsMonthlySelected);
        ApplyTimeRangeButtonStyle(_allButton, ViewModel.IsAllSelected);
    }

    private static void ApplyTimeRangeButtonStyle(Button? button, bool isSelected)
    {
        if (button is null)
            return;

        if (isSelected)
        {
            button.Background = new SolidColorBrush(Color.Parse("#238636"));
            button.BorderBrush = new SolidColorBrush(Color.Parse("#2EA043"));
            button.Foreground = Brushes.White;
            button.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            button.Background = new SolidColorBrush(Color.Parse("#21262D"));
            button.BorderBrush = new SolidColorBrush(Color.Parse("#30363D"));
            button.Foreground = new SolidColorBrush(Color.Parse("#C9D1D9"));
            button.FontWeight = FontWeight.SemiBold;
        }
    }
}
