using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using EasyRest.Models;
using EasyRest.Services;

// Los proyectos de Android traen Android.Widget en los implicit usings y ahí también hay un
// Button, así que el nombre queda ambiguo. Mismo caso que Application en App.cs.
using Button = Avalonia.Controls.Button;

namespace EasyRest.Android;

/// <summary>La pantalla del spike. Manda una request de verdad con el HttpExecutor del Core y
/// corre un script con Jint sobre la respuesta, que son las dos piezas que había que ver
/// funcionando en un teléfono. También chequea que la carpeta de datos de la app sea escribible,
/// que es de donde va a leer el Storage.
///
/// Armada en C# y no en XAML por el bug de build que documenta docs/ANDROID.md.</summary>
public class SpikeView : UserControl
{
    // los mismos colores que el escritorio (catppuccin), para que se vea como la app
    static readonly IBrush Fondo = new SolidColorBrush(Color.Parse("#1E1E2E"));
    static readonly IBrush Panel = new SolidColorBrush(Color.Parse("#272739"));
    static readonly IBrush Acento = new SolidColorBrush(Color.Parse("#89B4FA"));
    static readonly IBrush Tenue = new SolidColorBrush(Color.Parse("#9399B2"));
    static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#CDD6F4"));
    static readonly IBrush Verde = new SolidColorBrush(Color.Parse("#A6E3A1"));
    static readonly IBrush Amarillo = new SolidColorBrush(Color.Parse("#F9E2AF"));

    readonly EnvironmentModel _env = new() { Name = "Spike" };

    readonly TextBlock _entorno = Parrafo(Normal, 13);
    readonly TextBlock _estado = new() { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Verde };
    readonly TextBlock _tests = Parrafo(Amarillo, 13);
    readonly TextBlock _cuerpo = Parrafo(Normal, 12, mono: true);

    readonly TextBox _url = new() { Text = "https://api.github.com/zen", FontSize = 15 };
    readonly TextBox _script = new()
    {
        AcceptsReturn = true,
        Height = 120,
        FontSize = 13,
        FontFamily = "monospace",
        Text = """
            er.test("responde 200", er.response.status === 200);
            er.test("el cuerpo no viene vacío", er.response.body.length > 0);
            er.setVar("ultimoEstado", String(er.response.status));
            console.log("el script corrió en el teléfono");
            """
    };

    readonly Button _enviar = new()
    {
        Content = "Enviar",
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Padding = new Thickness(0, 14),
        FontSize = 16
    };

    public SpikeView()
    {
        // los campos de arriba ya se inicializaron: si el rastro se corta antes de esta miga,
        // el problema está en construir alguno de los controles
        Diag.Marcar("SpikeView: controles creados");

        _env.Variables.Add(new KeyValueItem { Key = "host", Value = "https://api.github.com" });

        // Pantalla de diagnóstico, no de producto: la pregunta del spike es si el Core funciona
        // en un teléfono, así que lo que se ve son las respuestas a esa pregunta.
        Content = new ScrollViewer
        {
            Background = Fondo,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "EasyRest · spike Android",
                        FontSize = 20,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Acento
                    },
                    Tarjeta(Rotulo("Entorno"), _entorno),
                    Rotulo("URL"),
                    _url,
                    Rotulo("Script post-respuesta (Jint)"),
                    _script,
                    _enviar,
                    Tarjeta(_estado, _tests, _cuerpo),
                }
            }
        };

        Diag.Marcar("SpikeView: árbol visual armado");

        _enviar.Click += EnviarAsync;
        _entorno.Text = DescribirEntorno();

        Diag.Marcar("SpikeView: lista");
    }

    static TextBlock Parrafo(IBrush color, double tamaño, bool mono = false) => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = tamaño,
        Foreground = color,
        FontFamily = mono ? "monospace" : FontFamily.Default
    };

    static TextBlock Rotulo(string texto) => new() { Text = texto, FontSize = 13, Foreground = Tenue };

    static Border Tarjeta(params Control[] hijos)
    {
        var pila = new StackPanel { Spacing = 8 };
        foreach (var hijo in hijos) pila.Children.Add(hijo);
        return new Border { Background = Panel, CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = pila };
    }

    /// <summary>Lo que hay que saber apenas abre la app: si el runtime tiene JIT (Android sí,
    /// iOS no) y si la carpeta privada de la app se puede leer y escribir.</summary>
    static string DescribirEntorno()
    {
        var texto = new StringBuilder();
        texto.AppendLine($"SO: {Environment.OSVersion}");
        texto.AppendLine($"Arquitectura: {RuntimeInformation.OSArchitecture}");
        texto.AppendLine($"Genera código en runtime: {RuntimeFeature.IsDynamicCodeSupported}");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        texto.AppendLine($"Datos de la app: {appData}");

        try
        {
            var prueba = Path.Combine(appData, "EasyRest", "spike.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(prueba)!);
            File.WriteAllText(prueba, "ok");
            texto.Append(File.ReadAllText(prueba) == "ok"
                ? "Almacenamiento: escribe y lee ✔"
                : "Almacenamiento: leyó algo distinto ✘");
        }
        catch (Exception ex)
        {
            texto.Append($"Almacenamiento: falló ✘ ({ex.Message})");
        }

        return texto.ToString();
    }

    async void EnviarAsync(object? sender, RoutedEventArgs e)
    {
        _enviar.IsEnabled = false;
        _estado.Text = "Enviando…";
        _tests.Text = "";
        _cuerpo.Text = "";

        try
        {
            var request = new RequestItem
            {
                Name = "spike",
                Method = "GET",
                Url = _url.Text ?? "",
                TestScript = _script.Text ?? ""
            };

            var resultado = await HttpExecutor.ExecuteAsync(request, null, _env);

            _estado.Text = resultado.Error != null
                ? $"Error: {resultado.Error}"
                : $"{resultado.StatusCode} {resultado.StatusText} · {resultado.ElapsedMs} ms · {resultado.SizeBytes} bytes";

            var tests = new StringBuilder();
            foreach (var test in resultado.ScriptTests ?? new())
                tests.AppendLine($"{(test.Passed ? "✔" : "✘")} {test.Name}{(test.Error is { } x ? $" — {x}" : "")}");
            if (resultado.ScriptError != null) tests.AppendLine($"Script: {resultado.ScriptError}");
            if (!string.IsNullOrWhiteSpace(resultado.ScriptLog)) tests.Append(resultado.ScriptLog);
            // si la variable quedó escrita, el ida y vuelta con el ambiente también anduvo
            var ultimo = _env.Variables.FirstOrDefault(v => v.Key == "ultimoEstado")?.Value;
            if (ultimo != null) tests.AppendLine($"Variable guardada por el script: ultimoEstado = {ultimo}");
            _tests.Text = tests.ToString();

            _cuerpo.Text = resultado.Body.Length > 2000
                ? resultado.Body[..2000] + "\n…"
                : resultado.Body;
        }
        catch (Exception ex)
        {
            _estado.Text = $"Falló: {ex.Message}";
        }
        finally
        {
            _enviar.IsEnabled = true;
        }
    }
}
