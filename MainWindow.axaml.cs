using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;
using System.Diagnostics;
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
        private bool _initialized;

        public MainWindow()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = _viewModel;
            _dailyButton = this.FindControl<Button>("DailyTimeRangeButton");
            _weeklyButton = this.FindControl<Button>("WeeklyTimeRangeButton");
            _monthlyButton = this.FindControl<Button>("MonthlyTimeRangeButton");
            _allButton = this.FindControl<Button>("AllTimeRangeButton");
            Loaded += async (_, _) =>
            {
                if (_initialized)
                {
                    return;
                }

                _initialized = true;
                await _viewModel.InitializeAsync();
                UpdateTimeRangeButtonStyles();
            };
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
                button.Background = new SolidColorBrush(Color.Parse("#2563EB"));
                button.BorderBrush = new SolidColorBrush(Color.Parse("#93C5FD"));
                button.Foreground = Brushes.White;
                button.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                button.Background = new SolidColorBrush(Color.Parse("#111827"));
                button.BorderBrush = new SolidColorBrush(Color.Parse("#334155"));
                button.Foreground = new SolidColorBrush(Color.Parse("#CBD5E1"));
                button.FontWeight = FontWeight.SemiBold;
            }
        }
    }
}