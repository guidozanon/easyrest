using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Endpoints;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaces(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces");

        group.MapGet("", async (Caller? caller, SyncDbContext db, CancellationToken ct) =>
        {
            if (caller == null) return Api.Unauthorized();

            if (caller.Service is { } service)
            {
                var ws = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == service.WorkspaceId, ct);
                return ws == null
                    ? Results.Ok(Array.Empty<WorkspaceResponse>())
                    : Results.Ok(new[]
                    {
                        new WorkspaceResponse(ws.Id, ws.Name, service.Role, service.CanReadSecrets,
                            ws.SeqCounter, ws.CreatedAt)
                    });
            }

            var rows = await db.Memberships
                .Where(m => m.UserId == caller.User!.Id)
                .Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new { m, w })
                .OrderBy(x => x.w.Name)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(x => new WorkspaceResponse(
                x.w.Id, x.w.Name, x.m.Role, x.m.CanReadSecrets, x.w.SeqCounter, x.w.CreatedAt)).ToArray());
        });

        group.MapPost("", async (CreateWorkspaceRequest request, Caller? caller,
            WorkspaceService workspaces, CancellationToken ct) =>
        {
            if (caller?.User is not { } user)
                return caller == null ? Api.Unauthorized()
                    : Api.Forbidden("Un token de servicio no puede crear workspaces.");
            if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("El workspace necesita un nombre.");

            var ws = await workspaces.CreateAsync(user, request.Name.Trim(), ct);
            return Results.Created($"/api/v1/workspaces/{ws.Id}", new WorkspaceResponse(
                ws.Id, ws.Name, WorkspaceRole.Owner, true, ws.SeqCounter, ws.CreatedAt));
        });

        group.MapGet("/{workspaceId:guid}", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var ws = access!.Workspace;
            return Results.Ok(new WorkspaceResponse(ws.Id, ws.Name, access.Role,
                access.CanReadSecretsDefault, ws.SeqCounter, ws.CreatedAt));
        });

        group.MapDelete("/{workspaceId:guid}", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Owner, ct);
            if (error != null) return error;

            db.Workspaces.Remove(access!.Workspace);
            db.Memberships.RemoveRange(db.Memberships.Where(m => m.WorkspaceId == workspaceId));
            db.Documents.RemoveRange(db.Documents.Where(d => d.WorkspaceId == workspaceId));
            db.SecretValues.RemoveRange(db.SecretValues.Where(s => s.WorkspaceId == workspaceId));
            db.Invitations.RemoveRange(db.Invitations.Where(i => i.WorkspaceId == workspaceId));
            db.ServiceTokens.RemoveRange(db.ServiceTokens.Where(t => t.WorkspaceId == workspaceId));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ----- Miembros -----

        group.MapGet("/{workspaceId:guid}/members", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var rows = await db.Memberships
                .Where(m => m.WorkspaceId == workspaceId)
                .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m, u })
                .ToListAsync(ct);

            return Results.Ok(rows.Select(x => new MemberResponse(
                x.u.Id, x.u.Email, x.u.DisplayName, x.m.Role, x.m.CanReadSecrets, x.m.CreatedAt)).ToArray());
        });

        group.MapPatch("/{workspaceId:guid}/members/{userId:guid}", async (
            Guid workspaceId, Guid userId, UpdateMemberRequest request, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var membership = await db.Memberships
                .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);
            if (membership == null) return Api.NotFound("Esa persona no es miembro del workspace.");

            // el owner no se degrada por accidente: quedarse sin owner deja el workspace huérfano
            if (membership.Role == WorkspaceRole.Owner && request.Role is { } newRole &&
                newRole != WorkspaceRole.Owner && !access!.AtLeast(WorkspaceRole.Owner))
                return Api.Forbidden("Sólo el owner puede cambiar el rol del owner.");

            if (request.Role is { } role) membership.Role = role;
            if (request.CanReadSecrets is { } canRead) membership.CanReadSecrets = canRead;
            await db.SaveChangesAsync(ct);

            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            return Results.Ok(new MemberResponse(user.Id, user.Email, user.DisplayName,
                membership.Role, membership.CanReadSecrets, membership.CreatedAt));
        });

        group.MapDelete("/{workspaceId:guid}/members/{userId:guid}", async (
            Guid workspaceId, Guid userId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var membership = await db.Memberships
                .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == userId, ct);
            if (membership == null) return Api.NotFound("Esa persona no es miembro del workspace.");
            if (membership.Role == WorkspaceRole.Owner)
                return Api.Forbidden("No se puede sacar al owner del workspace.");
            if (membership.UserId == access!.UserId)
                return Api.Forbidden("No te podés sacar a vos mismo: pedile a otro admin.");

            db.Memberships.Remove(membership);
            db.SecretOverrides.RemoveRange(db.SecretOverrides.Where(o => o.MembershipId == membership.Id));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ----- Invitaciones -----

        group.MapPost("/{workspaceId:guid}/invitations", async (
            Guid workspaceId, CreateInvitationRequest request, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;
            if (request.Role == WorkspaceRole.Owner)
                return Api.Invalid("No se invita como owner: transferí el workspace después.");

            var token = Tokens.Create();
            var invitation = new Invitation
            {
                WorkspaceId = workspaceId,
                Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                TokenHash = Tokens.Hash(token),
                Role = request.Role,
                CanReadSecrets = request.CanReadSecrets,
                ExpiresAt = DateTime.UtcNow.AddHours(request.ExpiresInHours is > 0 and <= 720
                    ? request.ExpiresInHours.Value : 168),
                CreatedByUserId = access!.UserId ?? Guid.Empty
            };
            db.Invitations.Add(invitation);
            await db.SaveChangesAsync(ct);

            // el token en claro se ve una sola vez, acá
            return Results.Ok(new InvitationResponse(invitation.Id, invitation.Email, invitation.Role,
                invitation.CanReadSecrets, invitation.ExpiresAt, false, token));
        });

        group.MapGet("/{workspaceId:guid}/invitations", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var rows = await db.Invitations
                .Where(i => i.WorkspaceId == workspaceId && !i.Revoked)
                .ToListAsync(ct);
            return Results.Ok(rows.Select(i => new InvitationResponse(i.Id, i.Email, i.Role,
                i.CanReadSecrets, i.ExpiresAt, i.AcceptedByUserId != null, null)).ToArray());
        });

        group.MapDelete("/{workspaceId:guid}/invitations/{invitationId:guid}", async (
            Guid workspaceId, Guid invitationId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var invitation = await db.Invitations
                .FirstOrDefaultAsync(i => i.Id == invitationId && i.WorkspaceId == workspaceId, ct);
            if (invitation == null) return Api.NotFound("La invitación no existe.");

            invitation.Revoked = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // ----- Tokens de servicio (CI, headless) -----

        group.MapPost("/{workspaceId:guid}/tokens", async (
            Guid workspaceId, CreateServiceTokenRequest request, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;
            if (string.IsNullOrWhiteSpace(request.Name)) return Api.Invalid("El token necesita un nombre.");
            if (request.Role == WorkspaceRole.Owner)
                return Api.Invalid("Un token de servicio no puede ser owner.");

            var value = AuthService.CreateServiceTokenValue();
            var token = new ServiceToken
            {
                WorkspaceId = workspaceId,
                Name = request.Name.Trim(),
                TokenHash = Tokens.Hash(value),
                Role = request.Role,
                CanReadSecrets = request.CanReadSecrets,
                ExpiresAt = request.ExpiresInDays is > 0 ? DateTime.UtcNow.AddDays(request.ExpiresInDays.Value) : null,
                CreatedAt = DateTime.UtcNow,
                CreatedByUserId = access!.UserId ?? Guid.Empty
            };
            db.ServiceTokens.Add(token);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new ServiceTokenResponse(token.Id, token.Name, token.Role,
                token.CanReadSecrets, token.ExpiresAt, false, value));
        });

        group.MapGet("/{workspaceId:guid}/tokens", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var rows = await db.ServiceTokens.Where(t => t.WorkspaceId == workspaceId).ToListAsync(ct);
            return Results.Ok(rows.Select(t => new ServiceTokenResponse(t.Id, t.Name, t.Role,
                t.CanReadSecrets, t.ExpiresAt, t.Revoked, null)).ToArray());
        });

        group.MapDelete("/{workspaceId:guid}/tokens/{tokenId:guid}", async (
            Guid workspaceId, Guid tokenId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var token = await db.ServiceTokens
                .FirstOrDefaultAsync(t => t.Id == tokenId && t.WorkspaceId == workspaceId, ct);
            if (token == null) return Api.NotFound("El token no existe.");

            token.Revoked = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Aceptar invitación: fuera del grupo de workspaces porque quien la acepta todavía
    /// no tiene acceso a ese workspace.</summary>
    public static void MapInvitationAccept(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/invitations/accept", async (
            AcceptInvitationRequest request, Caller? caller, SyncDbContext db, CancellationToken ct) =>
        {
            if (caller?.User is not { } user)
                return caller == null ? Api.Unauthorized()
                    : Api.Forbidden("Un token de servicio no puede aceptar invitaciones.");
            if (string.IsNullOrWhiteSpace(request.Token)) return Api.Invalid("Falta el token.");

            var hash = Tokens.Hash(request.Token.Trim());
            var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
            if (invitation == null) return Api.NotFound("La invitación no existe.");
            if (invitation.Revoked) return Api.Invalid("La invitación fue revocada.");
            if (invitation.AcceptedByUserId != null) return Api.Invalid("La invitación ya se usó.");
            if (invitation.ExpiresAt < DateTime.UtcNow) return Api.Invalid("La invitación venció.");
            if (invitation.Email != null &&
                !string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase))
                return Api.Forbidden("La invitación es para otra dirección de mail.");

            var existing = await db.Memberships
                .FirstOrDefaultAsync(m => m.WorkspaceId == invitation.WorkspaceId && m.UserId == user.Id, ct);
            if (existing == null)
            {
                db.Memberships.Add(new Membership
                {
                    WorkspaceId = invitation.WorkspaceId,
                    UserId = user.Id,
                    Role = invitation.Role,
                    CanReadSecrets = invitation.CanReadSecrets,
                    CreatedAt = DateTime.UtcNow
                });
            }

            invitation.AcceptedByUserId = user.Id;
            invitation.AcceptedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            var workspace = await db.Workspaces.FirstAsync(w => w.Id == invitation.WorkspaceId, ct);
            return Results.Ok(new WorkspaceResponse(workspace.Id, workspace.Name, invitation.Role,
                invitation.CanReadSecrets, workspace.SeqCounter, workspace.CreatedAt));
        });
    }
}
