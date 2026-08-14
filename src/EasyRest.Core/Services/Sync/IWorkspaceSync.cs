namespace EasyRest.Services.Sync;

/// <summary>Estado del workspace para la barra de estado: qué backend sincroniza y cuánto hay
/// pendiente.</summary>
public record WorkspaceSyncStatus(string Label, int PendingChanges);

/// <summary>Resultado de sincronizar. HasConflicts avisa que hubo ediciones cruzadas y que la UI
/// tiene que preguntar cómo resolverlas; PulledRemote, que conviene recargar desde disco.</summary>
public record WorkspaceSyncOutcome(
    bool Ok,
    string Message,
    bool HasConflicts = false,
    bool PulledRemote = false);

/// <summary>Cómo sincroniza un workspace. Hay dos implementaciones y conviven: el repo git de
/// siempre (GitWorkspaceSync) y el servicio propio (RemoteWorkspaceSync), que es la única que
/// compila en móvil, donde no existe el CLI de git.</summary>
public interface IWorkspaceSync
{
    /// <summary>Nombre para mostrar en la UI ("Git", "Servidor de sync").</summary>
    string DisplayName { get; }

    /// <summary>Está listo para sincronizar (hay repo, o hay server y sesión).</summary>
    bool IsConfigured { get; }

    Task<WorkspaceSyncStatus?> StatusAsync(CancellationToken ct = default);

    /// <summary>Sincroniza. Si una sincronización anterior encontró conflictos, la UI vuelve a
    /// llamar con la resolución elegida.</summary>
    Task<WorkspaceSyncOutcome> SyncAsync(ConflictResolution? resolution = null,
        CancellationToken ct = default);
}
