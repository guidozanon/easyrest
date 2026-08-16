using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace EasyRest.Android;

// La UI del head está armada en C# y no en XAML, y no es por gusto: el compilador de XAML de
// Avalonia reescribe el assembly de la app hacia obj/…/Avalonia/ y ese redireccionamiento se
// aplica dos veces en el build de Android, dejando una ruta Avalonia/Avalonia/ que no existe.
// El resultado era un APK sin ninguna actividad. Está explicado en docs/ANDROID.md.
//
// Application va calificado: los proyectos de Android traen Android.App en los implicit usings
// y el nombre choca con el de Avalonia.
public class App : Avalonia.Application
{
    public override void Initialize()
    {
        Diag.Marcar("App.Initialize entra");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diag.RegistrarCrash(e.ExceptionObject as Exception, "excepción no manejada");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Diag.RegistrarCrash(e.Exception, "tarea sin observar");

        RequestedThemeVariant = ThemeVariant.Dark;
        Diag.Marcar("App.Initialize: tema pedido");

        Styles.Add(new FluentTheme());
        Diag.Marcar("App.Initialize ok (FluentTheme cargado)");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Diag.Marcar("App.OnFrameworkInitializationCompleted entra");

        // en móvil no hay ventanas: la app tiene una sola vista y el sistema la enmarca
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new SpikeView();
            Diag.Marcar("App: SpikeView armada y asignada");
        }
        else
        {
            Diag.Marcar($"App: lifetime inesperado ({ApplicationLifetime?.GetType().Name ?? "null"})");
        }

        base.OnFrameworkInitializationCompleted();
        Diag.Marcar("App.OnFrameworkInitializationCompleted ok");
    }
}
