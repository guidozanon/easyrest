using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyRest.Services.Sync;

/// <summary>Lo que el cliente recuerda de la última sincronización: hasta dónde leyó (Cursor) y,
/// por cada archivo, con qué revisión del server quedó y con qué contenido. El hash es lo que
/// permite distinguir "no lo tocué" de "lo edité", que es toda la lógica de conflictos.</summary>
public class SyncState
{
    public string ServerUrl { get; set; } = "";
    public Guid WorkspaceId { get; set; }
    public long Cursor { get; set; }
    public Dictionary<string, SyncDocState> Docs { get; set; } = new(StringComparer.Ordinal);

    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static SyncState Load(string path)
    {
        if (!File.Exists(path)) return new SyncState();
        try
        {
            return JsonSerializer.Deserialize<SyncState>(File.ReadAllText(path), Options) ?? new SyncState();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // el estado es una caché: si se corrompió, se reconstruye sincronizando de nuevo
            return new SyncState();
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    /// <summary>Hash del contenido de un archivo, para detectar ediciones locales.</summary>
    public static string HashOf(string content) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

public class SyncDocState
{
    public Guid Id { get; set; }
    public string Rev { get; set; } = "";
    public string Hash { get; set; } = "";
}
