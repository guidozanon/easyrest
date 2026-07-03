using System.Windows;
using System.Windows.Controls;
using EasyRest.Models;

namespace EasyRest;

public partial class CollectionEditor : UserControl
{
    public CollectionEditor()
    {
        InitializeComponent();
        // Inherit no aplica a colecciones (no hay de dónde heredar)
        AuthTypeCombo.ItemsSource = new[] { AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey };
        ApiKeyInCombo.ItemsSource = new[] { "header", "query" };
        DataContextChanged += (_, _) => UpdateAuthPanels();
    }

    CollectionTab? Vm => DataContext as CollectionTab;

    void AuthType_Changed(object sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    void UpdateAuthPanels()
    {
        var type = Vm?.Collection.Auth.Type ?? AuthType.None;
        BearerPanel.Visibility = type == AuthType.Bearer ? Visibility.Visible : Visibility.Collapsed;
        BasicPanel.Visibility = type == AuthType.Basic ? Visibility.Visible : Visibility.Collapsed;
        ApiKeyPanel.Visibility = type == AuthType.ApiKey ? Visibility.Visible : Visibility.Collapsed;
    }

    void Save_Click(object sender, RoutedEventArgs e) => Vm?.Save();
}
