using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace EasyRest.Android;

// Exported=true es obligatorio desde Android 12 para cualquier actividad con intent-filter, y
// MainLauncher genera uno. Name fija el nombre del componente en el manifiesto: sin él queda el
// crc64… que genera el SDK, que sirve igual pero no se puede lanzar a mano con `adb shell am`.
[Activity(
    Name = "com.rentlysoft.easyrest.MainActivity",
    Label = "EasyRest",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.UiMode | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
