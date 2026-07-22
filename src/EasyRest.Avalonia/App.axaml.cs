using System;
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

    void AboutClicked(object? sender, EventArgs e)
    {
        var about = new AboutWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
            about.ShowDialog(main);
        else
            about.Show();
    }
}
