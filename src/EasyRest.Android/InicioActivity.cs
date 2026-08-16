using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Android.App;
using Android.Content;
using Android.Widget;

namespace EasyRest.Android;

/// <summary>La pantalla de arranque, en Android puro y sin una sola línea de Avalonia.
///
/// El motivo es concreto: el spike se cerraba al instante y una pantalla de error hecha con
/// Avalonia no servía, porque el crash pasaba antes de que Avalonia llegara a dibujar. Esta
/// actividad no depende de nada de eso, así que la app siempre abre y siempre puede contar qué
/// pasó en el intento anterior.</summary>
[Activity(
    Name = "com.rentlysoft.easyrest.InicioActivity",
    Label = "EasyRest",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/EasyRestTheme")]
public class InicioActivity : Activity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var informe = Informe();

        var abrir = new Button(this) { Text = "Abrir el spike (Avalonia)" };
        abrir.Click += (_, _) =>
        {
            Diag.Limpiar();
            Diag.Marcar("inicio: lanzando MainActivity");
            StartActivity(new Intent(this, typeof(MainActivity)));
        };

        var compartir = new Button(this) { Text = "Compartir este informe" };
        compartir.Click += (_, _) =>
        {
            var envío = new Intent(Intent.ActionSend).SetType("text/plain");
            envío.PutExtra(Intent.ExtraSubject, "EasyRest · diagnóstico del spike");
            envío.PutExtra(Intent.ExtraText, informe);
            StartActivity(Intent.CreateChooser(envío, "Compartir el informe")!);
        };

        var texto = new TextView(this) { Text = informe, TextSize = 11 };
        texto.SetTextIsSelectable(true);
        texto.SetTypeface(global::Android.Graphics.Typeface.Monospace, global::Android.Graphics.TypefaceStyle.Normal);

        var columna = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        columna.SetPadding(28, 28, 28, 28);
        columna.AddView(abrir);
        columna.AddView(compartir);
        columna.AddView(texto);

        var scroll = new ScrollView(this);
        scroll.AddView(columna);
        SetContentView(scroll);
    }

    /// <summary>Lo del intento anterior primero, porque es lo que se vino a buscar.</summary>
    string Informe()
    {
        var texto = new StringBuilder();

        var crash = Diag.Crash();
        var trace = Diag.Trace();

        if (crash.Length > 0)
        {
            texto.AppendLine("=== SE CAYÓ ===");
            texto.AppendLine(crash);
        }

        if (trace.Length > 0)
        {
            texto.AppendLine("=== HASTA DÓNDE LLEGÓ ===");
            texto.AppendLine(trace);
            if (crash.Length == 0)
                texto.AppendLine("(sin excepción registrada: si el rastro se corta antes del final,\n"
                                 + " el proceso murió sin poder contarlo — típico de un crash nativo)");
        }

        if (crash.Length == 0 && trace.Length == 0)
            texto.AppendLine("Todavía no hay nada del spike. Tocá el botón de arriba.");

        texto.AppendLine();
        texto.AppendLine("=== ENTORNO ===");
        texto.AppendLine($"SO: {Environment.OSVersion}");
        texto.AppendLine($"Arquitectura: {RuntimeInformation.OSArchitecture}");
        texto.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
        texto.AppendLine($"Genera código en runtime: {RuntimeFeature.IsDynamicCodeSupported}");
        texto.AppendLine($"Datos de la app: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}");

        return texto.ToString();
    }
}
