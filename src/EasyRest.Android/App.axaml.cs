using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EasyRest.Android;

// Vuelve a usar XAML: el bug que reescribía el assembly dos veces está parcheado en el csproj.
// Si el parche fallara, el APK saldría sin actividades y el chequeo del CI lo corta.
//
// Application va calificado: los proyectos de Android traen Android.App en los implicit usings
// y el nombre choca con el de Avalonia.
public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        Diag.Marcar("App.Initialize entra");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Diag.RegistrarCrash(e.ExceptionObject as Exception, "excepción no manejada");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            Diag.RegistrarCrash(e.Exception, "tarea sin observar");

        AvaloniaXamlLoader.Load(this);
        Diag.Marcar("App.Initialize ok (XAML cargado)");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Diag.Marcar("App.OnFrameworkInitializationCompleted entra");

        // en móvil no hay ventanas: la app tiene una sola vista y el sistema la enmarca
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new ShellView();
            Diag.Marcar("App: ShellView armada y asignada");
        }
        else
        {
            Diag.Marcar($"App: lifetime inesperado ({ApplicationLifetime?.GetType().Name ?? "null"})");
        }

        base.OnFrameworkInitializationCompleted();
        Diag.Marcar("App.OnFrameworkInitializationCompleted ok");
    }
}
