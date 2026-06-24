using Avalonia.Controls;
using Avalonia.Interactivity;
using Github_Trend.Localization;
using Github_Trend.Services;

namespace Github_Trend.Views.Settings;

public partial class AppearancePage : UserControl
{
    public AppearancePage()
    {
        InitializeComponent();
    }

    public void UpdateThemeUI()
    {
        var vm = DataContext as SettingsWindowViewModel;
        if (vm is null) return;
        var isDark = vm.IsDarkTheme;

        if (isDark)
        {
            DarkThemeButton.Classes.Add("selected");
            LightThemeButton.Classes.Remove("selected");
        }
        else
        {
            LightThemeButton.Classes.Add("selected");
            DarkThemeButton.Classes.Remove("selected");
        }
    }

    public void UpdateLangUI()
    {
        var isFrench = LocalizationService.CurrentLanguage == "fr";
        if (isFrench)
        {
            FrenchLangButton.Classes.Add("selected");
            EnglishLangButton.Classes.Remove("selected");
        }
        else
        {
            EnglishLangButton.Classes.Add("selected");
            FrenchLangButton.Classes.Remove("selected");
        }
    }

    private void OnDarkThemeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SetDarkThemeCommand.Execute(null);
        UpdateThemeUI();
    }

    private void OnLightThemeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.SetLightThemeCommand.Execute(null);
        UpdateThemeUI();
    }

    private void OnEnglishLangClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.IsLanguageEnglish = true;
        UpdateLangUI();
    }

    private void OnFrenchLangClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsWindowViewModel vm)
            vm.IsLanguageFrench = true;
        UpdateLangUI();
    }
}
