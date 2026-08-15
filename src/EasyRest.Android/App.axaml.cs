using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EasyRest.Android;

public class App : Application
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
