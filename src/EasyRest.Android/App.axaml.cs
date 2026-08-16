using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EasyRest.Android;

// Application va calificado: los proyectos de Android traen Android.App en los implicit usings
// y el nombre choca con el de Avalonia.
public class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // en móvil no hay ventanas: la app tiene una sola vista y el sistema la enmarca
        if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
            singleView.MainView = new SpikeView();

        base.OnFrameworkInitializationCompleted();
    }
}
