using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

public partial class CollectionEditor : UserControl
{
    public CollectionEditor()
    {
        InitializeComponent();
        AuthTypeCombo.ItemsSource = new[] { AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey };
        ApiKeyInCombo.ItemsSource = new[] { "header", "query" };
        DataContextChanged += (_, _) => UpdateAuthPanels();
    }

    CollectionTab? Vm => DataContext as CollectionTab;

    void AuthType_Changed(object? sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    void UpdateAuthPanels()
    {
        var type = Vm?.Collection.Auth.Type ?? AuthType.None;
        BearerPanel.IsVisible = type == AuthType.Bearer;
        BasicPanel.IsVisible = type == AuthType.Basic;
        ApiKeyPanel.IsVisible = type == AuthType.ApiKey;
    }

    void Save_Click(object? sender, RoutedEventArgs e) => Vm?.Save();
}
