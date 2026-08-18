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
    readonly ContentControl _respuesta = new();

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

        _url = Ui.Campo(request.Url, "https://…", mono: true);
        _url.TextChanged += (_, _) => { _request.Url = _url.Text ?? ""; Ensuciar(); };

        _método = MétodoBoton();
        _enviar = Ui.PrimarioAsync("Enviar", Iconos.Enviar, EnviarAsync);
        _guardar = Ui.BotonIcono(Iconos.Guardar, Guardar, Ui.Amarillo, Ui.Superficie);
        // del alto y el radio del primario: los dos son el pie, y un cuadrado de 48 al lado de una
        // barra de 52 se lee como si uno estuviera flotando
        _guardar.MinHeight = 52;
        _guardar.Width = 56;
        _guardar.CornerRadius = new CornerRadius(12);
        _guardar.IsEnabled = false;

        _respuesta.Content = Reposo();
        ArmarSolapas();
        MostrarSección(_seccionActiva);

        var pila = new StackPanel
        {
            Margin = new Thickness(16, 12, 16, 16),
            Spacing = 14,
            Children =
            {
                Encabezado(),
                Solapas(),
                _sección,
                _respuesta
            }
        };

        var raíz = new DockPanel();
        var pie = Pie();
        DockPanel.SetDock(pie, Dock.Bottom);
        raíz.Children.Add(pie);
        raíz.Children.Add(new ScrollViewer { Content = pila });
        Content = raíz;
    }

    // ----- Encabezado -----

    Control Encabezado()
    {
        var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(_método, 0);
        Grid.SetColumn(_url, 1);
        _url.Margin = new Thickness(8, 0, 0, 0);
        grilla.Children.Add(_método);
        grilla.Children.Add(_url);

        return new StackPanel
        {
            Spacing = 8,
            Children = { Ui.Nota(_colección.Name), grilla }
        };
    }

    /// <summary>El método rota con un toque en vez de abrir un desplegable: son siete valores, el
    /// que se usa siempre está a uno o dos toques, y el color cambia con él.</summary>
    Button MétodoBoton()
    {
        var boton = new Button
        {
            Padding = new Thickness(12, 0),
            MinHeight = 44,
            MinWidth = 92,
            CornerRadius = new CornerRadius(10),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        boton.Click += (_, _) =>
        {
            var i = Array.IndexOf(Métodos, _request.Method);
            _request.Method = Métodos[(i + 1) % Métodos.Length];
            PintarMétodo(boton);
            Ensuciar();
        };
        PintarMétodo(boton);
        return boton;
    }

    void PintarMétodo(Button boton)
    {
        var color = Ui.ColorDeMetodo(_request.Method);
        boton.Background = Ui.Tinte(color);
        boton.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = _request.Method,
                    FontSize = 13,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(color),
                    VerticalAlignment = VerticalAlignment.Center
                },
                Ui.IconoDeTexto(Iconos.ChevronAbajo, 12, new SolidColorBrush(color))
            }
        };
    }

    // ----- Solapas -----

    Control Solapas() => new ScrollViewer
    {
        Content = _solapas,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };

    void ArmarSolapas()
    {
        _solapas.Children.Clear();
        foreach (var nombre in Secciones)
        {
            var cual = nombre;
            _solapas.Children.Add(Ui.Pastilla(nombre, nombre == _seccionActiva, () => MostrarSección(cual)));
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
        var pila = new StackPanel { Spacing = 12 };
        var tipos = Ui.Pastillas();

        foreach (var tipo in new[] { AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey })
        {
            var cual = tipo;
            tipos.Children.Add(Ui.Pastilla(Nombre(tipo), _request.Auth.Type == tipo, () =>
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
                pila.Children.Add(Ui.Tarjeta(10,
                    Ui.Nota("Token"),
                    Atado(_request.Auth.BearerToken, "{{token}}", v => _request.Auth.BearerToken = v)));
                break;

            case AuthType.Basic:
                pila.Children.Add(Ui.Tarjeta(10,
                    Ui.Nota("Usuario"),
                    Atado(_request.Auth.Username, "usuario", v => _request.Auth.Username = v),
                    Ui.Nota("Contraseña"),
                    Atado(_request.Auth.Password, "contraseña", v => _request.Auth.Password = v)));
                break;

            case AuthType.ApiKey:
                pila.Children.Add(Ui.Tarjeta(10,
                    Ui.Nota("Nombre"),
                    Atado(_request.Auth.ApiKeyName, "X-Api-Key", v => _request.Auth.ApiKeyName = v),
                    Ui.Nota("Valor"),
                    Atado(_request.Auth.ApiKeyValue, "{{apiKey}}", v => _request.Auth.ApiKeyValue = v),
                    Ui.Barra(
                        Ui.Pastilla("En header", _request.Auth.ApiKeyIn != "query", () => ApiKeyEn("header")),
                        Ui.Pastilla("En query", _request.Auth.ApiKeyIn == "query", () => ApiKeyEn("query")))));
                break;

            case AuthType.Inherit:
                pila.Children.Add(Ui.Aviso($"Usa la autenticación de «{_colección.Name}».",
                    Ui.CTenue, Iconos.Candado));
                break;
        }

        return pila;
    }

    void ApiKeyEn(string dónde)
    {
        _request.Auth.ApiKeyIn = dónde;
        Ensuciar();
        MostrarSección("Auth");
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
        var pila = new StackPanel { Spacing = 12 };
        var tipos = Ui.Pastillas();

        foreach (var tipo in new[] { BodyType.None, BodyType.Json, BodyType.Text, BodyType.FormUrlEncoded })
        {
            var cual = tipo;
            tipos.Children.Add(Ui.Pastilla(Nombre(tipo), _request.Body.Type == tipo, () =>
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
                var formatear = Ui.Enlace("Formatear JSON", null, () =>
                {
                    var lindo = Formatear(_request.Body.Raw);
                    if (lindo == null) return;
                    _request.Body.Raw = lindo;
                    texto.Text = lindo;
                    Ensuciar();
                });
                pila.Children.Add(formatear);
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
            Children = { Ui.Nota("Pre-request"), pre, Ui.Nota("Post-response"), post }
        };
    }

    Control Atado(string valor, string marca, Action<string> asignar)
    {
        var campo = Ui.Campo(valor, marca, mono: true);
        campo.TextChanged += (_, _) => { asignar(campo.Text ?? ""); Ensuciar(); };
        return campo;
    }

    // ----- Guardar y enviar -----

    Control Pie()
    {
        var grilla = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(_guardar, 0);
        Grid.SetColumn(_enviar, 1);
        _enviar.Margin = new Thickness(10, 0, 0, 0);
        grilla.Children.Add(_guardar);
        grilla.Children.Add(_enviar);

        return new Border
        {
            Background = Ui.Panel,
            BorderBrush = Ui.Superficie,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12),
            Child = grilla
        };
    }

    void Ensuciar()
    {
        _sucio = true;
        _guardar.IsEnabled = true;
        _guardar.Background = Ui.Tinte(Ui.CAmarillo, 0.22);
    }

    void Guardar()
    {
        try
        {
            Storage.SaveCollection(_colección);
            _sucio = false;
            _guardar.IsEnabled = false;
            _guardar.Background = Ui.Superficie;
            _alGuardar();
        }
        catch (Exception ex)
        {
            _respuesta.Content = Ui.Aviso($"No se pudo guardar: {ex.Message}", Ui.CRojo);
        }
    }

    public bool TieneCambiosSinGuardar => _sucio;

    async Task EnviarAsync()
    {
        _enviar.IsEnabled = false;
        _respuesta.Content = Ui.Tarjeta(Ui.Parrafo("Enviando…", Ui.Subtexto, 14));

        try
        {
            // la colección va como dueña, así que valen las cabeceras y la auth heredadas: sin
            // eso, la misma request anda en el escritorio y falla en el teléfono
            _última = await HttpExecutor.ExecuteAsync(_request, _colección, _ambiente());
            MostrarVista(_vistaActiva);
        }
        catch (Exception ex)
        {
            _respuesta.Content = Ui.Aviso($"Falló: {ex.Message}", Ui.CRojo);
        }
        finally
        {
            _enviar.IsEnabled = true;
        }
    }

    // ----- Respuesta -----

    static Control Reposo() => Ui.Tarjeta(Ui.Parrafo("Todavía no mandaste nada.", Ui.Tenue, 13));

    void MostrarVista(string cual)
    {
        _vistaActiva = cual;
        if (_última == null) { _respuesta.Content = Reposo(); return; }

        if (_última.Error != null)
        {
            _respuesta.Content = Ui.Aviso(_última.Error, Ui.CRojo);
            return;
        }

        var color = Ui.ColorDeEstado(_última.StatusCode);
        var cabecera = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var estado = Ui.Estado($"{_última.StatusCode} {_última.StatusText}", color);
        var números = new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            Children =
            {
                new TextBlock { Text = $"{_última.ElapsedMs} ms", FontSize = 13, Foreground = Ui.Normal },
                new TextBlock
                {
                    Text = $"{_última.SizeBytes} bytes · {_última.ContentType ?? "sin tipo"}",
                    FontSize = 11,
                    Foreground = Ui.Tenue,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            }
        };
        var copiar = Ui.BotonIcono(Iconos.Copiar, async () =>
        {
            var portapapeles = TopLevel.GetTopLevel(this)?.Clipboard;
            if (portapapeles != null) await portapapeles.SetTextAsync(CuerpoLindo(_última));
        }, Ui.Subtexto, Ui.Superficie);

        Grid.SetColumn(estado, 0);
        Grid.SetColumn(números, 1);
        Grid.SetColumn(copiar, 2);
        cabecera.Children.Add(estado);
        cabecera.Children.Add(números);
        cabecera.Children.Add(copiar);

        var vistas = Ui.Pastillas();
        foreach (var nombre in new[] { "Cuerpo", "Cabeceras", "Tests" })
        {
            var cualVista = nombre;
            vistas.Children.Add(Ui.Pastilla(Etiqueta(nombre), nombre == _vistaActiva,
                () => MostrarVista(cualVista)));
        }

        var texto = cual switch
        {
            "Cabeceras" => _última.HeadersText,
            "Tests" => TextoDeTests(_última),
            _ => Recortar(CuerpoLindo(_última))
        };

        var visor = new Border
        {
            Background = Ui.Corteza,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Child = Ui.Mono(texto, cual == "Cuerpo" ? Ui.Normal : Ui.Subtexto)
        };

        _respuesta.Content = Ui.Tarjeta(10, cabecera, vistas, visor);
    }

    string Etiqueta(string vista)
    {
        if (vista != "Tests" || _última?.ScriptTests is not { Count: > 0 } tests) return vista;
        return $"Tests {tests.Count(t => t.Passed)}/{tests.Count}";
    }

    static string CuerpoLindo(ResponseResult resultado) =>
        resultado.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true
            ? Formatear(resultado.Body) ?? resultado.Body
            : resultado.Body;

    /// <summary>Pintar un JSON de un mega en un teléfono no ayuda a nadie.</summary>
    static string Recortar(string texto) =>
        texto.Length > 8000 ? texto[..8000] + "\n… (cortado)" : texto;

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
