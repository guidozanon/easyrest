using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace EasyRest.Android;

[Activity(
    Label = "EasyRest",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.UiMode | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
