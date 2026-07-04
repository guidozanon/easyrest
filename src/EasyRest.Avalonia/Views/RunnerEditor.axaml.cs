using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace EasyRest.Avalonia.Views;

public partial class RunnerEditor : UserControl
{
    bool _applyingPreset;

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

    // ----- Presets -----

    void Preset_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset) return;
        if (PresetCombo.SelectedItem is RunnerPreset p) Vm?.ApplyPreset(p);
    }

    async void SavePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || this.FindAncestorOfType<MainWindow>() is not { } main) return;
        var suggested = (PresetCombo.SelectedItem as RunnerPreset)?.Name ?? "";
        var name = await Dialogs.Prompt(main, "Guardar configuración", "Nombre del preset:", suggested);
        if (string.IsNullOrWhiteSpace(name)) return;
        var preset = vm.SavePreset(name);
        if (preset != null)
        {
            _applyingPreset = true;          // seleccionarlo no debe re-aplicar
            PresetCombo.SelectedItem = preset;
            _applyingPreset = false;
        }
    }

    async void DeletePreset_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || PresetCombo.SelectedItem is not RunnerPreset p) return;
        if (this.FindAncestorOfType<MainWindow>() is not { } main) return;
        var ok = await Dialogs.Confirm(main, $"¿Borrar el preset «{p.Name}»?", "Borrar preset");
        if (ok == DialogResult.Yes) vm.DeletePreset(p);
    }
}
