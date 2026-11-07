using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace Github_Trend;

[Activity(Label = "Github-Trend", MainLauncher = true,
          ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App> { }
