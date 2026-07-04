using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

public partial class EnvironmentsWindow : Window
{
    ObservableCollection<EnvironmentModel> _environments = new();

    public EnvironmentsWindow() => InitializeComponent();

    public EnvironmentsWindow(ObservableCollection<EnvironmentModel> environments) : this()
    {
        _environments = environments;
        EnvList.ItemsSource = _environments;
        if (_environments.Count > 0) EnvList.SelectedIndex = 0;
    }

    void EnvList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var env = EnvList.SelectedItem as EnvironmentModel;
        KvGrid.Bind(VarsGrid, env?.Variables, "VARIABLE", "VALOR");
        VarsGrid.IsEnabled = env != null;
    }

    async void Add_Click(object? sender, RoutedEventArgs e)
    {
        var name = await Dialogs.Prompt(this, "Nuevo ambiente", "Nombre del ambiente:", "Desarrollo");
        if (string.IsNullOrWhiteSpace(name)) return;
        var env = new EnvironmentModel { Name = name.Trim() };
        _environments.Add(env);
        EnvList.SelectedItem = env;
    }

    async void Rename_Click(object? sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        var name = await Dialogs.Prompt(this, "Renombrar ambiente", "Nuevo nombre:", env.Name);
        if (!string.IsNullOrWhiteSpace(name)) env.Name = name.Trim();
    }

    async void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        if (await Dialogs.Confirm(this, $"¿Eliminar el ambiente \"{env.Name}\"?", "Eliminar") == DialogResult.Yes)
            _environments.Remove(env);
    }
}
