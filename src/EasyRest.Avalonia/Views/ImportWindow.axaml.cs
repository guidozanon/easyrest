using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

public partial class ImportWindow : Window
{
    string _filePath = "";

    public RequestCollection? ImportedCollection { get; private set; }
    public string? BaseUrl { get; private set; }

    public ImportWindow() => InitializeComponent();

    public ImportWindow(string filePath) : this()
    {
        _filePath = filePath;
        FileText.Text = Path.GetFileName(filePath);
        Opened += async (_, _) => await RunImportAsync();
    }

    async Task RunImportAsync()
    {
        try
        {
            var (collection, baseUrl) = await Task.Run(() => OpenApiImporter.Import(_filePath));
            ImportedCollection = collection;
            BaseUrl = baseUrl;

            TitleText.Text = "✔ Importación completa";
            TitleText.Foreground = Brush.Parse("#A6E3A1");
            ResultText.Text =
                $"Colección \"{collection.Name}\"\n" +
                $"    •  {collection.AllRequests.Count()} requests\n" +
                $"    •  {collection.AllFolders.Count()} carpetas\n" +
                (string.IsNullOrWhiteSpace(baseUrl)
                    ? "    •  El documento no define servers: definí {{baseUrl}} en un ambiente."
                    : $"    •  Se creará el ambiente \"{collection.Name}\" con baseUrl = {baseUrl}");
        }
        catch (Exception ex)
        {
            TitleText.Text = "✖ Error al importar";
            TitleText.Foreground = Brush.Parse("#F38BA8");
            ResultText.Text = ex.Message;
        }
        finally
        {
            Progress.IsVisible = false;
            ResultText.IsVisible = true;
            OkBtn.IsEnabled = true;
        }
    }

    void Ok_Click(object? sender, RoutedEventArgs e) => Close();
}
