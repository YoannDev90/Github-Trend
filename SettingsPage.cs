namespace Github_Trend;

public enum SettingsPageType
{
    Appearance,
    General,
    Account,
    Trending,
    Cache,
    Logs,
    Backup,
    About
}

public sealed class SettingsPage : ViewModelBase
{
    public SettingsPageType Type { get; }
    public string Label { get; }
    public string IconPath { get; }
    public string Description { get; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SettingsPage(SettingsPageType type, string label, string iconPath, string description)
    {
        Type = type;
        Label = label;
        IconPath = iconPath;
        Description = description;
    }

}
