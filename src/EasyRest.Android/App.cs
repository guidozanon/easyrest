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
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // en móvil no hay ventanas: la app tiene una sola vista y el sistema la enmarca
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new SpikeView();

        base.OnFrameworkInitializationCompleted();
    }
}
