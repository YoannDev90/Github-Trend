using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;

namespace Github_Trend.Services;

public sealed class AppSettings : INotifyPropertyChanged
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Github_Trend",
        "user_preferences.json"
    );

    private static AppSettings? _instance;
    public static AppSettings Default => _instance ??= new AppSettings();

    private bool _showBanners = true;
    private bool _showTopics = true;
    private bool _autoRefresh;
    private int _autoRefreshIntervalMinutes = 15;
    private bool _confirmUnstar = true;
    private bool _confirmUnwatch = true;
    private bool _rememberLastTimeRange = true;

    public bool ShowBanners
    {
        get => _showBanners;
        set { if (_showBanners == value) return; _showBanners = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public bool ShowTopics
    {
        get => _showTopics;
        set { if (_showTopics == value) return; _showTopics = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public bool AutoRefresh
    {
        get => _autoRefresh;
        set { if (_autoRefresh == value) return; _autoRefresh = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public int AutoRefreshIntervalMinutes
    {
        get => _autoRefreshIntervalMinutes;
        set { if (_autoRefreshIntervalMinutes == value) return; _autoRefreshIntervalMinutes = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public bool ConfirmUnstar
    {
        get => _confirmUnstar;
        set { if (_confirmUnstar == value) return; _confirmUnstar = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public bool ConfirmUnwatch
    {
        get => _confirmUnwatch;
        set { if (_confirmUnwatch == value) return; _confirmUnwatch = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    public bool RememberLastTimeRange
    {
        get => _rememberLastTimeRange;
        set { if (_rememberLastTimeRange == value) return; _rememberLastTimeRange = value; OnPropertyChanged(); _ = SaveAsync(); }
    }

    private AppSettings() { }

    public async Task LoadAsync()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = await File.ReadAllTextAsync(ConfigPath);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data is null) return;

            _showBanners = data.ShowBanners;
            _showTopics = data.ShowTopics;
            _autoRefresh = data.AutoRefresh;
            _autoRefreshIntervalMinutes = data.AutoRefreshIntervalMinutes;
            _confirmUnstar = data.ConfirmUnstar;
            _confirmUnwatch = data.ConfirmUnwatch;
            _rememberLastTimeRange = data.RememberLastTimeRange;

            OnPropertyChanged(nameof(ShowBanners));
            OnPropertyChanged(nameof(ShowTopics));
            OnPropertyChanged(nameof(AutoRefresh));
            OnPropertyChanged(nameof(AutoRefreshIntervalMinutes));
            OnPropertyChanged(nameof(ConfirmUnstar));
            OnPropertyChanged(nameof(ConfirmUnwatch));
            OnPropertyChanged(nameof(RememberLastTimeRange));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load user preferences");
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            var data = new SettingsData
            {
                ShowBanners = _showBanners,
                ShowTopics = _showTopics,
                AutoRefresh = _autoRefresh,
                AutoRefreshIntervalMinutes = _autoRefreshIntervalMinutes,
                ConfirmUnstar = _confirmUnstar,
                ConfirmUnwatch = _confirmUnwatch,
                RememberLastTimeRange = _rememberLastTimeRange,
            };
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to save user preferences");
        }
    }

    private sealed class SettingsData
    {
        public bool ShowBanners { get; set; } = true;
        public bool ShowTopics { get; set; } = true;
        public bool AutoRefresh { get; set; }
        public int AutoRefreshIntervalMinutes { get; set; } = 15;
        public bool ConfirmUnstar { get; set; } = true;
        public bool ConfirmUnwatch { get; set; } = true;
        public bool RememberLastTimeRange { get; set; } = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
