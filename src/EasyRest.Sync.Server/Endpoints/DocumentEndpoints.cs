using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace EasyRest.Sync.Server.Endpoints;

public static class DocumentEndpoints
{
    public static void MapDocuments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/workspaces/{workspaceId:guid}");

        // Delta desde el cursor: es lo que hace viable el móvil, que nunca baja el workspace entero.
        group.MapGet("/changes", async (Guid workspaceId, long? since, int? limit, Caller? caller,
            WorkspaceService workspaces, DocumentService documents, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            return Results.Ok(await documents.ChangesAsync(access!, since ?? 0, limit ?? 200, ct));
        });

        group.MapGet("/documents", async (Guid workspaceId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var docs = await db.Documents
                .Where(d => d.WorkspaceId == workspaceId && !d.Deleted)
                .OrderBy(d => d.Path)
                .ToListAsync(ct);
            return Results.Ok(docs.Select(DocumentService.ToResponse).ToArray());
        });

        group.MapGet("/documents/{documentId:guid}", async (Guid workspaceId, Guid documentId, Caller? caller,
            WorkspaceService workspaces, SyncDbContext db, CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var doc = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct);
            return doc == null ? Api.NotFound("El documento no existe.") : Results.Ok(DocumentService.ToResponse(doc));
        });

        // If-Match ausente = crear, "*" = pisar, cualquier otro valor = la revisión esperada.
        group.MapPut("/documents", async (Guid workspaceId, PutDocumentRequest request, HttpContext http,
            Caller? caller, WorkspaceService workspaces, DocumentService documents, CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var ifMatch = http.Request.Headers.IfMatch.ToString();
            var result = await documents.PutAsync(access!, request, ifMatch, ct);

            return result.Outcome switch
            {
                WriteOutcome.Ok => Results.Ok(DocumentService.ToResponse(result.Document!)),
                WriteOutcome.Invalid => Api.Invalid(result.Error!),
                WriteOutcome.Conflict => Api.Conflict(
                    "El documento cambió en el server: pediste otra revisión.",
                    DocumentService.ToResponse(result.Document!)),
                _ => Api.NotFound("El documento no existe.")
            };
        });

        group.MapDelete("/documents/{documentId:guid}", async (Guid workspaceId, Guid documentId,
            HttpContext http, Caller? caller, WorkspaceService workspaces, DocumentService documents,
            CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var result = await documents.DeleteAsync(access!, documentId, http.Request.Headers.IfMatch.ToString(), ct);
            return result.Outcome switch
            {
                WriteOutcome.Ok => Results.Ok(DocumentService.ToResponse(result.Document!)),
                WriteOutcome.Conflict => Api.Conflict(
                    "El documento cambió en el server: pediste otra revisión.",
                    DocumentService.ToResponse(result.Document!)),
                _ => Api.NotFound("El documento no existe.")
            };
        });

        // Los valores de los secretos viven acá y sólo acá: nunca viajan dentro del documento.
        group.MapGet("/documents/{documentId:guid}/secrets", async (Guid workspaceId, Guid documentId,
            Caller? caller, WorkspaceService workspaces, DocumentService documents, SyncDbContext db,
            CancellationToken ct) =>
        {
            var (access, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Member, ct);
            if (error != null) return error;

            var doc = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct);
            if (doc == null || doc.Deleted) return Api.NotFound("El documento no existe.");

            if (!await workspaces.CanAccessSecretsAsync(access!, doc.Id, ct))
                return Api.Forbidden("No tenés permiso para ver los secretos de este ambiente.");

            return Results.Ok(new SecretsResponse(doc.Id, await documents.ReadSecretsAsync(access!, doc, ct)));
        });

        // Excepción al default del miembro, para un ambiente puntual.
        group.MapPut("/documents/{documentId:guid}/secret-access", async (Guid workspaceId, Guid documentId,
            SecretOverrideRequest request, Caller? caller, WorkspaceService workspaces, SyncDbContext db,
            CancellationToken ct) =>
        {
            var (_, error) = await Api.AccessAsync(workspaces, caller, workspaceId, WorkspaceRole.Admin, ct);
            if (error != null) return error;

            var doc = await db.Documents
                .FirstOrDefaultAsync(d => d.Id == documentId && d.WorkspaceId == workspaceId, ct);
            if (doc == null || doc.Deleted) return Api.NotFound("El documento no existe.");

            var membership = await db.Memberships
                .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == request.UserId, ct);
            if (membership == null) return Api.NotFound("Esa persona no es miembro del workspace.");

            var over = await db.SecretOverrides
                .FirstOrDefaultAsync(o => o.MembershipId == membership.Id && o.DocumentId == documentId, ct);

            if (request.CanRead is not { } canRead)
            {
                // sin valor: se borra la excepción y vuelve a mandar el default de la membresía
                if (over != null) db.SecretOverrides.Remove(over);
            }
            else if (over == null)
            {
                db.SecretOverrides.Add(new SecretOverride
                {
                    MembershipId = membership.Id,
                    DocumentId = documentId,
                    CanRead = canRead
                });
            }
            else
            {
                over.CanRead = canRead;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
