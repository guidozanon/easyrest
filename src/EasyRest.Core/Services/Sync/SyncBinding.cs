using System.IO;
using System.Text.Json;

namespace EasyRest.Services.Sync;

/// <summary>A qué workspace remoto está atado un workspace local. Es lo que hace que "sincronizar"
/// signifique algo: sin esto hay sesión pero no se sabe contra qué carpeta del server sincronizar.
///
/// Se guarda aparte de la sesión porque son cosas distintas: la sesión es por servidor y la
/// atadura es por carpeta local. Dos workspaces locales pueden apuntar a dos remotos del mismo
/// server con un solo login.</summary>
public class SyncBinding
{
    public string ServerUrl { get; set; } = "";
    public Guid WorkspaceId { get; set; }

    /// <summary>Sólo para mostrar. El nombre real vive en el server y puede cambiar.</summary>
    public string WorkspaceName { get; set; } = "";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool IsSet => !string.IsNullOrWhiteSpace(ServerUrl) && WorkspaceId != Guid.Empty;

    public static SyncBinding Load(string path)
    {
        if (!File.Exists(path)) return new SyncBinding();
        try
        {
            return JsonSerializer.Deserialize<SyncBinding>(File.ReadAllText(path), Json) ?? new SyncBinding();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new SyncBinding();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public static void Clear(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
