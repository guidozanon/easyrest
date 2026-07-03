using System.IO;
using System.Text.Json;
using EasyRest.Models;

namespace EasyRest.Services;

public static class Storage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Mapa colección-id → archivo actual, para manejar renombres y colisiones.</summary>
    static readonly Dictionary<string, string> FileById = new(StringComparer.OrdinalIgnoreCase);

    static string? _workspacePath;
    static bool _settingsLoaded;

    /// <summary>Carpeta personal (ambientes, settings). Nunca va al repo: acá viven los tokens.</summary>
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyRest");

    /// <summary>Raíz del workspace: la carpeta configurada, o AppData por defecto.</summary>
    public static string WorkspaceRoot
    {
        get
        {
            EnsureSettingsLoaded();
            return string.IsNullOrWhiteSpace(_workspacePath) ? AppDataRoot : _workspacePath!;
        }
    }

    public static bool HasCustomWorkspace
    {
        get
        {
            EnsureSettingsLoaded();
            return !string.IsNullOrWhiteSpace(_workspacePath);
        }
    }

    static string CollectionsDir => Path.Combine(WorkspaceRoot, "collections");
    static string EnvironmentsFile => Path.Combine(AppDataRoot, "environments.json");
    static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    static void EnsureSettingsLoaded()
    {
        if (_settingsLoaded) return;
        _settingsLoaded = true;
        _workspacePath = LoadSettings().WorkspacePath;
    }

    /// <summary>Cambia la carpeta del workspace (null = volver a AppData) y lo persiste.</summary>
    public static void SetWorkspacePath(string? path)
    {
        EnsureSettingsLoaded();
        _workspacePath = string.IsNullOrWhiteSpace(path) ? null : path;
        SaveSettings(LoadSettings());
        FileById.Clear();
    }

    static void EnsureDirs()
    {
        Directory.CreateDirectory(AppDataRoot);
        Directory.CreateDirectory(CollectionsDir);
    }

    // ----- Colecciones (viven en el workspace, con nombre de archivo legible) -----

    public static List<RequestCollection> LoadCollections()
    {
        EnsureDirs();
        FileById.Clear();
        var list = new List<RequestCollection>();
        foreach (var file in Directory.GetFiles(CollectionsDir, "*.json"))
        {
            try
            {
                var col = JsonSerializer.Deserialize<RequestCollection>(File.ReadAllText(file));
                if (col == null) continue;
                list.Add(col);
                FileById[col.Id] = file;
            }
            catch
            {
                // archivo corrupto: se ignora para no bloquear el inicio
            }
        }
        return list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static void SaveCollection(RequestCollection collection)
    {
        EnsureDirs();
        var path = ResolvePathFor(collection);

        // renombre: borrar el archivo anterior si cambió
        if (FileById.TryGetValue(collection.Id, out var current) &&
            !string.Equals(current, path, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(current))
            File.Delete(current);

        File.WriteAllText(path, JsonSerializer.Serialize(collection, Options));
        FileById[collection.Id] = path;
    }

    public static void DeleteCollection(RequestCollection collection)
    {
        if (FileById.TryGetValue(collection.Id, out var path) && File.Exists(path))
            File.Delete(path);
        FileById.Remove(collection.Id);
    }

    static string ResolvePathFor(RequestCollection collection)
    {
        var name = SanitizeFileName(collection.Name);
        var path = Path.Combine(CollectionsDir, name + ".json");

        // colisión: otro id ya usa (o el disco ya tiene) ese archivo → sufijo con id corto
        var takenByOther = FileById.Any(kv =>
            kv.Key != collection.Id && string.Equals(kv.Value, path, StringComparison.OrdinalIgnoreCase));
        var existsUnknown = !takenByOther && File.Exists(path) &&
            (!FileById.TryGetValue(collection.Id, out var own) ||
             !string.Equals(own, path, StringComparison.OrdinalIgnoreCase));
        if (takenByOther || existsUnknown)
        {
            var shortId = collection.Id.Length >= 8 ? collection.Id[..8] : collection.Id;
            path = Path.Combine(CollectionsDir, $"{name} ({shortId}).json");
        }
        return path;
    }

    static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "coleccion" : name;
    }

    // ----- Ambientes y settings (siempre en AppData: son personales) -----

    public static List<EnvironmentModel> LoadEnvironments()
    {
        EnsureDirs();
        if (!File.Exists(EnvironmentsFile)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<EnvironmentModel>>(File.ReadAllText(EnvironmentsFile)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void SaveEnvironments(IEnumerable<EnvironmentModel> environments)
    {
        EnsureDirs();
        File.WriteAllText(EnvironmentsFile, JsonSerializer.Serialize(environments.ToList(), Options));
    }

    public static AppSettings LoadSettings()
    {
        Directory.CreateDirectory(AppDataRoot);
        if (!File.Exists(SettingsFile)) return new();
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(AppDataRoot);
        EnsureSettingsLoaded();
        settings.WorkspacePath = _workspacePath; // Storage es dueño de este campo
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, Options));
    }
}
