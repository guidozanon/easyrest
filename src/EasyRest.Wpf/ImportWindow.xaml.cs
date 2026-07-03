using System.IO;
using System.Windows;
using System.Windows.Media;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest;

/// <summary>Diálogo de importación de OpenAPI: muestra el progreso y el resumen del resultado.</summary>
public partial class ImportWindow : Window
{
    readonly string _filePath;

    public RequestCollection? ImportedCollection { get; private set; }
    public string? BaseUrl { get; private set; }

    public ImportWindow(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        FileText.Text = Path.GetFileName(filePath);
        Loaded += async (_, _) => await RunImportAsync();
    }

    async Task RunImportAsync()
    {
        try
        {
            var (collection, baseUrl) = await Task.Run(() => OpenApiImporter.Import(_filePath));
            ImportedCollection = collection;
            BaseUrl = baseUrl;

            TitleText.Text = "✔ Importación completa";
            TitleText.Foreground = (Brush)FindResource("GreenBrush");
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
            TitleText.Foreground = (Brush)FindResource("RedBrush");
            ResultText.Text = ex.Message;
        }
        finally
        {
            Progress.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            OkBtn.IsEnabled = true;
        }
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = ImportedCollection != null;
}
