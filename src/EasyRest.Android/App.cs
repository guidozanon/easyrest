using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
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
        // Un crash en un teléfono sin cable es una pantalla que se cierra y nada más. Con esto,
        // la excepción queda en disco y se muestra en el próximo arranque.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Registrar(e.ExceptionObject as Exception, "excepción no manejada");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            CrashLog.Registrar(e.Exception, "tarea sin observar");

        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // en móvil no hay ventanas: la app tiene una sola vista y el sistema la enmarca
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = ArmarVista();

        base.OnFrameworkInitializationCompleted();
    }

    static Control ArmarVista()
    {
        // el crash de la corrida anterior, si lo hubo, se muestra arriba de todo
        var anterior = CrashLog.LeerYBorrar();

        try
        {
            var vista = new SpikeView();
            return anterior is null ? vista : ConAviso(anterior, vista);
        }
        catch (Exception ex)
        {
            // si ni la pantalla se puede armar, al menos que se vea por qué
            CrashLog.Registrar(ex, "armando la pantalla");
            return ConAviso($"No se pudo armar la pantalla.\n\n{CrashLog.Describir(ex)}", null);
        }
    }

    static Control ConAviso(string texto, Control? abajo)
    {
        var aviso = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#45222E")),
            Padding = new Thickness(12),
            Margin = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Se cayó",
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#F38BA8"))
                    },
                    // seleccionable para poder copiar el stack y pegarlo
                    new SelectableTextBlock
                    {
                        Text = texto,
                        FontSize = 11,
                        FontFamily = "monospace",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#CDD6F4"))
                    }
                }
            }
        };

        var pila = new StackPanel { Children = { aviso } };
        if (abajo != null) pila.Children.Add(abajo);

        return new ScrollViewer
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            Content = pila
        };
    }
}
