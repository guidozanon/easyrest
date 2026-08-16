using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

using Button = Avalonia.Controls.Button;
using Orientation = Avalonia.Layout.Orientation;

namespace EasyRest.Android;

/// <summary>Una request de la colección, con lo justo para mandarla desde el teléfono: método, URL,
/// cabeceras, cuerpo y la respuesta.
///
/// Deliberadamente no edita ni guarda: en móvil lo que se hace es correr requests que ya existen,
/// no armarlas. Los cambios acá son para esta corrida y no vuelven al disco ni al server, así que
/// no hay forma de romper sin querer una colección compartida desde el colectivo.</summary>
public class RequestView : UserControl
{
    readonly RequestItem _request;
    readonly EnvironmentModel _ambiente;

    readonly TextBox _url;
    readonly TextBox _cuerpo;
    readonly TextBlock _estado = new() { FontSize = 15, FontWeight = FontWeight.SemiBold };
    readonly TextBlock _tests = ShellView.Parrafo("", ShellView.ColorTenue, 12);
    readonly TextBlock _respuesta = ShellView.Parrafo("", ShellView.ColorNormal, 12);
    readonly Button _enviar = new()
    {
        Content = "Enviar",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(0, 14),
        FontSize = 16
    };

    public RequestView(RequestItem request, EnvironmentModel ambiente)
    {
        _request = request;
        _ambiente = ambiente;

        _url = new TextBox { Text = request.Url, FontSize = 14 };
        _cuerpo = new TextBox
        {
            Text = request.Body.Raw,
            AcceptsReturn = true,
            Height = 110,
            FontSize = 12,
            FontFamily = "monospace",
            IsVisible = TieneCuerpo(request)
        };
        _respuesta.FontFamily = "monospace";

        var pila = new StackPanel
        {
            Margin = new Thickness(14, 0, 14, 14),
            Spacing = 10,
            Children =
            {
                ShellView.Rotulo($"{request.Method} · ambiente «{ambiente.Name}»"),
                _url
            }
        };

        var cabeceras = Cabeceras(request);
        if (cabeceras != null)
        {
            pila.Children.Add(ShellView.Rotulo("Cabeceras"));
            pila.Children.Add(cabeceras);
        }

        if (_cuerpo.IsVisible)
        {
            pila.Children.Add(ShellView.Rotulo("Cuerpo"));
            pila.Children.Add(_cuerpo);
        }

        pila.Children.Add(_enviar);
        pila.Children.Add(ShellView.Tarjeta(_estado, _tests, _respuesta));

        _enviar.Click += async (_, _) => await EnviarAsync();
        Content = new ScrollViewer { Content = pila };
    }

    static bool TieneCuerpo(RequestItem request) => request.Body.Type != BodyType.None;

    /// <summary>Sólo lectura: los valores se muestran resueltos contra el ambiente, que es lo que
    /// de verdad se va a mandar.</summary>
    Control? Cabeceras(RequestItem request)
    {
        var activas = request.Headers.Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key)).ToList();
        if (activas.Count == 0) return null;

        var texto = new StringBuilder();
        foreach (var h in activas) texto.AppendLine($"{h.Key}: {h.Value}");

        return ShellView.Tarjeta(ShellView.Parrafo(texto.ToString().TrimEnd(),
            ShellView.ColorTenue, 12));
    }

    async Task EnviarAsync()
    {
        _enviar.IsEnabled = false;
        _estado.Text = "Enviando…";
        _estado.Foreground = ShellView.ColorTenue;
        _tests.Text = "";
        _respuesta.Text = "";

        try
        {
            // se clona lo editable de esta pantalla: la request de la colección no se toca
            var aEnviar = new RequestItem
            {
                Name = _request.Name,
                Method = _request.Method,
                Url = _url.Text ?? "",
                PreRequestScript = _request.PreRequestScript,
                TestScript = _request.TestScript,
                Auth = _request.Auth,
                // el cuerpo se clona: si se compartiera la instancia, editar acá cambiaría la
                // request de la colección, que es justo lo que esta pantalla promete no hacer
                Body = new BodyConfig
                {
                    Type = _request.Body.Type,
                    Raw = _cuerpo.IsVisible ? _cuerpo.Text ?? "" : _request.Body.Raw
                }
            };
            foreach (var item in _request.Body.FormItems) aEnviar.Body.FormItems.Add(item);
            foreach (var h in _request.Headers) aEnviar.Headers.Add(h);
            foreach (var q in _request.QueryParams) aEnviar.QueryParams.Add(q);

            var resultado = await HttpExecutor.ExecuteAsync(aEnviar, null, _ambiente);

            if (resultado.Error != null)
            {
                _estado.Text = $"Error: {resultado.Error}";
                _estado.Foreground = ShellView.ColorError;
            }
            else
            {
                _estado.Text = $"{resultado.StatusCode} {resultado.StatusText} · " +
                               $"{resultado.ElapsedMs} ms · {resultado.SizeBytes} bytes";
                _estado.Foreground = resultado.StatusCode < 400 ? ShellView.ColorOk : ShellView.ColorError;
            }

            var tests = new StringBuilder();
            foreach (var test in resultado.ScriptTests ?? new())
                tests.AppendLine($"{(test.Passed ? "✔" : "✘")} {test.Name}" +
                                 $"{(test.Error is { } x ? $" — {x}" : "")}");
            if (resultado.ScriptError != null) tests.AppendLine($"Script: {resultado.ScriptError}");
            if (!string.IsNullOrWhiteSpace(resultado.ScriptLog)) tests.Append(resultado.ScriptLog);
            _tests.Text = tests.ToString().TrimEnd();

            // el cuerpo se corta: pintar un JSON de un mega en un teléfono no ayuda a nadie
            _respuesta.Text = resultado.Body.Length > 4000
                ? resultado.Body[..4000] + "\n… (cortado)"
                : resultado.Body;
        }
        catch (Exception ex)
        {
            _estado.Text = $"Falló: {ex.Message}";
            _estado.Foreground = ShellView.ColorError;
        }
        finally
        {
            _enviar.IsEnabled = true;
        }
    }
}
