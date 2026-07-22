using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using EasyRest.Models;

namespace EasyRest.Services;

/// <summary>Formato de colección en carpeta: cada colección es un directorio, cada request un
/// archivo *.req.json con solo los datos de esa request, y cada carpeta un subdirectorio con su
/// folder.json. Así los diffs de git son por request y los conflictos de sync se reducen al mínimo.
///
///   collections/Mi API/collection.json          ← metadata (nombre, auth, headers, orden)
///   collections/Mi API/Login.req.json           ← una request
///   collections/Mi API/Usuarios/folder.json     ← metadata de la carpeta
///   collections/Mi API/Usuarios/Alta.req.json
/// </summary>
public static class CollectionFileStore
{
    public const string CollectionMetaFileName = "collection.json";
    public const string FolderMetaFileName = "folder.json";
    public const string RequestExtension = ".req.json";

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Metadata de la colección: todo menos las requests y carpetas (que van en archivos
    /// propios). El orden se guarda por id para respetar el reordenado por drag &amp; drop.</summary>
    class CollectionMeta
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<KeyValueItem> Headers { get; set; } = new();
        public AuthConfig Auth { get; set; } = new();
        public List<string> FolderOrder { get; set; } = new();
        public List<string> RequestOrder { get; set; } = new();
    }

    class FolderMeta
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public AuthConfig Auth { get; set; } = new() { Type = AuthType.Inherit };
        public List<string> FolderOrder { get; set; } = new();
        public List<string> RequestOrder { get; set; } = new();
    }

    public static bool IsCollectionDir(string dir) =>
        File.Exists(Path.Combine(dir, CollectionMetaFileName));

    // ----- Guardado -----

    /// <summary>Escribe la colección completa en su carpeta. Solo toca los archivos cuyo contenido
    /// cambió (para no ensuciar el repo git) y borra los que ya no corresponden a ninguna
    /// request/carpeta (renombres y eliminaciones).</summary>
    public static void Save(RequestCollection collection, string dir)
    {
        Directory.CreateDirectory(dir);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var meta = new CollectionMeta
        {
            Id = collection.Id,
            Name = collection.Name,
            Headers = collection.Headers.ToList(),
            Auth = collection.Auth,
            FolderOrder = collection.Folders.Select(f => f.Id).ToList(),
            RequestOrder = collection.Requests.Select(r => r.Id).ToList()
        };
        WriteIfChanged(Path.Combine(dir, CollectionMetaFileName),
            JsonSerializer.Serialize(meta, Options), written);

        WriteChildren(dir, collection.Folders, collection.Requests, written);
        Cleanup(dir, written);
    }

    static void WriteChildren(string dir, IEnumerable<Folder> folders,
        IEnumerable<RequestItem> requests, HashSet<string> written)
    {
        var usedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var req in requests)
        {
            var name = UniqueName(SanitizeFileName(req.Name), req.Id, usedFiles);
            WriteIfChanged(Path.Combine(dir, name + RequestExtension),
                JsonSerializer.Serialize(req, Options), written);
        }

        var usedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in folders)
        {
            var name = UniqueName(SanitizeFileName(folder.Name), folder.Id, usedDirs);
            var sub = Path.Combine(dir, name);
            Directory.CreateDirectory(sub);

            var meta = new FolderMeta
            {
                Id = folder.Id,
                Name = folder.Name,
                Auth = folder.Auth,
                FolderOrder = folder.Folders.Select(f => f.Id).ToList(),
                RequestOrder = folder.Requests.Select(r => r.Id).ToList()
            };
            WriteIfChanged(Path.Combine(sub, FolderMetaFileName),
                JsonSerializer.Serialize(meta, Options), written);

            WriteChildren(sub, folder.Folders, folder.Requests, written);
        }
    }

    static void WriteIfChanged(string path, string content, HashSet<string> written)
    {
        written.Add(path);
        if (File.Exists(path) && File.ReadAllText(path) == content) return;
        File.WriteAllText(path, content);
    }

    /// <summary>Borra los archivos administrados (collection.json / folder.json / *.req.json) que
    /// no se escribieron en este guardado, y las subcarpetas que quedaron vacías. Cualquier otro
    /// archivo (README, .gitignore, etc.) se deja intacto.</summary>
    static void Cleanup(string dir, HashSet<string> written)
    {
        foreach (var sub in Directory.GetDirectories(dir))
            Cleanup(sub, written);

        foreach (var file in Directory.GetFiles(dir))
        {
            if (written.Contains(file)) continue;
            var name = Path.GetFileName(file);
            var managed = name.EndsWith(RequestExtension, StringComparison.OrdinalIgnoreCase) ||
                          name.Equals(CollectionMetaFileName, StringComparison.OrdinalIgnoreCase) ||
                          name.Equals(FolderMetaFileName, StringComparison.OrdinalIgnoreCase);
            if (managed) File.Delete(file);
        }

        foreach (var sub in Directory.GetDirectories(dir))
            if (!Directory.EnumerateFileSystemEntries(sub).Any())
                Directory.Delete(sub);
    }

    // ----- Carga -----

    /// <summary>Lee una colección desde su carpeta. Devuelve null si el directorio no tiene un
    /// collection.json válido.</summary>
    public static RequestCollection? Load(string dir)
    {
        var meta = Read<CollectionMeta>(Path.Combine(dir, CollectionMetaFileName));
        if (meta == null || string.IsNullOrEmpty(meta.Id)) return null;

        var collection = new RequestCollection
        {
            Id = meta.Id,
            Name = string.IsNullOrWhiteSpace(meta.Name) ? Path.GetFileName(dir) : meta.Name,
            Auth = meta.Auth ?? new(),
            Headers = new ObservableCollection<KeyValueItem>(meta.Headers ?? new())
        };
        LoadChildren(dir, collection.Folders, collection.Requests, meta.FolderOrder, meta.RequestOrder);
        return collection;
    }

    static void LoadChildren(string dir, ObservableCollection<Folder> folders,
        ObservableCollection<RequestItem> requests, List<string> folderOrder, List<string> requestOrder)
    {
        var reqs = new List<RequestItem>();
        foreach (var file in Directory.GetFiles(dir))
        {
            if (!file.EndsWith(RequestExtension, StringComparison.OrdinalIgnoreCase)) continue;
            var req = Read<RequestItem>(file);
            if (req != null) reqs.Add(req);
        }
        foreach (var req in SortByOrder(reqs, requestOrder, r => r.Id, r => r.Name))
            requests.Add(req);

        var subs = new List<Folder>();
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            var metaPath = Path.Combine(subDir, FolderMetaFileName);
            if (!File.Exists(metaPath)) continue;
            var meta = Read<FolderMeta>(metaPath);
            if (meta == null) continue;

            var folder = new Folder
            {
                Id = string.IsNullOrEmpty(meta.Id) ? Guid.NewGuid().ToString("N") : meta.Id,
                Name = string.IsNullOrWhiteSpace(meta.Name) ? Path.GetFileName(subDir) : meta.Name,
                Auth = meta.Auth ?? new() { Type = AuthType.Inherit }
            };
            LoadChildren(subDir, folder.Folders, folder.Requests, meta.FolderOrder, meta.RequestOrder);
            subs.Add(folder);
        }
        foreach (var folder in SortByOrder(subs, folderOrder, f => f.Id, f => f.Name))
            folders.Add(folder);
    }

    /// <summary>Ordena según la lista de ids del metadata; lo que no figura (agregado por otro
    /// integrante del equipo, por ejemplo) va al final, alfabéticamente.</summary>
    static IEnumerable<T> SortByOrder<T>(List<T> items, List<string>? order,
        Func<T, string> id, Func<T, string> name)
    {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < (order?.Count ?? 0); i++) index.TryAdd(order![i], i);
        return items
            .OrderBy(x => index.TryGetValue(id(x), out var i) ? i : int.MaxValue)
            .ThenBy(name, StringComparer.CurrentCultureIgnoreCase);
    }

    static T? Read<T>(string path) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return null; // archivo corrupto o a medio mergear: se ignora para no bloquear la carga
        }
    }

    static string UniqueName(string name, string id, HashSet<string> used)
    {
        if (!used.Add(name))
        {
            var shortId = id.Length >= 8 ? id[..8] : id;
            name = $"{name} ({shortId})";
            used.Add(name);
        }
        return name;
    }

    static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        return name.Length == 0 ? "sin nombre" : name;
    }
}
