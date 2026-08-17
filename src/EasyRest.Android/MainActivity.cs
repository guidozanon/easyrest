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
    Theme = "@style/EasyRestTheme",
    Exported = false,
    LaunchMode = LaunchMode.SingleTop,
    // Sin declarar estos cambios, Android recrea la actividad al rotar, al plegar o desplegar un
    // fold y al redimensionar en multiventana: se pierde la request abierta y lo que estabas
    // escribiendo. Declarándolos, el sistema sólo avisa y Avalonia relayoutea — que es justo lo
    // que ShellView sabe hacer sin reconstruir nada.
    //
    // ScreenLayout y SmallestScreenSize son las dos que importan en un fold: la pantalla no
    // cambia de orientación al desplegarse, cambia de tamaño y de clasificación.
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize |
                           ConfigChanges.UiMode | ConfigChanges.Density |
                           ConfigChanges.KeyboardHidden)]
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
