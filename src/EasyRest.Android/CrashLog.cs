using System.Text;

namespace EasyRest.Android;

/// <summary>Deja la última excepción no manejada en un archivo, para poder mostrarla la próxima
/// vez que la app abra. Sin esto, un crash en un teléfono sin cable es una pantalla que se cierra
/// y nada más: el spike existe justo para no quedarse con eso.</summary>
static class CrashLog
{
    static string Ruta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyRest", "crash.txt");

    public static void Registrar(Exception? ex, string dónde)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, $"{DateTime.Now:u} · {dónde}\n\n{Describir(ex)}");
        }
        catch
        {
            // si ni siquiera se puede escribir el crash, no hay nada más que hacer acá
        }
    }

    /// <summary>Devuelve el crash anterior y lo borra, así se muestra una sola vez.</summary>
    public static string? LeerYBorrar()
    {
        try
        {
            if (!File.Exists(Ruta)) return null;
            var texto = File.ReadAllText(Ruta);
            File.Delete(Ruta);
            return texto;
        }
        catch { return null; }
    }

    /// <summary>Las inner exceptions importan: el error real suele estar tres niveles abajo.</summary>
    public static string Describir(Exception ex)
    {
        var texto = new StringBuilder();
        for (var actual = ex; actual != null; actual = actual.InnerException)
        {
            texto.AppendLine($"{actual.GetType().FullName}: {actual.Message}");
            texto.AppendLine(actual.StackTrace);
            if (actual.InnerException != null) texto.AppendLine("--- causada por ---");
        }
        return texto.ToString();
    }
}
