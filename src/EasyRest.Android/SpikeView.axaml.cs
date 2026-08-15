using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Models;
using EasyRest.Services;

namespace EasyRest.Android;

/// <summary>La pantalla del spike. Manda una request de verdad con el HttpExecutor del Core y
/// corre un script con Jint sobre la respuesta, que son las dos piezas que había que ver
/// funcionando en un teléfono. También chequea que la carpeta de datos de la app sea escribible,
/// que es de donde va a leer el Storage.</summary>
public partial class SpikeView : UserControl
{
    readonly EnvironmentModel _env = new() { Name = "Spike" };

    public SpikeView()
    {
        InitializeComponent();

        _env.Variables.Add(new KeyValueItem { Key = "host", Value = "https://api.github.com" });

        ScriptBox.Text = """
            er.test("responde 200", er.response.status === 200);
            er.test("el cuerpo no viene vacío", er.response.body.length > 0);
            er.setVar("ultimoEstado", String(er.response.status));
            console.log("el script corrió en el teléfono");
            """;

        EnviarBtn.Click += EnviarAsync;
        EntornoTexto.Text = DescribirEntorno();
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
        EnviarBtn.IsEnabled = false;
        EstadoTexto.Text = "Enviando…";
        TestsTexto.Text = "";
        CuerpoTexto.Text = "";

        try
        {
            var request = new RequestItem
            {
                Name = "spike",
                Method = "GET",
                Url = UrlBox.Text ?? "",
                TestScript = ScriptBox.Text ?? ""
            };

            var resultado = await HttpExecutor.ExecuteAsync(request, null, _env);

            EstadoTexto.Text = resultado.Error != null
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
            TestsTexto.Text = tests.ToString();

            CuerpoTexto.Text = resultado.Body.Length > 2000
                ? resultado.Body[..2000] + "\n…"
                : resultado.Body;
        }
        catch (Exception ex)
        {
            EstadoTexto.Text = $"Falló: {ex.Message}";
        }
        finally
        {
            EnviarBtn.IsEnabled = true;
        }
    }
}
