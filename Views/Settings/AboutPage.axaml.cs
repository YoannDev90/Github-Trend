using Avalonia.Controls;
using Avalonia.Input;

namespace Github_Trend.Views.Settings;

public partial class AboutPage : UserControl
{
    public AboutPage()
    {
        InitializeComponent();
    }

    private void OnLinkPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is string url)
        {
            if (DataContext is SettingsWindowViewModel vm)
                vm.OpenLinkCommand.Execute(url);
        }
    }
}
