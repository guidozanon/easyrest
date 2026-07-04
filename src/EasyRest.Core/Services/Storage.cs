using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyRest;
using EasyRest.Models;

namespace EasyRest.Services;

public static class Storage
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Mapa colección-id → archivo actual, para manejar renombres y colisiones.</summary>
    static readonly Dictionary<string, string> FileById = new(StringComparer.OrdinalIgnoreCase);

    static string? _activePath;
    static List<WorkspaceRef> _workspaces = new();
    static Dictionary<string, string?> _activeEnvByWorkspace = new();
    static bool _settingsLoaded;

    /// <summary>Nombre del workspace "Personal" (AppData), siempre disponible.</summary>
    public const string PersonalName = "Personal (local)";

    /// <summary>Clave del workspace "Personal" en los mapas por workspace.</summary>
    public const string PersonalKey = "personal";

    /// <summary>Carpeta personal (ambientes, settings). Nunca va al repo: acá viven los tokens.</summary>
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyRest");

    /// <summary>Raíz del workspace activo: la carpeta configurada, o AppData por defecto.</summary>
    public static string WorkspaceRoot
    {
        get
        {
            EnsureSettingsLoaded();
            return string.IsNullOrWhiteSpace(_activePath) ? AppDataRoot : _activePath!;
        }
    }

    public static bool HasCustomWorkspace
    {
        get
        {
            EnsureSettingsLoaded();
            return !string.IsNullOrWhiteSpace(_activePath);
        }
    }

    /// <summary>Nombre del workspace activo.</summary>
    public static string ActiveWorkspaceName
    {
        get
        {
            EnsureSettingsLoaded();
            if (string.IsNullOrWhiteSpace(_activePath)) return PersonalName;
            var entry = _workspaces.FirstOrDefault(w =>
                string.Equals(w.Path, _activePath, StringComparison.OrdinalIgnoreCase));
            return entry?.Name ?? Path.GetFileName(_activePath!.TrimEnd(Path.DirectorySeparatorChar));
        }
    }

    static string CollectionsDir => Path.Combine(WorkspaceRoot, "collections");
    static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    /// <summary>Los ambientes son locales por workspace: nunca van al repo (ahí viven los tokens).
    /// El Personal usa AppData/environments.json (compat); los custom, AppData/workspaces/&lt;key&gt;/.</summary>
    static string EnvironmentsFile
    {
        get
        {
            EnsureSettingsLoaded();
            return string.IsNullOrWhiteSpace(_activePath)
                ? Path.Combine(AppDataRoot, "environments.json")
                : Path.Combine(AppDataRoot, "workspaces", WorkspaceKey, "environments.json");
        }
    }

    static void EnsureSettingsLoaded()
    {
        if (_settingsLoaded) return;
        _settingsLoaded = true;
        var s = LoadSettings();
        _workspaces = s.Workspaces ?? new();
        _activePath = s.ActiveWorkspacePath;
        _activeEnvByWorkspace = s.ActiveEnvByWorkspace ?? new();

        // Migración desde el esquema viejo de workspace único
        if (!string.IsNullOrWhiteSpace(s.WorkspacePath))
        {
            RegisterInternal(s.WorkspacePath!);
            _activePath ??= s.WorkspacePath;
        }

        // Migración del ambiente activo global → ambiente activo del workspace Personal
        if (!string.IsNullOrEmpty(s.ActiveEnvironmentId) && !_activeEnvByWorkspace.ContainsKey(PersonalKey))
            _activeEnvByWorkspace[PersonalKey] = s.ActiveEnvironmentId;
    }

    /// <summary>Clave estable del workspace activo (para archivos y settings por-workspace).
    /// "personal" para el local; hash de la ruta para los custom.</summary>
    public static string WorkspaceKey
    {
        get
        {
            EnsureSettingsLoaded();
            if (string.IsNullOrWhiteSpace(_activePath)) return PersonalKey;
            var norm = _activePath!.Trim()
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(norm)))[..16].ToLowerInvariant();
        }
    }

    static void RegisterInternal(string path)
    {
        if (_workspaces.Any(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase))) return;
        _workspaces.Add(new WorkspaceRef
        {
            Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)),
            Path = path
        });
    }

    /// <summary>Lista de workspaces: el "Personal" (Path vacío) primero, después los registrados.</summary>
    public static List<WorkspaceRef> ListWorkspaces()
    {
        EnsureSettingsLoaded();
        var list = new List<WorkspaceRef> { new() { Name = PersonalName, Path = "" } };
        list.AddRange(_workspaces);
        return list;
    }

    /// <summary>Registra un workspace (si no existe) y opcionalmente lo activa.</summary>
    public static void AddWorkspace(string path, string? name = null, bool activate = true)
    {
        EnsureSettingsLoaded();
        var existing = _workspaces.FirstOrDefault(w =>
            string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new WorkspaceRef
            {
                Name = string.IsNullOrWhiteSpace(name)
                    ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))
                    : name!.Trim(),
                Path = path
            };
            _workspaces.Add(existing);
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            existing.Name = name!.Trim();
        }
        if (activate) _activePath = path;
        Persist();
        FileById.Clear();
    }

    /// <summary>Quita un workspace del registro (no borra la carpeta). Si era el activo, vuelve a Personal.</summary>
    public static void RemoveWorkspace(string path)
    {
        EnsureSettingsLoaded();
        _workspaces.RemoveAll(w => string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(_activePath, path, StringComparison.OrdinalIgnoreCase)) _activePath = null;
        Persist();
        FileById.Clear();
    }

    /// <summary>Cambia el workspace activo (null/vacío = Personal). Lo registra si es nuevo.</summary>
    public static void SetWorkspacePath(string? path)
    {
        EnsureSettingsLoaded();
        if (string.IsNullOrWhiteSpace(path)) { _activePath = null; Persist(); FileById.Clear(); return; }
        AddWorkspace(path!);  // registra + activa + persiste
    }

    static void Persist()
    {
        var s = LoadSettings();
        SaveSettings(s);
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

    // ----- Ambientes (locales por workspace: siempre en AppData, nunca en el repo) -----

    public static List<EnvironmentModel> LoadEnvironments()
    {
        var file = EnvironmentsFile;
        if (!File.Exists(file)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<EnvironmentModel>>(File.ReadAllText(file)) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public static void SaveEnvironments(IEnumerable<EnvironmentModel> environments)
    {
        var file = EnvironmentsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(environments.ToList(), Options));
    }

    /// <summary>Id del ambiente activo del workspace actual (null si ninguno).</summary>
    public static string? GetActiveEnvironmentId()
    {
        EnsureSettingsLoaded();
        return _activeEnvByWorkspace.TryGetValue(WorkspaceKey, out var id) ? id : null;
    }

    /// <summary>Guarda el ambiente activo del workspace actual.</summary>
    public static void SetActiveEnvironmentId(string? id)
    {
        EnsureSettingsLoaded();
        if (string.IsNullOrEmpty(id)) _activeEnvByWorkspace.Remove(WorkspaceKey);
        else _activeEnvByWorkspace[WorkspaceKey] = id;
        Persist();
    }

    // ----- Settings -----

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
        // Storage es dueño de estos campos
        settings.Workspaces = _workspaces;
        settings.ActiveWorkspacePath = _activePath;
        settings.ActiveEnvByWorkspace = _activeEnvByWorkspace;
        settings.WorkspacePath = null;      // ya migrado al esquema nuevo
        settings.ActiveEnvironmentId = null; // ya migrado a ActiveEnvByWorkspace
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, Options));
    }

    // ----- Corridas del runner (siempre en AppData: pueden traer datos de respuestas) -----

    static string RunsDir => Path.Combine(AppDataRoot, "runs");

    public static List<RunRecord> LoadRuns()
    {
        Directory.CreateDirectory(RunsDir);
        var list = new List<RunRecord>();
        foreach (var file in Directory.GetFiles(RunsDir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(file));
                if (r != null) list.Add(r);
            }
            catch { /* corrida corrupta: se ignora */ }
        }
        return list.OrderByDescending(r => r.SavedAt).ToList();
    }

    public static void SaveRun(RunRecord record)
    {
        Directory.CreateDirectory(RunsDir);
        File.WriteAllText(Path.Combine(RunsDir, record.Id + ".json"),
            JsonSerializer.Serialize(record, Options));
    }

    public static void DeleteRun(string id)
    {
        var path = Path.Combine(RunsDir, id + ".json");
        if (File.Exists(path)) File.Delete(path);
    }

    // ----- Presets de configuración del runner (en AppData) -----

    static string RunnerPresetsFile => Path.Combine(AppDataRoot, "runner-presets.json");

    public static List<RunnerPreset> LoadRunnerPresets()
    {
        if (!File.Exists(RunnerPresetsFile)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<RunnerPreset>>(File.ReadAllText(RunnerPresetsFile)) ?? new();
        }
        catch { return new(); }
    }

    public static void SaveRunnerPresets(IEnumerable<RunnerPreset> presets)
    {
        Directory.CreateDirectory(AppDataRoot);
        File.WriteAllText(RunnerPresetsFile, JsonSerializer.Serialize(presets.ToList(), Options));
    }
}
