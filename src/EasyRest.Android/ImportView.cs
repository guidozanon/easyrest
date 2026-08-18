using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

using Orientation = Avalonia.Layout.Orientation;

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
/// Cuando termina bien no dice «listo» y ya: muestra qué entró —cuántas requests, cuántas
/// carpetas, qué ambiente quedó armado— y ofrece ir a verlo. Importar doscientas requests y volver
/// a una pantalla que no cambió es lo que hace dudar de si funcionó.
///
/// Las dos importaciones son las del Core, sin una segunda implementación: OpenApiImporter pide
/// una ruta, así que el texto se escribe en un archivo temporal y se borra enseguida.</summary>
internal class ImportView : UserControl
{
    readonly Action<RequestCollection> _alImportar;
    readonly Func<List<RequestCollection>> _colecciones;
    readonly Action _alVolver;

    readonly TextBox _texto;
    readonly TextBlock _estado = Ui.Nota("");
    readonly ContentControl _resultado = new();

    public ImportView(Func<List<RequestCollection>> colecciones, Action<RequestCollection> alImportar,
        Action alVolver)
    {
        _colecciones = colecciones;
        _alImportar = alImportar;
        _alVolver = alVolver;

        _texto = Ui.Campo("", "https://…/openapi.json — o el documento, o un curl",
            multilinea: true, mono: true);
        _texto.MinHeight = 130;
        _texto.Background = Brushes.Transparent;
        _texto.BorderThickness = new Thickness(0);
        _texto.Padding = new Thickness(0);

        var pegar = Ui.Secundario("Pegar", Iconos.Copiar, () => _ = PegarAsync());
        pegar.MinHeight = 40;
        var limpiar = Ui.Fantasma("Limpiar", null, () =>
        {
            _texto.Text = "";
            _estado.Text = "";
            _resultado.Content = null;
        });
        limpiar.MinHeight = 40;

        var entrada = Ui.Tarjeta(12,
            _texto,
            new Border { Height = 1, Background = Ui.Borde },
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { pegar, limpiar }
            });

        var openApi = Ui.PrimarioAsync("OpenAPI", null, ImportarOpenApiAsync);
        var curl = Ui.Secundario("cURL", null, ImportarCurl);
        curl.MinHeight = 52;
        curl.CornerRadius = new CornerRadius(12);
        curl.HorizontalAlignment = HorizontalAlignment.Stretch;

        var pila = new StackPanel
        {
            Margin = new Thickness(16, 18, 16, 24),
            Spacing = 14,
            Children =
            {
                Ui.Parrafo("Pegá el link de un OpenAPI y lo bajo, o el documento entero —JSON o YAML—. " +
                           "Un comando cURL crea una request suelta.", Ui.Subtexto),
                entrada,
                Ui.Rejilla(2, 10, openApi, curl),
                _estado,
                _resultado
            }
        };

        var atrás = Ui.BotonIcono(Iconos.Atras, alVolver, Ui.Normal);
        var titulo = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { atrás, Ui.Titulo("Importar") }
        };

        var raíz = new DockPanel();
        var encabezado = Ui.Encabezado(titulo);
        DockPanel.SetDock(encabezado, Dock.Top);
        raíz.Children.Add(encabezado);
        raíz.Children.Add(new ScrollViewer { Content = pila });

        Content = raíz;
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
            Aviso("No hay nada pegado.");
            return;
        }

        if (EsUrl(texto))
        {
            _estado.Text = "Bajando el documento…";
            _estado.Foreground = Ui.Tenue;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                texto = await http.GetStringAsync(texto);
            }
            catch (Exception ex)
            {
                Aviso($"No se pudo bajar el documento: {ex.Message}");
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

            _estado.Text = "";
            _resultado.Content = Resultado(colección, baseUrl != null, variables);
            _alImportar(colección);
        }
        catch (Exception ex)
        {
            // Cuando lo pegado no es un documento, el parser contesta «OpenAPI specification
            // version '<todo lo que le pasaste>' is not supported», que no le dice nada a nadie
            // y encima escupe el contenido entero en la pantalla.
            Aviso(texto.Length < 500 && !texto.TrimStart().StartsWith('{')
                ? "Eso no parece un documento OpenAPI. Si es un link, pegalo solo y lo bajo yo."
                : $"No se pudo importar: {ex.Message}");
        }
        finally
        {
            try { File.Delete(temporal); } catch (IOException) { /* temporal huérfano, no es grave */ }
        }
    }

    void Aviso(string texto)
    {
        _resultado.Content = null;
        _estado.Text = texto;
        _estado.Foreground = Ui.Durazno;
    }

    /// <summary>Lo que entró, en números. Después de bajar un spec de doscientas requests, ver
    /// «142 requests · 29 carpetas» es la diferencia entre creerle a la app y volver a apretar.</summary>
    Control Resultado(RequestCollection colección, bool hayAmbiente, List<KeyValueItem> variables)
    {
        var requests = colección.AllRequests.Count();
        var carpetas = colección.AllFolders.Count();

        var titulo = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                Ui.Icono(Iconos.Tilde, 18, Ui.Verde),
                new TextBlock
                {
                    Text = colección.Name,
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Ui.Verde,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };

        var cifras = Ui.Rejilla(3, 10,
            Ui.Metrica("requests", requests.ToString()),
            Ui.Metrica("carpetas", carpetas.ToString()),
            Ui.Metrica("ambiente", hayAmbiente ? "1" : "0"));

        var pila = new StackPanel { Spacing = 12, Children = { titulo, cifras } };

        if (hayAmbiente)
        {
            var nombres = new List<string> { "baseUrl" };
            nombres.AddRange(variables.Select(v => v.Key));
            var detalle = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    Ui.Icono(Iconos.Globo, 15, Ui.Subtexto),
                    Ui.Parrafo($"Ambiente «{colección.Name}» con {string.Join(", ", nombres)}.",
                        Ui.Subtexto, 12)
                }
            };
            pila.Children.Add(detalle);
        }

        var ver = Ui.Secundario("Ver la colección", null, _alVolver);
        ver.MinHeight = 52;
        ver.CornerRadius = new CornerRadius(12);
        ver.HorizontalAlignment = HorizontalAlignment.Stretch;
        pila.Children.Add(ver);

        return new Border
        {
            Background = Ui.Tinte(Ui.CVerde, 0.08),
            BorderBrush = Ui.Tinte(Ui.CVerde, 0.28),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Child = pila
        };
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
            Aviso("Eso no parece un comando cURL.");
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
        _estado.Text = "";
        _resultado.Content = Resultado(colección, false, new List<KeyValueItem>());
        _alImportar(colección);
    }

    static string NombreDesde(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "Importada";
        var segmento = uri.Segments.LastOrDefault()?.Trim('/');
        return string.IsNullOrWhiteSpace(segmento) ? uri.Host : segmento;
    }
}
