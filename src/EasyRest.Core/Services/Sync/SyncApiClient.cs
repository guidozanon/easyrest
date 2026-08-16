using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyRest.Services.Sync;

// Contratos de la API del servidor de sync. Son los mismos nombres que devuelve el server,
// con la serialización web (camelCase) por defecto.

public record SyncMeta(string Server, string Version, int[] ApiVersions, string[] Capabilities, SyncMetaAuth Auth);
public record SyncMetaAuth(SyncProvider[] Providers, string[] AllowedRedirectSchemes);
public record SyncProvider(string Id, string DisplayName, string Kind);

public record SyncSession(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, SyncUser User);
public record SyncUser(Guid Id, string Email, string DisplayName, string Provider,
    bool IsServerAdmin = false);

public record SyncWorkspace(Guid Id, string Name, string Role, bool CanReadSecrets, long Cursor, DateTime CreatedAt);

public record SyncDocument(Guid Id, string Path, string Kind, string? Content, string Rev, bool Deleted,
    long Seq, DateTime UpdatedAt);

public record SyncChanges(long Cursor, bool HasMore, SyncDocument[] Documents);

public record SyncSecrets(Guid DocumentId, Dictionary<string, string> Secrets);

/// <summary>Los roles viajan como texto ("Owner", "Admin", "Member", "Viewer"): el enum vive en
/// el server y no hay razón para copiarlo acá, donde sólo se muestra y se manda de vuelta.</summary>
public record SyncMember(Guid UserId, string Email, string DisplayName, string Role,
    bool CanReadSecrets, DateTime CreatedAt);

/// <summary>El token en claro llega una única vez, al crear la invitación: después el server sólo
/// guarda el hash. Si no se copia en ese momento, se pierde y hay que crear otra.</summary>
public record SyncInvitation(Guid Id, string? Email, string Role, bool CanReadSecrets,
    DateTime ExpiresAt, bool Accepted, string? Token);

/// <summary>El server contestó 409: alguien más cambió el documento. Trae la versión del server
/// para poder resolver sin perder nada.</summary>
public class SyncConflictException(string message, SyncDocument? current) : Exception(message)
{
    public SyncDocument? Current { get; } = current;
}

public class SyncApiException(string message, HttpStatusCode status) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>Cliente HTTP del servidor de sync. No sabe nada de Google ni de GitHub: pregunta por
/// /meta qué providers hay y arma el login con el que elija la persona.</summary>
public class SyncApiClient : IDisposable
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    readonly HttpClient _http;
    readonly bool _ownsClient;

    public SyncApiClient(string baseUrl, HttpClient? http = null)
    {
        BaseUrl = baseUrl.TrimEnd('/');
        _ownsClient = http == null;
        _http = http ?? new HttpClient();
        if (_http.BaseAddress == null) _http.BaseAddress = new Uri(BaseUrl + "/");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string BaseUrl { get; }

    /// <summary>Token de acceso de la sesión. La UI lo persiste y lo vuelve a poner al arrancar.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Cómo conseguir un access token nuevo cuando el server contesta 401. Devuelve null
    /// si ya no se puede (refresh vencido o revocado), y ahí el 401 sube como error.
    ///
    /// Va acá y no en una capa de arriba porque <see cref="RemoteWorkspaceSync"/> recibe el cliente
    /// directo: si el refresh viviera afuera, una sincronización larga se caería a la mitad.</summary>
    public Func<CancellationToken, Task<string?>>? TokenRefresher { get; set; }

    public Task<SyncMeta> GetMetaAsync(CancellationToken ct = default) =>
        SendAsync<SyncMeta>(HttpMethod.Get, "api/v1/meta", null, null, ct);

    /// <summary>URL para abrir en el navegador del sistema. El code_verifier queda en la app y
    /// se manda recién al canjear: eso es PKCE, y es lo que permite ser un cliente público sin
    /// client secret.</summary>
    public string BuildLoginUrl(string providerId, string redirectUri, string codeChallenge, string state) =>
        $"{BaseUrl}/api/v1/auth/start" +
        $"?provider={Uri.EscapeDataString(providerId)}" +
        $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
        $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
        $"&state={Uri.EscapeDataString(state)}";

    public async Task<SyncSession> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct = default)
    {
        var session = await SendAsync<SyncSession>(HttpMethod.Post, "api/v1/auth/token",
            new { grantType = "authorization_code", code, codeVerifier }, null, ct);
        AccessToken = session.AccessToken;
        return session;
    }

    public async Task<SyncSession> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var session = await SendAsync<SyncSession>(HttpMethod.Post, "api/v1/auth/token",
            new { grantType = "refresh_token", refreshToken }, null, ct);
        AccessToken = session.AccessToken;
        return session;
    }

    public Task<SyncUser> GetMeAsync(CancellationToken ct = default) =>
        SendAsync<SyncUser>(HttpMethod.Get, "api/v1/me", null, null, ct);

    public Task<SyncWorkspace[]> GetWorkspacesAsync(CancellationToken ct = default) =>
        SendAsync<SyncWorkspace[]>(HttpMethod.Get, "api/v1/workspaces", null, null, ct);

    public Task<SyncWorkspace> CreateWorkspaceAsync(string name, CancellationToken ct = default) =>
        SendAsync<SyncWorkspace>(HttpMethod.Post, "api/v1/workspaces", new { name }, null, ct);

    public Task<SyncWorkspace> AcceptInvitationAsync(string token, CancellationToken ct = default) =>
        SendAsync<SyncWorkspace>(HttpMethod.Post, "api/v1/invitations/accept", new { token }, null, ct);

    public Task<SyncChanges> GetChangesAsync(Guid workspaceId, long since, int limit = 200,
        CancellationToken ct = default) =>
        SendAsync<SyncChanges>(HttpMethod.Get,
            $"api/v1/workspaces/{workspaceId}/changes?since={since}&limit={limit}", null, null, ct);

    /// <summary>Sube un documento. ifMatch null = crear, "*" = pisar, o la revisión esperada.
    /// Si el server responde 409 tira SyncConflictException con su versión.</summary>
    public Task<SyncDocument> PutDocumentAsync(Guid workspaceId, string path, string kind, string content,
        string? ifMatch, Dictionary<string, string>? secrets = null, CancellationToken ct = default) =>
        SendAsync<SyncDocument>(HttpMethod.Put, $"api/v1/workspaces/{workspaceId}/documents",
            new { path, kind, content, secrets }, ifMatch, ct);

    public Task<SyncDocument> DeleteDocumentAsync(Guid workspaceId, Guid documentId, string ifMatch,
        CancellationToken ct = default) =>
        SendAsync<SyncDocument>(HttpMethod.Delete,
            $"api/v1/workspaces/{workspaceId}/documents/{documentId}", null, ifMatch, ct);

    public Task<SyncSecrets> GetSecretsAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default) =>
        SendAsync<SyncSecrets>(HttpMethod.Get,
            $"api/v1/workspaces/{workspaceId}/documents/{documentId}/secrets", null, null, ct);

    // ----- Miembros -----

    public Task<SyncMember[]> GetMembersAsync(Guid workspaceId, CancellationToken ct = default) =>
        SendAsync<SyncMember[]>(HttpMethod.Get, $"api/v1/workspaces/{workspaceId}/members", null, null, ct);

    /// <summary>Cambia rol y/o acceso a secretos. Lo que va en null se deja como está.</summary>
    public Task<SyncMember> UpdateMemberAsync(Guid workspaceId, Guid userId, string? role = null,
        bool? canReadSecrets = null, CancellationToken ct = default) =>
        SendAsync<SyncMember>(HttpMethod.Patch, $"api/v1/workspaces/{workspaceId}/members/{userId}",
            new { role, canReadSecrets }, null, ct);

    public Task RemoveMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/workspaces/{workspaceId}/members/{userId}", null, null, ct);

    /// <summary>Sólo el owner del workspace o un administrador del server. El owner anterior queda
    /// como admin.</summary>
    public Task<SyncMember> TransferOwnershipAsync(Guid workspaceId, Guid userId,
        CancellationToken ct = default) =>
        SendAsync<SyncMember>(HttpMethod.Post, $"api/v1/workspaces/{workspaceId}/transfer-ownership",
            new { userId }, null, ct);

    // ----- Invitaciones -----

    public Task<SyncInvitation[]> GetInvitationsAsync(Guid workspaceId, CancellationToken ct = default) =>
        SendAsync<SyncInvitation[]>(HttpMethod.Get, $"api/v1/workspaces/{workspaceId}/invitations",
            null, null, ct);

    /// <summary>email null = invitación abierta, la usa quien reciba el token. La respuesta trae el
    /// token en claro y es la única vez que se puede leer.</summary>
    public Task<SyncInvitation> CreateInvitationAsync(Guid workspaceId, string? email, string role,
        bool canReadSecrets = false, int? expiresInHours = null, CancellationToken ct = default) =>
        SendAsync<SyncInvitation>(HttpMethod.Post, $"api/v1/workspaces/{workspaceId}/invitations",
            new { email, role, canReadSecrets, expiresInHours }, null, ct);

    public Task RevokeInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, $"api/v1/workspaces/{workspaceId}/invitations/{invitationId}",
            null, null, ct);

    async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? ifMatch,
        CancellationToken ct)
    {
        var (text, status) = await SendRawAsync(method, path, body, ifMatch, ct);
        return JsonSerializer.Deserialize<T>(text, Json)
               ?? throw new SyncApiException("El server devolvió una respuesta vacía.", status);
    }

    /// <summary>Para los endpoints que contestan 204 y no traen cuerpo.</summary>
    async Task SendAsync(HttpMethod method, string path, object? body, string? ifMatch,
        CancellationToken ct) =>
        await SendRawAsync(method, path, body, ifMatch, ct);

    async Task<(string Text, HttpStatusCode Status)> SendRawAsync(HttpMethod method, string path,
        object? body, string? ifMatch, CancellationToken ct)
    {
        var (text, status) = await OnceAsync();

        // Un solo reintento: si el token nuevo también da 401, el problema no es el token.
        if (status == HttpStatusCode.Unauthorized && TokenRefresher != null)
        {
            var nuevo = await TokenRefresher(ct);
            if (nuevo != null)
            {
                AccessToken = nuevo;
                (text, status) = await OnceAsync();
            }
        }

        if (status == HttpStatusCode.Conflict)
            throw new SyncConflictException(ErrorDetail(text) ?? "El documento cambió en el server.",
                ReadCurrent(text));

        if ((int)status is < 200 or > 299)
            throw new SyncApiException(ErrorDetail(text) ?? $"El server respondió {(int)status}.", status);

        return (text, status);

        async Task<(string, HttpStatusCode)> OnceAsync()
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = JsonContent.Create(body, options: Json);
            if (AccessToken != null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
            if (ifMatch != null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

            using var response = await _http.SendAsync(request, ct);
            return (await response.Content.ReadAsStringAsync(ct), response.StatusCode);
        }
    }

    static string? ErrorDetail(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static SyncDocument? ReadCurrent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Object
                ? c.Deserialize<SyncDocument>(Json)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
