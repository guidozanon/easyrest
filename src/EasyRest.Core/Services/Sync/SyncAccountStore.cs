using System.IO;
using System.Text.Json;

namespace EasyRest.Services.Sync;

/// <summary>La sesión con un servidor de sync: quién sos y con qué tokens.
///
/// Se guarda por servidor y no por workspace, a propósito: si alguien tiene dos workspaces locales
/// contra el mismo server, no tiene por qué loguearse dos veces.</summary>
public class SyncAccount
{
    public string ServerUrl { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";

    /// <summary>Cuándo vence el access token. Se refresca un rato antes, no cuando ya falló.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public Guid UserId { get; set; }
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Provider { get; set; } = "";
    public bool IsServerAdmin { get; set; }

    public void Apply(SyncSession session)
    {
        AccessToken = session.AccessToken;
        RefreshToken = session.RefreshToken;
        ExpiresAtUtc = DateTime.UtcNow.AddSeconds(session.ExpiresIn);
        UserId = session.User.Id;
        Email = session.User.Email;
        DisplayName = session.User.DisplayName;
        Provider = session.User.Provider;
        IsServerAdmin = session.User.IsServerAdmin;
    }
}

/// <summary>Dónde quedan las sesiones entre arranques. Recibe la ruta como SyncState y SyncBinding:
/// la app usa <see cref="Default"/> y los tests un archivo temporal.
///
/// Va en texto plano, igual que environments.json, que ya guarda secretos de ambientes. No es una
/// decisión cómoda pero sí consistente: cifrarlo de verdad necesita el llavero de cada sistema
/// operativo, y hacerlo a medias sólo da sensación de seguridad. Si algún día se cifra, se cifran
/// los dos juntos.</summary>
public class SyncAccountStore(string filePath)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    readonly object _gate = new();

    /// <summary>La de la app: AppData/sync-sessions.json.</summary>
    public static SyncAccountStore Default { get; } =
        new(Path.Combine(Storage.AppDataRoot, "sync-sessions.json"));

    public string FilePath { get; } = filePath;

    public List<SyncAccount> All()
    {
        lock (_gate) return Read();
    }

    public SyncAccount? Find(string serverUrl) =>
        All().FirstOrDefault(a => Same(a.ServerUrl, serverUrl));

    public void Save(SyncAccount account)
    {
        lock (_gate)
        {
            var list = Read();
            list.RemoveAll(a => Same(a.ServerUrl, account.ServerUrl));
            list.Add(account);
            Write(list);
        }
    }

    public void Remove(string serverUrl)
    {
        lock (_gate)
        {
            var list = Read();
            if (list.RemoveAll(a => Same(a.ServerUrl, serverUrl)) > 0) Write(list);
        }
    }

    List<SyncAccount> Read()
    {
        if (!File.Exists(FilePath)) return new List<SyncAccount>();
        try
        {
            return JsonSerializer.Deserialize<List<SyncAccount>>(File.ReadAllText(FilePath), Json)
                   ?? new List<SyncAccount>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // si el archivo se rompió se pierde la sesión y hay que loguearse de nuevo: molesto
            // pero recuperable, y mejor que no arrancar
            return new List<SyncAccount>();
        }
    }

    void Write(List<SyncAccount> list)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Json));
    }

    /// <summary>Las URLs se comparan sin la barra final y sin distinguir mayúsculas: escribir
    /// "https://sync.acme.com/" y "https://sync.acme.com" es la misma cuenta.</summary>
    internal static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
