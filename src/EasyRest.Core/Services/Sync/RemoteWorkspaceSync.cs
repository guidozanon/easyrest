using System.IO;
using System.Net;

namespace EasyRest.Services.Sync;

/// <summary>Sincroniza la carpeta del workspace contra el servidor de sync de la organización.
/// Es la implementación que corre en móvil, donde no existe el CLI de git.
///
/// El modelo es offline-first: el disco manda. Se baja el delta desde el último cursor, se sube
/// lo que cambió localmente y, si las dos puntas tocaron el mismo archivo, gana lo local y la
/// versión del server queda guardada al lado con extensión .remoto-&lt;rev&gt;.json. Nunca se
/// pierde una edición sin dejar rastro.</summary>
public class RemoteWorkspaceSync : IWorkspaceSync
{
    /// <summary>Sólo se sincronizan estas subcarpetas. Es deliberado: la raíz del workspace
    /// personal es AppData, donde viven settings.json y environments.json con los tokens
    /// locales, y esos no se suben nunca.</summary>
    public static readonly string[] SyncedFolders = { "collections", EnvironmentDocument.FolderName };

    readonly string _root;
    readonly SyncApiClient _api;
    readonly Guid _workspaceId;
    readonly string _statePath;

    public RemoteWorkspaceSync(string workspaceRoot, SyncApiClient api, Guid workspaceId, string statePath)
    {
        _root = workspaceRoot;
        _api = api;
        _workspaceId = workspaceId;
        _statePath = statePath;
    }

    public string DisplayName => "Servidor de sync";

    public bool IsConfigured => !string.IsNullOrEmpty(_api.AccessToken) && _workspaceId != Guid.Empty;

    public Task<WorkspaceSyncStatus?> StatusAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Task.FromResult<WorkspaceSyncStatus?>(null);

        // sin red: se cuenta lo pendiente comparando con el estado de la última sincronización
        var state = SyncState.Load(_statePath);
        var pending = 0;

        foreach (var (path, content) in EnumerateLocalFiles())
        {
            var hash = SyncState.HashOf(content);
            if (!state.Docs.TryGetValue(path, out var known) || known.Hash != hash) pending++;
        }
        pending += state.Docs.Keys.Count(path => !File.Exists(FullPath(path)));

        return Task.FromResult<WorkspaceSyncStatus?>(new WorkspaceSyncStatus("☁ sync", pending));
    }

    public async Task<WorkspaceSyncOutcome> SyncAsync(ConflictResolution? resolution = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new WorkspaceSyncOutcome(false, "Todavía no iniciaste sesión en el servidor de sync.");

        var state = SyncState.Load(_statePath);
        if (state.WorkspaceId != _workspaceId || state.ServerUrl != _api.BaseUrl)
        {
            // cambió el server o el workspace: el estado anterior no aplica
            state = new SyncState { ServerUrl = _api.BaseUrl, WorkspaceId = _workspaceId };
        }

        var conflicts = new List<string>();
        int pulled, pushed;

        try
        {
            pulled = await PullAsync(state, resolution, conflicts, ct);
            pushed = await PushAsync(state, conflicts, ct);
        }
        catch (SyncApiException ex)
        {
            state.Save(_statePath);
            return new WorkspaceSyncOutcome(false, ex.Status == HttpStatusCode.Unauthorized
                ? "La sesión con el servidor de sync venció: volvé a iniciar sesión."
                : $"No se pudo sincronizar: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            state.Save(_statePath);
            return new WorkspaceSyncOutcome(false, $"No se pudo llegar al servidor de sync: {ex.Message}");
        }

        state.Save(_statePath);

        var message = (pulled, pushed) switch
        {
            (0, 0) => "Todo al día.",
            (_, 0) => $"Se bajaron {pulled} cambios.",
            (0, _) => $"Se subieron {pushed} cambios.",
            _ => $"Se bajaron {pulled} y se subieron {pushed} cambios."
        };
        if (conflicts.Count > 0)
            message += $"\n\nHubo {conflicts.Count} conflicto(s); se conservó tu versión y la del server " +
                       $"quedó al lado como .remoto-*.json:\n- {string.Join("\n- ", conflicts.Take(10))}";

        return new WorkspaceSyncOutcome(true, message, conflicts.Count > 0, pulled > 0);
    }

    // ----- Bajada -----

    async Task<int> PullAsync(SyncState state, ConflictResolution? resolution, List<string> conflicts,
        CancellationToken ct)
    {
        var applied = 0;
        bool hasMore;

        do
        {
            var changes = await _api.GetChangesAsync(_workspaceId, state.Cursor, 200, ct);
            hasMore = changes.HasMore;

            foreach (var doc in changes.Documents)
            {
                if (!IsSyncedPath(doc.Path)) continue;

                // El delta incluye lo que subió este mismo dispositivo (el cursor va detrás de
                // las propias escrituras). Si la revisión es la que ya tenemos anotada, no hay
                // nada que aplicar: sin esto, un archivo borrado acá volvería a aparecer solo,
                // porque "no está en disco" se confundiría con "es nuevo en el server".
                if (state.Docs.TryGetValue(doc.Path, out var current) && current.Rev == doc.Rev) continue;

                if (doc.Deleted) applied += ApplyRemoteDelete(state, doc, conflicts);
                else applied += await ApplyRemoteWriteAsync(state, doc, resolution, conflicts, ct);
            }

            state.Cursor = changes.Cursor;
        } while (hasMore);

        return applied;
    }

    int ApplyRemoteDelete(SyncState state, SyncDocument doc, List<string> conflicts)
    {
        var full = FullPath(doc.Path);
        state.Docs.TryGetValue(doc.Path, out var known);

        if (!File.Exists(full))
        {
            state.Docs.Remove(doc.Path);
            return 0;
        }

        var localHash = SyncState.HashOf(File.ReadAllText(full));
        if (known != null && known.Hash == localHash)
        {
            File.Delete(full);
            state.Docs.Remove(doc.Path);
            return 1;
        }

        // lo borraron en el server pero acá está editado: se conserva y se vuelve a subir
        conflicts.Add($"{doc.Path} (borrado en el server, se conserva tu versión)");
        state.Docs.Remove(doc.Path);
        return 0;
    }

    async Task<int> ApplyRemoteWriteAsync(SyncState state, SyncDocument doc, ConflictResolution? resolution,
        List<string> conflicts, CancellationToken ct)
    {
        var remote = await MaterializeAsync(doc, ct);
        var full = FullPath(doc.Path);
        state.Docs.TryGetValue(doc.Path, out var known);

        var localExists = File.Exists(full);
        var localHash = localExists ? SyncState.HashOf(File.ReadAllText(full)) : null;

        // sin cambios locales (o sin archivo): la versión del server entra derecho
        if (!localExists || (known != null && known.Hash == localHash))
        {
            Write(full, remote);
            state.Docs[doc.Path] = new SyncDocState
            {
                Id = doc.Id,
                Rev = doc.Rev,
                Hash = SyncState.HashOf(remote)
            };
            return localHash == SyncState.HashOf(remote) ? 0 : 1;
        }

        if (resolution == ConflictResolution.KeepRemote)
        {
            Write(full, remote);
            state.Docs[doc.Path] = new SyncDocState
            {
                Id = doc.Id, Rev = doc.Rev, Hash = SyncState.HashOf(remote)
            };
            return 1;
        }

        // KeepLocal o sin resolución: gana lo local, pero la versión del server se guarda al
        // lado para no perderla. La revisión nueva queda anotada para que el push de abajo
        // pueda pisar el server sin volver a chocar.
        if (resolution != ConflictResolution.KeepLocal)
        {
            var copy = $"{full}.remoto-{doc.Rev}.json";
            Write(copy, remote);
            conflicts.Add($"{doc.Path} → {Path.GetFileName(copy)}");
        }
        else
        {
            conflicts.Add($"{doc.Path} (se conservó tu versión)");
        }

        state.Docs[doc.Path] = new SyncDocState
        {
            Id = doc.Id,
            Rev = doc.Rev,
            Hash = known?.Hash ?? ""   // distinto del local: el push lo va a subir
        };
        return 0;
    }

    /// <summary>Arma el contenido que va a disco: para los ambientes hay que volver a meter los
    /// valores secretos, que viajan por un endpoint aparte y sólo si hay permiso.</summary>
    async Task<string> MaterializeAsync(SyncDocument doc, CancellationToken ct)
    {
        var content = doc.Content ?? "";
        if (doc.Kind != "environment" || !EnvironmentDocument.HasSecrets(content)) return content;

        try
        {
            var secrets = await _api.GetSecretsAsync(_workspaceId, doc.Id, ct);
            return EnvironmentDocument.Merge(content, secrets.Secrets);
        }
        catch (SyncApiException ex) when (ex.Status == HttpStatusCode.Forbidden)
        {
            // sin permiso sobre los secretos: quedan las claves con valor vacío, igual que en
            // "compartir sólo las claves"
            return content;
        }
    }

    // ----- Subida -----

    async Task<int> PushAsync(SyncState state, List<string> conflicts, CancellationToken ct)
    {
        var pushed = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (path, raw) in EnumerateLocalFiles())
        {
            seen.Add(path);
            var hash = SyncState.HashOf(raw);
            state.Docs.TryGetValue(path, out var known);
            if (known != null && known.Hash == hash) continue;

            var kind = KindOf(path);
            var (content, secrets) = kind == "environment"
                ? EnvironmentDocument.Split(raw)
                : (raw, new Dictionary<string, string>());

            try
            {
                var doc = await _api.PutDocumentAsync(_workspaceId, path, kind, content,
                    known?.Rev, secrets.Count > 0 ? secrets : null, ct);
                state.Docs[path] = new SyncDocState { Id = doc.Id, Rev = doc.Rev, Hash = hash };
                pushed++;
            }
            catch (SyncConflictException ex)
            {
                await SaveRemoteCopyAsync(path, ex.Current, conflicts, ct);
                if (ex.Current != null)
                    state.Docs[path] = new SyncDocState
                    {
                        Id = ex.Current.Id, Rev = ex.Current.Rev, Hash = known?.Hash ?? ""
                    };
            }
        }

        // lo que está en el estado y ya no está en disco: se borró localmente
        foreach (var path in state.Docs.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            var known = state.Docs[path];
            if (known.Id == Guid.Empty || string.IsNullOrEmpty(known.Rev))
            {
                state.Docs.Remove(path);
                continue;
            }

            try
            {
                await _api.DeleteDocumentAsync(_workspaceId, known.Id, known.Rev, ct);
                state.Docs.Remove(path);
                pushed++;
            }
            catch (SyncConflictException)
            {
                // alguien lo modificó mientras acá se borraba: gana el server y vuelve a bajar
                conflicts.Add($"{path} (lo modificaron en el server mientras lo borrabas)");
                state.Docs.Remove(path);
            }
            catch (SyncApiException ex) when (ex.Status == HttpStatusCode.NotFound)
            {
                state.Docs.Remove(path);
            }
        }

        return pushed;
    }

    async Task SaveRemoteCopyAsync(string path, SyncDocument? current, List<string> conflicts,
        CancellationToken ct)
    {
        if (current == null)
        {
            conflicts.Add($"{path} (cambió en el server)");
            return;
        }

        var copy = $"{FullPath(path)}.remoto-{current.Rev}.json";
        Write(copy, await MaterializeAsync(current, ct));
        conflicts.Add($"{path} → {Path.GetFileName(copy)}");
    }

    // ----- Archivos -----

    /// <summary>Los json de las carpetas sincronizadas, con su ruta relativa normalizada. Las
    /// copias .remoto-*.json quedan afuera: son para que las mire una persona, no para subirlas.</summary>
    IEnumerable<(string Path, string Content)> EnumerateLocalFiles()
    {
        foreach (var folder in SyncedFolders)
        {
            var dir = Path.Combine(_root, folder);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
            {
                if (file.Contains(".remoto-", StringComparison.Ordinal)) continue;

                var relative = Path.GetRelativePath(_root, file).Replace('\\', '/');
                string content;
                try
                {
                    content = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;   // lo está escribiendo la app justo ahora: va en la próxima
                }

                yield return (relative, content);
            }
        }
    }

    string FullPath(string relative) =>
        Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

    static void Write(string fullPath, string content)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
    }

    static bool IsSyncedPath(string path) =>
        SyncedFolders.Any(f => path.StartsWith(f + "/", StringComparison.Ordinal));

    /// <summary>El tipo se deduce del nombre, igual que lo hace el storage en disco.</summary>
    internal static string KindOf(string path)
    {
        if (path.StartsWith(EnvironmentDocument.FolderName + "/", StringComparison.Ordinal))
            return "environment";

        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.Equals("collection.json", StringComparison.OrdinalIgnoreCase)) return "collection";
        if (name.Equals("folder.json", StringComparison.OrdinalIgnoreCase)) return "folder";
        if (name.EndsWith(".req.json", StringComparison.OrdinalIgnoreCase)) return "request";
        return "file";
    }
}
