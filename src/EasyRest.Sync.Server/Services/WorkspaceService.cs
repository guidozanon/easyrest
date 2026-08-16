using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Services;

/// <summary>El acceso de un caller a un workspace, ya resuelto: sirve igual para una persona
/// (por su membresía) que para un token de servicio (que trae su rol adentro).</summary>
public record WorkspaceAccess(
    Workspace Workspace,
    WorkspaceRole Role,
    bool CanReadSecretsDefault,
    Guid? MembershipId,
    Guid? UserId)
{
    public bool AtLeast(WorkspaceRole role) => Role >= role;
}

public class WorkspaceService(SyncDbContext db, SecretBox secretBox)
{
    /// <summary>Resuelve el acceso, o null si el caller no tiene nada que hacer en ese
    /// workspace. Devolver null y contestar 404 es a propósito: un 403 confirmaría que el
    /// workspace existe.</summary>
    public async Task<WorkspaceAccess?> ResolveAsync(Caller caller, Guid workspaceId, CancellationToken ct)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (workspace == null) return null;

        if (caller.Service is { } service)
            return service.WorkspaceId != workspaceId
                ? null
                : new WorkspaceAccess(workspace, service.Role, service.CanReadSecrets, null, null);

        if (caller.User is not { } user) return null;

        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == user.Id, ct);
        return membership == null
            ? null
            : new WorkspaceAccess(workspace, membership.Role, membership.CanReadSecrets, membership.Id, user.Id);
    }

    /// <summary>Permiso efectivo sobre los secretos de un ambiente: el override por documento
    /// gana sobre el default de la membresía. Los tokens de servicio no tienen overrides.</summary>
    public async Task<bool> CanAccessSecretsAsync(WorkspaceAccess access, Guid documentId, CancellationToken ct)
    {
        if (access.MembershipId is not { } membershipId) return access.CanReadSecretsDefault;

        var over = await db.SecretOverrides
            .FirstOrDefaultAsync(o => o.MembershipId == membershipId && o.DocumentId == documentId, ct);
        return over?.CanRead ?? access.CanReadSecretsDefault;
    }

    public async Task<Workspace> CreateAsync(User owner, string name, CancellationToken ct)
    {
        var workspace = new Workspace
        {
            Name = name,
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            WrappedKey = secretBox.CreateWrappedDataKey()
        };
        db.Workspaces.Add(workspace);
        db.Memberships.Add(new Membership
        {
            WorkspaceId = workspace.Id,
            UserId = owner.Id,
            Role = WorkspaceRole.Owner,
            CanReadSecrets = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return workspace;
    }
}
