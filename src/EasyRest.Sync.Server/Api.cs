using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Services;

namespace EasyRest.Sync.Server;

/// <summary>Respuestas de error uniformes. Cuando el caller no tiene acceso a un workspace se
/// devuelve 404 y no 403: un 403 ya confirmaría que ese workspace existe.</summary>
public static class Api
{
    public static IResult Unauthorized(string detail = "Falta un token válido.") =>
        Results.Json(new ErrorResponse("unauthorized", detail), statusCode: 401);

    public static IResult Forbidden(string detail) =>
        Results.Json(new ErrorResponse("forbidden", detail), statusCode: 403);

    public static IResult NotFound(string detail = "No existe o no tenés acceso.") =>
        Results.Json(new ErrorResponse("not_found", detail), statusCode: 404);

    public static IResult Invalid(string detail) =>
        Results.Json(new ErrorResponse("invalid_request", detail), statusCode: 400);

    public static IResult Conflict(string detail, DocumentResponse? current = null) =>
        Results.Json(new { error = "conflict", detail, current }, statusCode: 409);

    /// <summary>Resuelve el workspace y exige un rol mínimo. Devuelve el acceso o el IResult que
    /// hay que contestar.</summary>
    public static async Task<(WorkspaceAccess? Access, IResult? Error)> AccessAsync(
        WorkspaceService workspaces, Caller? caller, Guid workspaceId, WorkspaceRole minimum,
        CancellationToken ct)
    {
        if (caller == null) return (null, Unauthorized());

        var access = await workspaces.ResolveAsync(caller, workspaceId, ct);
        if (access == null) return (null, NotFound());
        if (!access.AtLeast(minimum))
            return (null, Forbidden($"Hace falta rol {minimum} en este workspace."));

        return (access, null);
    }
}
