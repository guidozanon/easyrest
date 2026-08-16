using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace EasyRest.Android;

// Exported=true es obligatorio desde Android 12 para cualquier actividad con intent-filter, y
// MainLauncher genera uno. Sin esto el sistema puede aceptar el paquete y dejar la app sin
// entrada en el lanzador, que es justo el síntoma de "instala pero no aparece".
[Activity(
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
