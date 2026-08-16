using System.Text;

namespace EasyRest.Android;

/// <summary>Migas de pan en disco. Una excepción manejada se puede atrapar y mostrar; un crash
/// nativo o una falla al inicializar el runtime no dejan nada. Lo que sí sobrevive es un archivo
/// con "hasta acá llegué", escrito paso a paso: al volver a abrir la app se ve dónde se cortó.</summary>
static class Diag
{
    static string Carpeta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyRest");

    static string RutaTrace => Path.Combine(Carpeta, "trace.txt");
    static string RutaCrash => Path.Combine(Carpeta, "crash.txt");

    public static void Marcar(string paso)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            // append y no buffer: si el proceso muere en la línea siguiente, esto tiene que estar
            File.AppendAllText(RutaTrace, $"{DateTime.Now:HH:mm:ss.fff}  {paso}\n");
        }
        catch
        {
            // si no se puede ni escribir la miga, no hay nada más que hacer acá
        }
    }

    public static void RegistrarCrash(Exception? ex, string dónde)
    {
        if (ex is null) return;
        Marcar($"CRASH en {dónde}: {ex.GetType().Name}");
        try
        {
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(RutaCrash, $"{DateTime.Now:u} · {dónde}\n\n{Describir(ex)}");
        }
        catch { }
    }

    /// <summary>Las inner exceptions importan: el error real suele estar dos niveles abajo.</summary>
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

    public static string Trace() => Leer(RutaTrace);
    public static string Crash() => Leer(RutaCrash);

    public static void Limpiar()
    {
        try { File.Delete(RutaTrace); } catch { }
        try { File.Delete(RutaCrash); } catch { }
    }

    static string Leer(string ruta)
    {
        try { return File.Exists(ruta) ? File.ReadAllText(ruta) : ""; }
        catch (Exception ex) { return $"(no se pudo leer {ruta}: {ex.Message})"; }
    }
}
