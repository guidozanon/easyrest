using EasyRest.Sync.Server.Data;

namespace EasyRest.Sync.Server;

// Contratos de la API v1. Los nombres van en camelCase por la config de JSON del host.

public record MetaResponse(
    string Server,
    string Version,
    int[] ApiVersions,
    string[] Capabilities,
    MetaAuth Auth);

public record MetaAuth(MetaProvider[] Providers, string[] AllowedRedirectSchemes);

public record MetaProvider(string Id, string DisplayName, string Kind);

public record TokenRequest(
    string? GrantType,
    string? Code,
    string? CodeVerifier,
    string? RefreshToken);

public record TokenResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    UserResponse User);

public record UserResponse(Guid Id, string Email, string DisplayName, string Provider);

public record WorkspaceResponse(
    Guid Id,
    string Name,
    WorkspaceRole Role,
    bool CanReadSecrets,
    long Cursor,
    DateTime CreatedAt);

public record CreateWorkspaceRequest(string Name);

public record MemberResponse(
    Guid UserId,
    string Email,
    string DisplayName,
    WorkspaceRole Role,
    bool CanReadSecrets,
    DateTime CreatedAt);

public record UpdateMemberRequest(WorkspaceRole? Role, bool? CanReadSecrets);

public record CreateInvitationRequest(
    string? Email,
    WorkspaceRole Role,
    bool CanReadSecrets,
    int? ExpiresInHours);

/// <summary>El token en claro se devuelve una única vez, al crearla: después sólo queda el hash.</summary>
public record InvitationResponse(
    Guid Id,
    string? Email,
    WorkspaceRole Role,
    bool CanReadSecrets,
    DateTime ExpiresAt,
    bool Accepted,
    string? Token);

public record AcceptInvitationRequest(string Token);

public record CreateServiceTokenRequest(
    string Name,
    WorkspaceRole Role,
    bool CanReadSecrets,
    int? ExpiresInDays);

public record ServiceTokenResponse(
    Guid Id,
    string Name,
    WorkspaceRole Role,
    bool CanReadSecrets,
    DateTime? ExpiresAt,
    bool Revoked,
    string? Token);

/// <summary>Un documento tal como viaja. Content nunca trae valores de secretos; para eso está
/// el endpoint /secrets, que además exige permiso.</summary>
public record DocumentResponse(
    Guid Id,
    string Path,
    string Kind,
    string? Content,
    string Rev,
    bool Deleted,
    long Seq,
    DateTime UpdatedAt);

public record PutDocumentRequest(
    string Path,
    string Kind,
    string Content,
    Dictionary<string, string>? Secrets);

public record ChangesResponse(long Cursor, bool HasMore, DocumentResponse[] Documents);

public record SecretsResponse(Guid DocumentId, Dictionary<string, string> Secrets);

public record SecretOverrideRequest(Guid UserId, bool? CanRead);

public record ErrorResponse(string Error, string? Detail = null);
