using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Una request: se edita entera —método, URL, params, cabeceras, auth, cuerpo y
/// scripts—, se manda y se guarda.
///
/// El spike no guardaba a propósito. Ahora sí, y por eso guardar es un botón y no un efecto de
/// escribir: los cambios se ven en el acto porque se escriben sobre el mismo modelo que muestra
/// el árbol —igual que en el escritorio—, pero al disco van cuando la persona lo decide. Sin ese
/// corte, un roce en el colectivo termina en el repo de todo el equipo.
///
/// Las secciones se arman al entrar en cada una en vez de todas juntas: en un teléfono lo que se
/// ve es una sola, y armar seis pantallas de controles para mostrar una es lo que hace que abrir
/// una request se sienta lenta.</summary>
internal class RequestEditorView : UserControl
{
    static readonly string[] Métodos = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS" };
    static readonly string[] Secciones = { "Params", "Cabeceras", "Auth", "Cuerpo", "Scripts" };

    readonly RequestItem _request;
    readonly RequestCollection _colección;
    readonly Func<EnvironmentModel?> _ambiente;
    readonly Action _alGuardar;

    readonly StackPanel _solapas = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
    readonly ContentControl _sección = new();
    readonly TextBox _url;
    readonly Button _método;
    readonly Button _guardar;
    readonly Button _enviar;

    readonly TextBlock _estado = new() { FontSize = 14, FontWeight = FontWeight.SemiBold };
    readonly StackPanel _respuesta = new() { Spacing = 8 };
    readonly ContentControl _vistaRespuesta = new();

    string _seccionActiva = "Params";
    string _vistaActiva = "Cuerpo";
    ResponseResult? _última;
    bool _sucio;

    public RequestEditorView(RequestItem request, RequestCollection colección,
        Func<EnvironmentModel?> ambiente, Action alGuardar)
    {
        _request = request;
        _colección = colección;
        _ambiente = ambiente;
        _alGuardar = alGuardar;

        _url = Ui.Campo(request.Url, "https://…");
        _url.TextChanged += (_, _) => { _request.Url = _url.Text ?? ""; Ensuciar(); };

        _método = Ui.Opcion(request.Method, true, RotarMétodo);
        _método.MinWidth = 92;

        _enviar = Ui.AccionAsync("Enviar", EnviarAsync);
        _enviar.Background = Ui.Acento;
        _enviar.Foreground = Ui.Fondo;

        _guardar = Ui.Accion("Guardar", Guardar);
        _guardar.IsEnabled = false;

        _vistaRespuesta.Content = Ui.Rotulo("Todavía no mandaste nada.");
        _respuesta.Children.Add(_estado);
        _respuesta.Children.Add(SelectorDeVista());
        _respuesta.Children.Add(_vistaRespuesta);

        ArmarSolapas();
        MostrarSección(_seccionActiva);

        var pila = new StackPanel
        {
            Margin = new Thickness(12, 0, 12, 16),
            Spacing = 10,
            Children =
            {
                Ui.Rotulo(_colección.Name),
                Encabezado(),
                Ui.Barra(_enviar, _guardar),
                _solapas,
                _sección,
                Ui.Tarjeta(_respuesta)
            }
        };

        Content = new ScrollViewer { Content = pila };
    }

    Control Encabezado()
    {
        // el método a la izquierda y la URL ocupando todo lo demás: es el renglón que más se mira
        var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(_método, 0);
        Grid.SetColumn(_url, 1);
        _url.Margin = new Thickness(8, 0, 0, 0);
        grilla.Children.Add(_método);
        grilla.Children.Add(_url);
        return grilla;
    }

    /// <summary>El método rota con un toque en vez de abrir un desplegable: son siete valores y
    /// el que se usa siempre está a uno o dos toques.</summary>
    void RotarMétodo()
    {
        var i = Array.IndexOf(Métodos, _request.Method);
        _request.Method = Métodos[(i + 1) % Métodos.Length];
        _método.Content = _request.Method;
        Ensuciar();
    }

    void ArmarSolapas()
    {
        _solapas.Children.Clear();
        foreach (var nombre in Secciones)
        {
            var cual = nombre;
            _solapas.Children.Add(Ui.Opcion(nombre, nombre == _seccionActiva, () => MostrarSección(cual)));
        }
    }

    void MostrarSección(string nombre)
    {
        _seccionActiva = nombre;
        ArmarSolapas();
        _sección.Content = nombre switch
        {
            "Params" => new KeyValueEditor(_request.QueryParams, Ensuciar, "parámetro", "valor"),
            "Cabeceras" => new KeyValueEditor(_request.Headers, Ensuciar, "cabecera", "valor"),
            "Auth" => SecciónAuth(),
            "Cuerpo" => SecciónCuerpo(),
            _ => SecciónScripts()
        };
    }

    // ----- Auth -----

    Control SecciónAuth()
    {
        var pila = new StackPanel { Spacing = 10 };
        var tipos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var tipo in new[] { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey })
        {
            var cual = tipo;
            tipos.Children.Add(Ui.Opcion(Nombre(tipo), _request.Auth.Type == tipo, () =>
            {
                _request.Auth.Type = cual;
                Ensuciar();
                MostrarSección("Auth");
            }));
        }

        pila.Children.Add(new ScrollViewer
        {
            Content = tipos,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        });

        switch (_request.Auth.Type)
        {
            case AuthType.Bearer:
                pila.Children.Add(Ui.Rotulo("Token"));
                pila.Children.Add(Atado(_request.Auth.BearerToken, "{{token}}",
                    v => _request.Auth.BearerToken = v));
                break;

            case AuthType.Basic:
                pila.Children.Add(Ui.Rotulo("Usuario"));
                pila.Children.Add(Atado(_request.Auth.Username, "usuario",
                    v => _request.Auth.Username = v));
                pila.Children.Add(Ui.Rotulo("Contraseña"));
                pila.Children.Add(Atado(_request.Auth.Password, "contraseña",
                    v => _request.Auth.Password = v));
                break;

            case AuthType.ApiKey:
                pila.Children.Add(Ui.Rotulo("Nombre"));
                pila.Children.Add(Atado(_request.Auth.ApiKeyName, "X-Api-Key",
                    v => _request.Auth.ApiKeyName = v));
                pila.Children.Add(Ui.Rotulo("Valor"));
                pila.Children.Add(Atado(_request.Auth.ApiKeyValue, "{{apiKey}}",
                    v => _request.Auth.ApiKeyValue = v));
                pila.Children.Add(Ui.Barra(
                    Ui.Opcion("En header", _request.Auth.ApiKeyIn != "query", () =>
                    {
                        _request.Auth.ApiKeyIn = "header";
                        Ensuciar();
                        MostrarSección("Auth");
                    }),
                    Ui.Opcion("En query", _request.Auth.ApiKeyIn == "query", () =>
                    {
                        _request.Auth.ApiKeyIn = "query";
                        Ensuciar();
                        MostrarSección("Auth");
                    })));
                break;

            case AuthType.Inherit:
                pila.Children.Add(Ui.Parrafo(
                    $"Usa la autenticación de «{_colección.Name}».", Ui.Tenue, 12));
                break;
        }

        return pila;
    }

    static string Nombre(AuthType tipo) => tipo switch
    {
        AuthType.Inherit => "Heredada",
        AuthType.None => "Ninguna",
        AuthType.ApiKey => "API Key",
        _ => tipo.ToString()
    };

    // ----- Cuerpo -----

    Control SecciónCuerpo()
    {
        var pila = new StackPanel { Spacing = 10 };
        var tipos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        foreach (var tipo in new[] { BodyType.None, BodyType.Json, BodyType.Text, BodyType.FormUrlEncoded })
        {
            var cual = tipo;
            tipos.Children.Add(Ui.Opcion(Nombre(tipo), _request.Body.Type == tipo, () =>
            {
                _request.Body.Type = cual;
                Ensuciar();
                MostrarSección("Cuerpo");
            }));
        }
        pila.Children.Add(tipos);

        if (_request.Body.Type == BodyType.FormUrlEncoded)
        {
            pila.Children.Add(new KeyValueEditor(_request.Body.FormItems, Ensuciar, "campo", "valor"));
        }
        else if (_request.Body.Type != BodyType.None)
        {
            var texto = Ui.Campo(_request.Body.Raw, "{ }", multilinea: true, mono: true);
            texto.TextChanged += (_, _) => { _request.Body.Raw = texto.Text ?? ""; Ensuciar(); };
            pila.Children.Add(texto);

            if (_request.Body.Type == BodyType.Json)
            {
                pila.Children.Add(Ui.Accion("Formatear JSON", () =>
                {
                    var lindo = Formatear(_request.Body.Raw);
                    if (lindo == null) return;
                    _request.Body.Raw = lindo;
                    texto.Text = lindo;
                    Ensuciar();
                }));
            }
        }

        return pila;
    }

    static string Nombre(BodyType tipo) => tipo switch
    {
        BodyType.None => "Ninguno",
        BodyType.FormUrlEncoded => "Form",
        _ => tipo.ToString()
    };

    // ----- Scripts -----

    Control SecciónScripts()
    {
        var pre = Ui.Campo(_request.PreRequestScript, "er.setVar(\"x\", 1)", multilinea: true, mono: true);
        pre.TextChanged += (_, _) => { _request.PreRequestScript = pre.Text ?? ""; Ensuciar(); };

        var post = Ui.Campo(_request.TestScript, "er.test(\"ok\", er.response.status === 200)",
            multilinea: true, mono: true);
        post.TextChanged += (_, _) => { _request.TestScript = post.Text ?? ""; Ensuciar(); };

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Ui.Rotulo("Pre-request"),
                pre,
                Ui.Rotulo("Post-response"),
                post
            }
        };
    }

    // ----- Guardar -----

    void Ensuciar()
    {
        _sucio = true;
        _guardar.IsEnabled = true;
        _guardar.Content = "Guardar •";
    }

    void Guardar()
    {
        try
        {
            Storage.SaveCollection(_colección);
            _sucio = false;
            _guardar.IsEnabled = false;
            _guardar.Content = "Guardar";
            _alGuardar();
        }
        catch (Exception ex)
        {
            _estado.Text = $"No se pudo guardar: {ex.Message}";
            _estado.Foreground = Ui.Rojo;
        }
    }

    public bool TieneCambiosSinGuardar => _sucio;

    // ----- Enviar -----

    async Task EnviarAsync()
    {
        _enviar.IsEnabled = false;
        _estado.Text = "Enviando…";
        _estado.Foreground = Ui.Tenue;

        try
        {
            // la colección va como dueña, así que valen las cabeceras y la auth heredadas: sin
            // eso, la misma request anda en el escritorio y falla en el teléfono
            _última = await HttpExecutor.ExecuteAsync(_request, _colección, _ambiente());

            if (_última.Error != null)
            {
                _estado.Text = $"Error: {_última.Error}";
                _estado.Foreground = Ui.Rojo;
            }
            else
            {
                _estado.Text = $"{_última.StatusCode} {_última.StatusText} · " +
                               $"{_última.ElapsedMs} ms · {_última.SizeBytes} bytes";
                _estado.Foreground = _última.StatusCode < 400 ? Ui.Verde : Ui.Rojo;
            }

            MostrarVista(_vistaActiva);
        }
        catch (Exception ex)
        {
            _estado.Text = $"Falló: {ex.Message}";
            _estado.Foreground = Ui.Rojo;
        }
        finally
        {
            _enviar.IsEnabled = true;
        }
    }

    Control SelectorDeVista()
    {
        var fila = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var nombre in new[] { "Cuerpo", "Cabeceras", "Tests" })
        {
            var cual = nombre;
            fila.Children.Add(Ui.Opcion(nombre, nombre == _vistaActiva, () => MostrarVista(cual)));
        }
        return fila;
    }

    void MostrarVista(string cual)
    {
        _vistaActiva = cual;
        _respuesta.Children[1] = SelectorDeVista();

        if (_última == null)
        {
            _vistaRespuesta.Content = Ui.Rotulo("Todavía no mandaste nada.");
            return;
        }

        _vistaRespuesta.Content = cual switch
        {
            "Cabeceras" => Ui.Parrafo(_última.HeadersText, Ui.Tenue, 12),
            "Tests" => Ui.Parrafo(TextoDeTests(_última), Ui.Tenue, 12),
            _ => Cuerpo(_última)
        };
    }

    Control Cuerpo(ResponseResult resultado)
    {
        var cuerpo = EsJson(resultado) ? Formatear(resultado.Body) ?? resultado.Body : resultado.Body;

        // pintar un JSON de un mega en un teléfono no ayuda a nadie: se corta y se avisa
        var recortado = cuerpo.Length > 8000;
        var texto = Ui.Parrafo(recortado ? cuerpo[..8000] + "\n… (cortado)" : cuerpo, Ui.Normal, 12);
        texto.FontFamily = "monospace";

        var copiar = Ui.AccionAsync("Copiar", async () =>
        {
            var portapapeles = TopLevel.GetTopLevel(this)?.Clipboard;
            if (portapapeles != null) await portapapeles.SetTextAsync(cuerpo);
        });

        return new StackPanel
        {
            Spacing = 8,
            Children = { Ui.Barra(copiar), texto }
        };
    }

    static bool EsJson(ResponseResult resultado) =>
        resultado.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

    static string TextoDeTests(ResponseResult resultado)
    {
        var texto = new StringBuilder();
        foreach (var test in resultado.ScriptTests ?? new())
            texto.AppendLine($"{(test.Passed ? "✔" : "✘")} {test.Name}" +
                             $"{(test.Error is { } x ? $" — {x}" : "")}");
        if (resultado.ScriptError != null) texto.AppendLine($"Script: {resultado.ScriptError}");
        if (!string.IsNullOrWhiteSpace(resultado.ScriptLog)) texto.Append(resultado.ScriptLog);
        return texto.Length == 0 ? "Esta request no tiene tests." : texto.ToString().TrimEnd();
    }

    /// <summary>Indenta un JSON. Devuelve null si no lo es: se usa tanto para el botón del cuerpo
    /// como para la respuesta, y en los dos casos el original sirve más que un error.</summary>
    static string? Formatear(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        try
        {
            // se escribe con Utf8JsonWriter y no con JsonSerializer a propósito: el serializador
            // resuelve por reflexión y este head corre con el trimming del SDK activado, que es
            // justo donde esas cosas se rompen en silencio (ver docs/ANDROID.md)
            using var doc = JsonDocument.Parse(texto);
            using var buffer = new MemoryStream();
            using (var escritor = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
                doc.WriteTo(escritor);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
