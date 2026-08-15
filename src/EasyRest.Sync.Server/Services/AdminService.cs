using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Services;

public enum AdminOutcome { Ok, NotFound, Forbidden }

public record AdminResult(AdminOutcome Outcome, string? Error = null)
{
    public static readonly AdminResult Ok = new(AdminOutcome.Ok);
    public static AdminResult NotFound(string error) => new(AdminOutcome.NotFound, error);
    public static AdminResult Forbidden(string error) => new(AdminOutcome.Forbidden, error);
}

/// <summary>Las operaciones de administración del server, en un solo lugar: las usan igual los
/// endpoints de la API y las páginas de la consola. Si vivieran en los endpoints, la consola
/// tendría su propia copia de las reglas y tarde o temprano divergirían.</summary>
public class AdminService(SyncDbContext db)
{
    public async Task<AdminResult> UpdateUserAsync(Guid actingUserId, Guid targetUserId,
        bool? isServerAdmin, bool? disabled, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (user == null) return AdminResult.NotFound("El usuario no existe.");

        // dos protecciones contra quedarse afuera del propio server
        if (user.Id == actingUserId && disabled == true)
            return AdminResult.Forbidden("No te podés desactivar a vos mismo.");

        var quitandoAdmin = isServerAdmin == false && user.IsServerAdmin;
        if (quitandoAdmin && await db.Users.CountAsync(u => u.IsServerAdmin && !u.Disabled, ct) <= 1)
            return AdminResult.Forbidden("Es el último administrador del server: nombrá a otro antes.");

        if (isServerAdmin is { } admin) user.IsServerAdmin = admin;
        if (disabled is { } value)
        {
            user.Disabled = value;
            // desactivar corta el acceso ahora: sin esto seguiría entrando hasta que venciera
            // el access token
            if (value)
                await db.SessionTokens
                    .Where(s => s.UserId == targetUserId && !s.Revoked)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revoked, true), ct);
        }

        await db.SaveChangesAsync(ct);
        return AdminResult.Ok;
    }

    /// <summary>Transfiere el workspace. El dueño anterior baja a Admin: sacarlo del todo sería
    /// una forma fácil de perder el acceso por accidente.</summary>
    public async Task<AdminResult> TransferOwnershipAsync(Guid workspaceId, Guid newOwnerUserId,
        CancellationToken ct)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (workspace == null) return AdminResult.NotFound("El workspace no existe.");

        var nuevo = await db.Memberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == newOwnerUserId, ct);
        if (nuevo == null) return AdminResult.NotFound("El nuevo dueño tiene que ser miembro del workspace.");

        var anterior = await db.Memberships
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceRole.Owner, ct);
        if (anterior != null && anterior.Id != nuevo.Id) anterior.Role = WorkspaceRole.Admin;

        nuevo.Role = WorkspaceRole.Owner;
        nuevo.CanReadSecrets = true;
        workspace.OwnerUserId = newOwnerUserId;
        await db.SaveChangesAsync(ct);
        return AdminResult.Ok;
    }
}
