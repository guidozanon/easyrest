namespace EasyRest.Services.Sync;

/// <summary>El sync de siempre: el repo git de la carpeta del workspace. Envuelve a GitService
/// sin cambiarle el comportamiento, para que la UI pueda hablarle igual que al servicio.</summary>
public class GitWorkspaceSync(string workspaceRoot) : IWorkspaceSync
{
    public string DisplayName => "Git";

    public bool IsConfigured => GitService.IsAvailable() && GitService.IsRepo(workspaceRoot);

    public Task<WorkspaceSyncStatus?> StatusAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return Task.FromResult<WorkspaceSyncStatus?>(null);

        var status = GitService.Status(workspaceRoot);
        return Task.FromResult(status == null
            ? null
            : new WorkspaceSyncStatus($"⎇ {status.Branch}", status.Pending));
    }

    public Task<WorkspaceSyncOutcome> SyncAsync(ConflictResolution? resolution = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
            return Task.FromResult(new WorkspaceSyncOutcome(false,
                "Este workspace no tiene un repositorio git."));

        var result = resolution is { } r
            ? GitService.Sync(workspaceRoot, r)
            : GitService.Sync(workspaceRoot);

        return Task.FromResult(new WorkspaceSyncOutcome(
            result.Ok, result.Message, result.HasConflicts, result.PulledRemote));
    }
}
