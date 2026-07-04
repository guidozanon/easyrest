using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using EasyRest.Avalonia.Views;
using EasyRest.Services;

namespace EasyRest.Avalonia;

public class App : Application
{
    public override void Initialize()
    {
        // nombre de la app en el menú de macOS ("EasyRest", "Ocultar EasyRest", etc.)
        Name = "EasyRest";
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ExecutionLog.Marshal = action => Dispatcher.UIThread.Post(action);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();

        base.OnFrameworkInitializationCompleted();
    }

    // "Acerca de EasyRest" del menú de macOS (reemplaza el About de Avalonia)
    async void About_Click(object? sender, EventArgs e)
    {
        if ((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is not { } owner) return;
        var version = GetType().Assembly.GetName().Version?.ToString(3) ?? "";
        await Dialogs.Info(owner,
            $"EasyRest{(version.Length > 0 ? $" {version}" : "")}\n\nCliente HTTP de escritorio.\nHecho con Avalonia.",
            "Acerca de EasyRest");
    }
}
