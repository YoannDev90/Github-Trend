using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Input;
using Avalonia.VisualTree;
using System;
using System.Diagnostics;
using System.Linq;
using Serilog;

namespace Github_Trend
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel = new();
        private Button? _dailyButton;
        private Button? _weeklyButton;
        private Button? _monthlyButton;
        private Button? _allButton;
        private ScrollViewer? _trendingScrollViewer;
        private ItemsControl? _trendingItemsControl;
        private double _cachedRepoBlockHeight = 260d;
        private bool _initialized;

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = _viewModel;
            _dailyButton = this.FindControl<Button>("DailyTimeRangeButton");
            _weeklyButton = this.FindControl<Button>("WeeklyTimeRangeButton");
            _monthlyButton = this.FindControl<Button>("MonthlyTimeRangeButton");
            _allButton = this.FindControl<Button>("AllTimeRangeButton");
            _trendingScrollViewer = this.FindControl<ScrollViewer>("TrendingScrollViewer");
            _trendingItemsControl = this.FindControl<ItemsControl>("TrendingItemsControl");
            Loaded += async (_, _) =>
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;
                await _viewModel.InitializeAsync();
                UpdateTimeRangeButtonStyles();
                _ = GetRepositoryBlockHeight();
            };
        }

        private void OnTrendingScrollWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            if (_viewModel.TrendingRepositories.Count == 0)
            {
                return;
            }

            var deltaY = e.Delta.Y;
            if (Math.Abs(deltaY) < double.Epsilon)
            {
                return;
            }

            var step = GetRepositoryBlockHeight();
            if (step <= 1)
            {
                return;
            }

            var currentOffset = scrollViewer.Offset.Y;
            var targetOffset = deltaY < 0
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
            {
                return _cachedRepoBlockHeight;
            }

            var firstCard = itemsControl
                .GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(border => border.Classes.Contains("repo-card") && border.Bounds.Height > 1);

            if (firstCard is null)
            {
                return _cachedRepoBlockHeight;
            }

            var measured = firstCard.Bounds.Height + firstCard.Margin.Top + firstCard.Margin.Bottom;
            if (measured > 1)
            {
                _cachedRepoBlockHeight = measured;
            }

            return _cachedRepoBlockHeight;
        }

        private void OnTimeRangeButtonClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is null)
            {
                return;
            }

            if (!int.TryParse(button.Tag.ToString(), out var index))
            {
                return;
            }

            _viewModel.SelectedTimeRangeIndex = index;
            UpdateTimeRangeButtonStyles();
        }

        private void OnOpenRepositoryClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not GithubTrendingRepository repository)
            {
                return;
            }

            var url = repository.RepositoryLink;
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Impossible d'ouvrir le repository URL {Url}", url);
            }
        }

        private void UpdateTimeRangeButtonStyles()
        {
            ApplyTimeRangeButtonStyle(_dailyButton, _viewModel.IsDailySelected);
            ApplyTimeRangeButtonStyle(_weeklyButton, _viewModel.IsWeeklySelected);
            ApplyTimeRangeButtonStyle(_monthlyButton, _viewModel.IsMonthlySelected);
            ApplyTimeRangeButtonStyle(_allButton, _viewModel.IsAllSelected);
        }

        private static void ApplyTimeRangeButtonStyle(Button? button, bool isSelected)
        {
            if (button is null)
            {
                return;
            }

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
}