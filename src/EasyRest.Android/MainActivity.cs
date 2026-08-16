using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace EasyRest.Android;

// Ya no es la actividad de lanzador: esa es InicioActivity, que es Android puro y por lo tanto
// puede mostrar el error cuando esto se cae. Acá empieza todo lo que depende de Avalonia, así
// que cada paso deja una miga en disco.
//
// Name fija el nombre del componente en el manifiesto: sin él queda el crc64… que genera el SDK,
// que sirve igual pero no se puede lanzar a mano con `adb shell am`.
[Activity(
    Name = "com.rentlysoft.easyrest.MainActivity",
    Label = "EasyRest",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    Exported = false,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.UiMode | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        Diag.Marcar("MainActivity.CustomizeAppBuilder entra");
        var armado = base.CustomizeAppBuilder(builder).WithInterFont();
        Diag.Marcar("MainActivity.CustomizeAppBuilder ok");
        return armado;
    }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        Diag.Marcar("MainActivity.OnCreate entra");
        try
        {
            base.OnCreate(savedInstanceState);
            Diag.Marcar("MainActivity.OnCreate ok");
        }
        catch (Exception ex)
        {
            // se registra y se deja propagar: atajarlo dejaría una actividad a medio construir
            Diag.RegistrarCrash(ex, "MainActivity.OnCreate");
            throw;
        }
    }
}
