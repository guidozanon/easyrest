using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using EasyRest.Models;

namespace EasyRest;

public partial class EnvironmentsWindow : Window
{
    readonly ObservableCollection<EnvironmentModel> _environments;

    public EnvironmentsWindow(ObservableCollection<EnvironmentModel> environments)
    {
        InitializeComponent();
        _environments = environments;
        EnvList.ItemsSource = _environments;
        if (_environments.Count > 0) EnvList.SelectedIndex = 0;
    }

    void EnvList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var env = EnvList.SelectedItem as EnvironmentModel;
        VarsGrid.ItemsSource = env?.Variables;
        VarsGrid.IsEnabled = env != null;
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Show(this, "Nuevo ambiente", "Nombre del ambiente:", "Desarrollo");
        if (string.IsNullOrWhiteSpace(name)) return;
        var env = new EnvironmentModel { Name = name.Trim() };
        _environments.Add(env);
        EnvList.SelectedItem = env;
    }

    void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        var name = PromptWindow.Show(this, "Renombrar ambiente", "Nuevo nombre:", env.Name);
        if (!string.IsNullOrWhiteSpace(name)) env.Name = name.Trim();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (EnvList.SelectedItem is not EnvironmentModel env) return;
        if (MessageBox.Show(this, $"¿Eliminar el ambiente \"{env.Name}\"?", "Eliminar",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _environments.Remove(env);
    }
}
