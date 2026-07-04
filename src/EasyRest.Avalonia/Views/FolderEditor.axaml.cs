using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;

namespace EasyRest.Avalonia.Views;

public partial class FolderEditor : UserControl
{
    public FolderEditor()
    {
        InitializeComponent();
        AuthTypeCombo.ItemsSource = new[]
            { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey };
        ApiKeyInCombo.ItemsSource = new[] { "header", "query" };
        DataContextChanged += (_, _) => UpdateAuthPanels();
    }

    FolderTab? Vm => DataContext as FolderTab;

    void AuthType_Changed(object? sender, SelectionChangedEventArgs e) => UpdateAuthPanels();

    void UpdateAuthPanels()
    {
        var type = Vm?.Folder.Auth.Type ?? AuthType.Inherit;
        BearerPanel.IsVisible = type == AuthType.Bearer;
        BasicPanel.IsVisible = type == AuthType.Basic;
        ApiKeyPanel.IsVisible = type == AuthType.ApiKey;

        InheritHint.IsVisible = type is AuthType.Inherit or AuthType.None;
        InheritHint.Text = type switch
        {
            AuthType.Inherit => "Hereda la autenticación de la carpeta padre o, en su defecto, de la colección.",
            AuthType.None => "No se envía autenticación en esta carpeta, aunque un nivel superior tenga una configurada.",
            _ => ""
        };
    }

    void Save_Click(object? sender, RoutedEventArgs e) => Vm?.Save();
}
