using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace EasyRest.Avalonia.Views;

public partial class RunnerEditor : UserControl
{
    public RunnerEditor() => InitializeComponent();

    RunnerTab? Vm => DataContext as RunnerTab;

    // "Correr": valida la config y abre un tab de corrida que ejecuta y muestra resultados
    void Run_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm?.BuildConfig() is { } cfg && this.FindAncestorOfType<MainWindow>() is { } main)
            main.OpenRun(cfg);
    }

    void Compare_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<MainWindow>() is { } main) main.OpenComparison();
    }
}
