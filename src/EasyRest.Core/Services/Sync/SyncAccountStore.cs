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

/// <summary>Dónde queda la sesión entre arranques.
///
/// Va en texto plano en AppData, igual que environments.json, que ya guarda secretos de ambientes.
/// No es una decisión cómoda pero sí consistente: cifrarlo bien necesita el llavero de cada
/// sistema operativo, y hacerlo a medias sólo da sensación de seguridad. Si algún día se cifra,
/// se cifran los dos juntos.</summary>
public static class SyncAccountStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    static readonly object Gate = new();

    static string FilePath => Path.Combine(Storage.AppDataRoot, "sync-sessions.json");

    public static List<SyncAccount> All()
    {
        lock (Gate)
        {
            if (!File.Exists(FilePath)) return new List<SyncAccount>();
            try
            {
                return JsonSerializer.Deserialize<List<SyncAccount>>(File.ReadAllText(FilePath), Json)
                       ?? new List<SyncAccount>();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // si el archivo se rompió, se pierde la sesión y hay que loguearse de nuevo:
                // molesto pero recuperable, y mejor que no arrancar
                return new List<SyncAccount>();
            }
        }
    }

    public static SyncAccount? Find(string serverUrl) =>
        All().FirstOrDefault(a => Same(a.ServerUrl, serverUrl));

    public static void Save(SyncAccount account)
    {
        lock (Gate)
        {
            var list = AllUnlocked();
            list.RemoveAll(a => Same(a.ServerUrl, account.ServerUrl));
            list.Add(account);
            WriteUnlocked(list);
        }
    }

    public static void Remove(string serverUrl)
    {
        lock (Gate)
        {
            var list = AllUnlocked();
            if (list.RemoveAll(a => Same(a.ServerUrl, serverUrl)) > 0) WriteUnlocked(list);
        }
    }

    static List<SyncAccount> AllUnlocked()
    {
        if (!File.Exists(FilePath)) return new List<SyncAccount>();
        try
        {
            return JsonSerializer.Deserialize<List<SyncAccount>>(File.ReadAllText(FilePath), Json)
                   ?? new List<SyncAccount>();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new List<SyncAccount>();
        }
    }

    static void WriteUnlocked(List<SyncAccount> list)
    {
        Directory.CreateDirectory(Storage.AppDataRoot);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(list, Json));
    }

    /// <summary>Las URLs se comparan sin la barra final y sin distinguir mayúsculas: escribir
    /// "https://sync.acme.com/" y "https://sync.acme.com" es la misma cuenta.</summary>
    internal static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
