using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Avalonia;

/// <summary>Semilla del head multiplataforma: lista las requests del workspace (mismo Storage
/// que la app WPF) y permite enviarlas con el mismo HttpExecutor, ambiente incluido.</summary>
public partial class MainWindow : Window
{
    public class RequestRow
    {
        public required RequestItem Request { get; init; }
        public required RequestCollection Owner { get; init; }
        public required string Display { get; init; }
        public string Method => Request.Method;
    }

    readonly List<EnvironmentModel> _environments;
    readonly EnvironmentModel? _activeEnv;

    public MainWindow()
    {
        InitializeComponent();

        var rows = new List<RequestRow>();
        foreach (var col in Storage.LoadCollections())
        {
            foreach (var req in col.AllRequests)
                rows.Add(new RequestRow
                {
                    Request = req,
                    Owner = col,
                    Display = $"{col.Name} › {req.Name}"
                });
        }
        RequestsList.ItemsSource = rows;

        _environments = Storage.LoadEnvironments();
        var settings = Storage.LoadSettings();
        _activeEnv = _environments.FirstOrDefault(e => e.Id == settings.ActiveEnvironmentId);
    }

    RequestRow? Selected => RequestsList.SelectedItem as RequestRow;

    void RequestsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Selected is not { } row) return;
        BreadcrumbText.Text = row.Display;
        MethodBox.Text = row.Request.Method;
        UrlBox.Text = row.Request.Url;
        StatusText.Text = "";
        ResponseBox.Text = "";
    }

    async void Send_Click(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        SendBtn.IsEnabled = false;
        StatusText.Text = "Enviando…";
        try
        {
            // la URL editada en la caja aplica solo a este envío
            var request = System.Text.Json.JsonSerializer.Deserialize<RequestItem>(
                System.Text.Json.JsonSerializer.Serialize(row.Request))!;
            request.Url = UrlBox.Text ?? request.Url;

            var r = await HttpExecutor.ExecuteAsync(request, row.Owner, _activeEnv);
            StatusText.Text = r.Error != null ? $"Error: {r.Error}" : $"{r.StatusCode} {r.StatusText} · {r.ElapsedMs} ms";
            ResponseBox.Text = r.Error ?? r.Body;
        }
        finally
        {
            SendBtn.IsEnabled = true;
        }
    }
}
