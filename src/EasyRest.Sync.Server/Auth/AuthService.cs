using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyRest.Sync.Server.Auth;

/// <summary>Quién está haciendo la request: una persona logueada o un token de servicio.
/// Se resuelve solo en cada handler que lo pida como parámetro (BindAsync de minimal APIs);
/// si el token falta o no sirve, llega null y el handler contesta 401.</summary>
public class Caller
{
    public User? User { get; init; }
    public ServiceToken? Service { get; init; }

    public bool IsService => Service != null;
    public Guid? UserId => User?.Id;

    public static async ValueTask<Caller?> BindAsync(HttpContext context)
    {
        var auth = context.RequestServices.GetRequiredService<AuthService>();
        return await auth.ResolveCallerAsync(
            context.Request.Headers.Authorization.ToString(), context.RequestAborted);
    }
}

public record SessionTokens(string AccessToken, string RefreshToken, DateTime AccessExpiresAt);

public class AuthException(string message) : Exception(message);

/// <summary>El flujo de login de la app: Authorization Code + PKCE contra este server, que a su
/// vez habla con el IdP de la organización. La app nunca ve credenciales del IdP ni necesita un
/// client secret — es un cliente público.</summary>
public class AuthService(SyncDbContext db, IdentityProviderRegistry providers, IOptions<AuthOptions> options)
{
    readonly AuthOptions _options = options.Value;

    public string CallbackUrl => _options.PublicUrl.TrimEnd('/') + "/api/v1/auth/callback";

    /// <summary>Arranca el login: valida el redirect de la app, guarda el PKCE challenge y
    /// devuelve la URL del IdP.</summary>
    public async Task<string> StartAsync(string providerId, string redirectUri, string codeChallenge,
        string clientState, CancellationToken ct)
    {
        var provider = providers.Find(providerId)
            ?? throw new AuthException($"El provider '{providerId}' no está configurado en este server.");

        if (string.IsNullOrWhiteSpace(_options.PublicUrl))
            throw new AuthException("El server no tiene configurado Auth:PublicUrl.");
        if (!IsAllowedRedirect(redirectUri))
            throw new AuthException("El redirect_uri no está permitido: usá loopback o un esquema registrado.");
        if (string.IsNullOrWhiteSpace(codeChallenge))
            throw new AuthException("Falta el code_challenge (PKCE S256 es obligatorio).");

        var flow = new AuthFlow
        {
            State = Tokens.Create(),
            Provider = provider.Id,
            RedirectUri = redirectUri,
            ClientState = clientState ?? "",
            CodeChallenge = codeChallenge,
            ExpiresAt = DateTime.UtcNow.AddMinutes(10)
        };
        db.AuthFlows.Add(flow);
        await db.SaveChangesAsync(ct);

        return await provider.BuildAuthorizationUrlAsync(flow.State, CallbackUrl, ct);
    }

    /// <summary>Vuelve el IdP: se canjea el code, se resuelve la persona y se emite un
    /// authorization code propio para que la app lo cambie por tokens.</summary>
    public async Task<string> CallbackAsync(string state, string code, CancellationToken ct)
    {
        var flow = await db.AuthFlows.FirstOrDefaultAsync(f => f.State == state, ct)
            ?? throw new AuthException("El login no existe o ya se usó.");
        if (flow.Consumed || flow.ExpiresAt < DateTime.UtcNow)
            throw new AuthException("El login venció: probá de nuevo.");

        var provider = providers.Find(flow.Provider)
            ?? throw new AuthException("El provider ya no está configurado.");

        var identity = await provider.ExchangeAsync(code, CallbackUrl, ct);
        EnsureEmailAllowed(identity.Email);

        var user = await UpsertUserAsync(provider.Id, identity, ct);

        var authCode = Tokens.Create();
        flow.AuthCodeHash = Tokens.Hash(authCode);
        flow.UserId = user.Id;
        flow.ExpiresAt = DateTime.UtcNow.AddMinutes(5);
        await db.SaveChangesAsync(ct);

        var separator = flow.RedirectUri.Contains('?') ? "&" : "?";
        return flow.RedirectUri + separator +
               OidcIdentityProvider.Query(new Dictionary<string, string>
               {
                   ["code"] = authCode,
                   ["state"] = flow.ClientState
               });
    }

    /// <summary>Canje final: authorization code + code_verifier → tokens de sesión.</summary>
    public async Task<(SessionTokens Tokens, User User)> ExchangeCodeAsync(string code, string codeVerifier,
        CancellationToken ct)
    {
        var hash = Tokens.Hash(code);
        var flow = await db.AuthFlows.FirstOrDefaultAsync(f => f.AuthCodeHash == hash, ct)
            ?? throw new AuthException("El código no es válido.");

        if (flow.Consumed) throw new AuthException("El código ya se usó.");
        if (flow.ExpiresAt < DateTime.UtcNow) throw new AuthException("El código venció.");
        if (!Tokens.VerifyPkce(flow.CodeChallenge, codeVerifier))
            throw new AuthException("El code_verifier no corresponde al code_challenge.");
        if (flow.UserId is not { } userId) throw new AuthException("El login no llegó a completarse.");

        // de un solo uso, aunque después falle algo: un code reutilizable es un code robado útil
        flow.Consumed = true;
        await db.SaveChangesAsync(ct);

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        return (await IssueSessionAsync(user, ct), user);
    }

    public async Task<(SessionTokens Tokens, User User)> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var hash = Tokens.Hash(refreshToken);
        var session = await db.SessionTokens.FirstOrDefaultAsync(s => s.RefreshHash == hash, ct)
            ?? throw new AuthException("El refresh token no es válido.");
        if (session.Revoked) throw new AuthException("La sesión fue revocada.");
        if (session.RefreshExpiresAt < DateTime.UtcNow) throw new AuthException("El refresh token venció.");

        // rotación: el refresh viejo muere al usarse
        session.Revoked = true;
        var user = await db.Users.FirstAsync(u => u.Id == session.UserId, ct);
        var tokens = await IssueSessionAsync(user, ct);
        return (tokens, user);
    }

    public async Task LogoutAsync(string accessToken, CancellationToken ct)
    {
        var hash = Tokens.Hash(accessToken);
        var session = await db.SessionTokens.FirstOrDefaultAsync(s => s.AccessHash == hash, ct);
        if (session == null) return;
        session.Revoked = true;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Resuelve el Authorization: Bearer. Sirve tanto para sesiones de personas como
    /// para tokens de servicio, que se distinguen por el prefijo.</summary>
    public async Task<Caller?> ResolveCallerAsync(string? authorizationHeader, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)) return null;
        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (token.Length == 0) return null;
        var hash = Tokens.Hash(token);

        if (token.StartsWith(ServiceTokenPrefix, StringComparison.Ordinal))
        {
            var service = await db.ServiceTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
            if (service == null || service.Revoked) return null;
            if (service.ExpiresAt is { } exp && exp < DateTime.UtcNow) return null;
            return new Caller { Service = service };
        }

        var session = await db.SessionTokens.FirstOrDefaultAsync(s => s.AccessHash == hash, ct);
        if (session == null || session.Revoked || session.AccessExpiresAt < DateTime.UtcNow) return null;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == session.UserId, ct);
        return user == null ? null : new Caller { User = user };
    }

    /// <summary>Los tokens de servicio se distinguen a simple vista: ayuda a no pegarlos donde
    /// no van y permite resolverlos sin consultar dos tablas.</summary>
    public const string ServiceTokenPrefix = "ert_";

    public static string CreateServiceTokenValue() => ServiceTokenPrefix + Tokens.Create();

    async Task<SessionTokens> IssueSessionAsync(User user, CancellationToken ct)
    {
        var access = Tokens.Create();
        var refresh = Tokens.Create();
        var accessExpires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        db.SessionTokens.Add(new SessionToken
        {
            UserId = user.Id,
            AccessHash = Tokens.Hash(access),
            RefreshHash = Tokens.Hash(refresh),
            AccessExpiresAt = accessExpires,
            RefreshExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            CreatedAt = DateTime.UtcNow
        });

        user.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new SessionTokens(access, refresh, accessExpires);
    }

    async Task<User> UpsertUserAsync(string providerId, ExternalIdentity identity, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Provider == providerId && u.Subject == identity.Subject, ct);

        if (user == null)
        {
            if (!_options.AllowOpenRegistration && await db.Users.AnyAsync(ct))
                throw new AuthException("Este server no acepta registros nuevos.");

            user = new User
            {
                Provider = providerId,
                Subject = identity.Subject,
                CreatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
        }

        // el mail y el nombre pueden cambiar en el IdP: la identidad estable es (provider, sub)
        user.Email = identity.Email;
        user.DisplayName = string.IsNullOrWhiteSpace(identity.DisplayName) ? identity.Email : identity.DisplayName;
        user.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return user;
    }

    void EnsureEmailAllowed(string email)
    {
        if (_options.AllowedEmailDomains.Count == 0) return;

        var at = email.LastIndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..] : "";
        if (!_options.AllowedEmailDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
            throw new AuthException($"El dominio '{domain}' no está habilitado en este server.");
    }

    /// <summary>Loopback (desktop) o un esquema propio registrado (móvil). Cualquier otra cosa
    /// se rechaza: un redirect abierto acá es entregarle el código a cualquiera.</summary>
    internal bool IsAllowedRedirect(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)) return false;

        if (uri.Scheme is "http" or "https")
            return uri.Scheme == "http" && uri.IsLoopback;

        return _options.AllowedRedirectSchemes
            .Any(s => string.Equals(s, uri.Scheme, StringComparison.OrdinalIgnoreCase));
    }
}
