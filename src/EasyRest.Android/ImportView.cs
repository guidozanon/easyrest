using Avalonia;
using Avalonia.Controls;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Android;

/// <summary>Importar pegando: un documento OpenAPI (JSON o YAML), la URL de uno, o un cURL.
///
/// En una computadora se importa un archivo; en un teléfono, lo que hay es el portapapeles —el
/// cURL que te pasaron por chat, el spec que abriste en el navegador—. Por eso la entrada es un
/// campo de texto con un botón de pegar y no un selector de archivos.
///
/// **La URL cuenta como documento.** Un spec publicado son cientos de kilobytes: nadie los pega
/// en un teléfono, se pega el link. Si lo pegado es una URL se baja primero, que es lo que
/// alguien esperaría que pase.
///
/// Las dos importaciones son las del Core, sin una segunda implementación: OpenApiImporter pide
/// una ruta, así que el texto se escribe en un archivo temporal y se borra enseguida.</summary>
internal class ImportView : UserControl
{
    readonly Action<RequestCollection> _alImportar;
    readonly Func<List<RequestCollection>> _colecciones;
    readonly TextBox _texto;
    readonly TextBlock _estado = Ui.Parrafo("", Ui.Tenue, 12);

    public ImportView(Func<List<RequestCollection>> colecciones, Action<RequestCollection> alImportar)
    {
        _colecciones = colecciones;
        _alImportar = alImportar;

        _texto = Ui.Campo("", "Pegá la URL del OpenAPI, el documento, o un curl…",
            multilinea: true, mono: true);
        _texto.MinHeight = 200;

        var pila = new StackPanel
        {
            Margin = new Thickness(12, 0, 12, 16),
            Spacing = 12,
            Children =
            {
                Ui.Parrafo("Pegá la URL de un OpenAPI —o el documento entero, JSON o YAML— para " +
                           "crear una colección, o un comando cURL para crear una request.",
                           Ui.Tenue, 12),
                Ui.Barra(
                    Ui.AccionAsync("Pegar del portapapeles", PegarAsync),
                    Ui.Accion("Limpiar", () => { _texto.Text = ""; _estado.Text = ""; })),
                _texto,
                Ui.Barra(
                    Ui.AccionAsync("Importar OpenAPI", ImportarOpenApiAsync),
                    Ui.Accion("Importar cURL", ImportarCurl)),
                _estado
            }
        };

        Content = new ScrollViewer { Content = pila };
    }

    async Task PegarAsync()
    {
        var portapapeles = TopLevel.GetTopLevel(this)?.Clipboard;
        if (portapapeles == null) return;
        _texto.Text = await portapapeles.GetTextAsync() ?? "";
    }

    async Task ImportarOpenApiAsync()
    {
        var texto = (_texto.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(texto))
        {
            _estado.Text = "No hay nada pegado.";
            return;
        }

        if (EsUrl(texto))
        {
            _estado.Text = "Bajando el documento…";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                texto = await http.GetStringAsync(texto);
            }
            catch (Exception ex)
            {
                _estado.Text = $"No se pudo bajar el documento: {ex.Message}";
                return;
            }
        }

        Importar(texto);
    }

    /// <summary>Una URL sola, sin saltos de línea. Alcanza para distinguirla de un documento: un
    /// OpenAPI empieza con `{` o con `openapi:`, nunca con http.</summary>
    static bool EsUrl(string texto) =>
        !texto.Contains('\n') &&
        (texto.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         texto.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        Uri.TryCreate(texto, UriKind.Absolute, out _);

    void Importar(string texto)
    {
        // el importador del Core lee de un archivo; el temporal se borra igual si falla
        var temporal = Path.Combine(Path.GetTempPath(), $"easyrest-import-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temporal, texto);
            var (colección, baseUrl, variables) = OpenApiImporter.Import(temporal);

            Storage.SaveCollection(colección);
            if (baseUrl != null) GuardarAmbiente(colección.Name, baseUrl, variables);

            _estado.Text = $"Importada «{colección.Name}» con {colección.AllRequests.Count()} requests" +
                           (baseUrl != null ? $" y el ambiente «{colección.Name}» con baseUrl." : ".");
            _alImportar(colección);
        }
        catch (Exception ex)
        {
            // Cuando lo pegado no es un documento, el parser contesta «OpenAPI specification
            // version '<todo lo que le pasaste>' is not supported», que no le dice nada a nadie
            // y encima escupe el contenido entero en la pantalla.
            _estado.Text = texto.Length < 500 && !texto.TrimStart().StartsWith('{')
                ? "Eso no parece un documento OpenAPI. Si es un link, pegalo solo y lo bajo yo."
                : $"No se pudo importar: {ex.Message}";
        }
        finally
        {
            try { File.Delete(temporal); } catch (IOException) { /* temporal huérfano, no es grave */ }
        }
    }

    /// <summary>Mismo gesto que el escritorio: si el spec declara servers, el baseUrl y las
    /// variables del server quedan en un ambiente propio, porque las requests importadas los usan
    /// como {{baseUrl}} y {{variable}}.</summary>
    static void GuardarAmbiente(string nombre, string baseUrl, List<KeyValueItem> variables)
    {
        var ambientes = Storage.LoadEnvironments();
        var ambiente = ambientes.FirstOrDefault(a => a.Name == nombre);
        if (ambiente == null)
        {
            ambiente = new EnvironmentModel { Name = nombre };
            ambientes.Add(ambiente);
        }

        Poner(ambiente, "baseUrl", baseUrl);
        foreach (var variable in variables) Poner(ambiente, variable.Key, variable.Value);

        Storage.SaveEnvironments(ambientes);
    }

    /// <summary>No pisa lo que ya tenga valor: reimportar no te borra el tenant que configuraste.</summary>
    static void Poner(EnvironmentModel ambiente, string clave, string valor)
    {
        var variable = ambiente.Variables.FirstOrDefault(v => v.Key == clave);
        if (variable == null) ambiente.Variables.Add(new KeyValueItem { Key = clave, Value = valor });
        else if (string.IsNullOrWhiteSpace(variable.Value)) variable.Value = valor;
    }

    void ImportarCurl()
    {
        var texto = _texto.Text ?? "";
        if (!CurlHelper.TryParse(texto, out var curl))
        {
            _estado.Text = "Eso no parece un comando cURL.";
            return;
        }

        var request = new RequestItem
        {
            Name = NombreDesde(curl.Url),
            Method = curl.Method,
            Url = curl.Url,
            Auth = { Type = AuthType.Inherit }
        };

        foreach (var (clave, valor) in curl.Headers)
            request.Headers.Add(new KeyValueItem { Key = clave, Value = valor });

        if (curl.BasicUser != null)
        {
            request.Auth.Type = AuthType.Basic;
            request.Auth.Username = curl.BasicUser;
            request.Auth.Password = curl.BasicPassword ?? "";
        }

        if (!string.IsNullOrWhiteSpace(curl.Body))
        {
            var cuerpo = curl.Body!.TrimStart();
            request.Body.Type = cuerpo.StartsWith('{') || cuerpo.StartsWith('[')
                ? BodyType.Json
                : BodyType.Text;
            request.Body.Raw = curl.Body!;
        }

        var existentes = _colecciones();
        if (existentes.Count == 0)
        {
            Guardar(new RequestCollection { Name = "Importadas" }, request);
            return;
        }

        // dónde va la request es la única decisión que no puedo tomar por la persona
        Dialogo.Opciones("¿En qué colección?", existentes
            .Select(c => (c.Name, (Action)(() => Guardar(c, request))))
            .Append(("+ Colección nueva", () => Guardar(new RequestCollection { Name = "Importadas" }, request)))
            .ToArray());
    }

    void Guardar(RequestCollection colección, RequestItem request)
    {
        colección.Requests.Add(request);
        Storage.SaveCollection(colección);
        _estado.Text = $"«{request.Name}» agregada a «{colección.Name}».";
        _alImportar(colección);
    }

    static string NombreDesde(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Importada";
        var segmento = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(segmento) ? uri.Host : segmento;
    }
}
