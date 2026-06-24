using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Github_Trend.Views.Main;

public partial class SidebarPanel : UserControl
{
    public event EventHandler<RoutedEventArgs>? SettingsClicked;

    public SidebarPanel()
    {
        InitializeComponent();
    }

    public void FocusSearch() => SearchTextBox?.Focus();

    private void OnSettingsButtonClick(object? sender, RoutedEventArgs e)
    {
        SettingsClicked?.Invoke(this, e);
    }
}
