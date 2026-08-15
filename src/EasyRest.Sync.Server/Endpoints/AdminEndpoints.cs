using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Endpoints;

/// <summary>Administración del server, no de un workspace: usuarios y vista global. Todo esto
/// pide IsServerAdmin, que es un permiso distinto de los roles de workspace — un owner manda en
/// lo suyo y no tiene por qué ver el resto del server.</summary>
public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin");

        group.MapGet("/users", async (Caller? caller, SyncDbContext db, CancellationToken ct) =>
        {
            if (Deny(caller) is { } error) return error;

            var users = await db.Users.OrderBy(u => u.Email).ToListAsync(ct);
            var counts = await db.Memberships
                .GroupBy(m => m.UserId)
                .Select(g => new { UserId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

            return Results.Ok(users.Select(u => new AdminUserResponse(
                u.Id, u.Email, u.DisplayName, u.Provider, u.IsServerAdmin, u.Disabled,
                counts.GetValueOrDefault(u.Id), u.CreatedAt, u.LastSeenAt)).ToArray());
        });

        group.MapPatch("/users/{userId:guid}", async (Guid userId, UpdateUserRequest request,
            Caller? caller, AdminService admin, SyncDbContext db, CancellationToken ct) =>
        {
            if (Deny(caller) is { } error) return error;

            var result = await admin.UpdateUserAsync(caller!.User!.Id, userId,
                request.IsServerAdmin, request.Disabled, ct);
            if (result.Outcome != AdminOutcome.Ok) return Map(result);

            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            return Results.Ok(new AdminUserResponse(user.Id, user.Email, user.DisplayName, user.Provider,
                user.IsServerAdmin, user.Disabled, 0, user.CreatedAt, user.LastSeenAt));
        });

        group.MapGet("/workspaces", async (Caller? caller, SyncDbContext db, CancellationToken ct) =>
        {
            if (Deny(caller) is { } error) return error;

            var workspaces = await db.Workspaces.OrderBy(w => w.Name).ToListAsync(ct);
            var members = await db.Memberships
                .GroupBy(m => m.WorkspaceId)
                .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);
            var docs = await db.Documents
                .Where(d => !d.Deleted)
                .GroupBy(d => d.WorkspaceId)
                .Select(g => new { WorkspaceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.WorkspaceId, x => x.Count, ct);
            var owners = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Email, ct);

            return Results.Ok(workspaces.Select(w => new AdminWorkspaceResponse(
                w.Id, w.Name, owners.GetValueOrDefault(w.OwnerUserId, "(sin dueño)"),
                members.GetValueOrDefault(w.Id), docs.GetValueOrDefault(w.Id),
                w.SeqCounter, w.CreatedAt)).ToArray());
        });

        static IResult? Deny(Caller? caller) => caller switch
        {
            null => Api.Unauthorized(),
            { User: { IsServerAdmin: true } } => null,
            _ => Api.Forbidden("Hace falta ser administrador del server.")
        };
    }

    static IResult Map(AdminResult result) => result.Outcome switch
    {
        AdminOutcome.NotFound => Api.NotFound(result.Error!),
        AdminOutcome.Forbidden => Api.Forbidden(result.Error!),
        _ => Results.NoContent()
    };

    /// <summary>Transferir el ownership vive con los workspaces, no en /admin: lo puede hacer el
    /// propio owner. Sin esto, si esa persona se va, el workspace queda sin quien lo administre
    /// — el rol de owner no se puede sacar ni degradar.</summary>
    public static void MapOwnershipTransfer(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/workspaces/{workspaceId:guid}/transfer-ownership", async (
            Guid workspaceId, TransferOwnershipRequest request, Caller? caller,
            WorkspaceService workspaces, AdminService admin, SyncDbContext db, CancellationToken ct) =>
        {
            if (caller == null) return Api.Unauthorized();

            var esAdminDelServer = caller.User is { IsServerAdmin: true };
            var access = await workspaces.ResolveAsync(caller, workspaceId, ct);

            if (access == null && !esAdminDelServer) return Api.NotFound();
            if (access != null && !access.AtLeast(WorkspaceRole.Owner) && !esAdminDelServer)
                return Api.Forbidden("Sólo el owner o un administrador del server pueden transferir.");

            var result = await admin.TransferOwnershipAsync(workspaceId, request.UserId, ct);
            if (result.Outcome != AdminOutcome.Ok) return Map(result);

            var user = await db.Users.FirstAsync(u => u.Id == request.UserId, ct);
            var membership = await db.Memberships
                .FirstAsync(m => m.WorkspaceId == workspaceId && m.UserId == request.UserId, ct);
            return Results.Ok(new MemberResponse(user.Id, user.Email, user.DisplayName,
                membership.Role, membership.CanReadSecrets, membership.CreatedAt));
        });
    }
}
