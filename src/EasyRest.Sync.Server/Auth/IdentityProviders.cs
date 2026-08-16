using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EasyRest.Sync.Server.Auth;

/// <summary>Lo que devuelve el IdP sobre la persona que se logueó.</summary>
public record ExternalIdentity(string Subject, string Email, string DisplayName);

/// <summary>Un proveedor de identidad. Hay dos implementaciones y alcanzan para todo:
/// OidcIdentityProvider (Google, Microsoft Entra, Okta, Auth0, Keycloak, Authentik…) y
/// GitHubIdentityProvider, que existe sólo porque GitHub es OAuth2 sin OIDC.</summary>
public interface IIdentityProvider
{
    string Id { get; }
    string DisplayName { get; }
    string Kind { get; }

    /// <summary>URL del IdP a la que mandamos al navegador.</summary>
    Task<string> BuildAuthorizationUrlAsync(string state, string callbackUrl, CancellationToken ct);

    /// <summary>Canjea el code del IdP por la identidad de la persona.</summary>
    Task<ExternalIdentity> ExchangeAsync(string code, string callbackUrl, CancellationToken ct);

    /// <summary>Chequeo de configuración para la consola: null si está bien, o el problema en
    /// castellano. Es donde se pierde más tiempo al instalar, así que conviene poder verlo sin
    /// leer logs.</summary>
    Task<string?> DiagnoseAsync(CancellationToken ct) => Task.FromResult<string?>(null);
}

public class OidcIdentityProvider(ProviderOptions options, IHttpClientFactory httpFactory) : IIdentityProvider
{
    OidcDiscovery? _discovery;

    public string Id => options.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(options.DisplayName) ? options.Id : options.DisplayName;
    public string Kind => "oidc";

    record OidcDiscovery(string AuthorizationEndpoint, string TokenEndpoint, string? UserInfoEndpoint, string Issuer);

    async Task<OidcDiscovery> DiscoverAsync(CancellationToken ct)
    {
        if (_discovery != null) return _discovery;

        var url = options.Authority.TrimEnd('/') + "/.well-known/openid-configuration";
        using var client = httpFactory.CreateClient("idp");
        using var resp = await client.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;

        _discovery = new OidcDiscovery(
            root.GetProperty("authorization_endpoint").GetString()!,
            root.GetProperty("token_endpoint").GetString()!,
            root.TryGetProperty("userinfo_endpoint", out var ui) ? ui.GetString() : null,
            root.TryGetProperty("issuer", out var iss) ? iss.GetString() ?? options.Authority : options.Authority);
        return _discovery;
    }

    public async Task<string?> DiagnoseAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Authority)) return "Falta la Authority del IdP.";
        if (string.IsNullOrWhiteSpace(options.ClientId)) return "Falta el ClientId.";
        if (string.IsNullOrWhiteSpace(options.ClientSecret)) return "Falta el ClientSecret.";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            await DiscoverAsync(cts.Token);
            return null;
        }
        catch (Exception ex)
        {
            return $"No se pudo leer {options.Authority.TrimEnd('/')}/.well-known/openid-configuration: {ex.Message}";
        }
    }

    public async Task<string> BuildAuthorizationUrlAsync(string state, string callbackUrl, CancellationToken ct)
    {
        var d = await DiscoverAsync(ct);
        var scopes = string.IsNullOrWhiteSpace(options.Scopes) ? "openid email profile" : options.Scopes;
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = callbackUrl,
            ["scope"] = scopes,
            ["state"] = state
        };
        return d.AuthorizationEndpoint + "?" + Query(q);
    }

    public async Task<ExternalIdentity> ExchangeAsync(string code, string callbackUrl, CancellationToken ct)
    {
        var d = await DiscoverAsync(ct);
        using var client = httpFactory.CreateClient("idp");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callbackUrl,
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, d.TokenEndpoint) { Content = form };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await client.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new IdentityProviderException($"El IdP rechazó el canje del código ({(int)resp.StatusCode}).");

        using var tokenDoc = JsonDocument.Parse(body);
        var idToken = tokenDoc.RootElement.TryGetProperty("id_token", out var it) ? it.GetString() : null;

        // El id_token llega por el back-channel sobre TLS directo contra el token endpoint, así
        // que alcanza con validar iss/aud/exp (OIDC Core 3.1.3.7). Igual, si no trae mail, se
        // consulta userinfo antes de darse por vencido.
        ExternalIdentity? identity = idToken != null ? ReadIdToken(idToken, d.Issuer) : null;

        if ((identity == null || string.IsNullOrWhiteSpace(identity.Email)) && d.UserInfoEndpoint != null)
        {
            var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            if (accessToken != null)
            {
                var fromUserInfo = await ReadUserInfoAsync(client, d.UserInfoEndpoint, accessToken, ct);
                if (fromUserInfo != null)
                    identity = new ExternalIdentity(
                        identity?.Subject is { Length: > 0 } s ? s : fromUserInfo.Subject,
                        fromUserInfo.Email,
                        string.IsNullOrWhiteSpace(fromUserInfo.DisplayName)
                            ? identity?.DisplayName ?? "" : fromUserInfo.DisplayName);
            }
        }

        if (identity == null || string.IsNullOrWhiteSpace(identity.Subject))
            throw new IdentityProviderException("El IdP no devolvió una identidad utilizable.");

        return identity;
    }

    ExternalIdentity? ReadIdToken(string idToken, string issuer)
    {
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;

        using var doc = JsonDocument.Parse(DecodeSegment(parts[1]));
        var c = doc.RootElement;

        var iss = c.TryGetProperty("iss", out var i) ? i.GetString() : null;
        if (iss != null && !string.Equals(iss.TrimEnd('/'), issuer.TrimEnd('/'), StringComparison.Ordinal))
            throw new IdentityProviderException("El emisor del id_token no coincide con el configurado.");

        if (!AudienceMatches(c))
            throw new IdentityProviderException("El id_token no está emitido para este client_id.");

        if (c.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var expSeconds) &&
            DateTimeOffset.FromUnixTimeSeconds(expSeconds) < DateTimeOffset.UtcNow)
            throw new IdentityProviderException("El id_token está vencido.");

        var sub = c.TryGetProperty("sub", out var s) ? s.GetString() ?? "" : "";
        var email = c.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
        var name = c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        return new ExternalIdentity(sub, email, name);
    }

    bool AudienceMatches(JsonElement claims)
    {
        if (!claims.TryGetProperty("aud", out var aud)) return true;   // sin aud no hay nada que comparar
        if (aud.ValueKind == JsonValueKind.String) return aud.GetString() == options.ClientId;
        if (aud.ValueKind == JsonValueKind.Array)
            return aud.EnumerateArray().Any(x => x.GetString() == options.ClientId);
        return false;
    }

    static async Task<ExternalIdentity?> ReadUserInfoAsync(HttpClient client, string endpoint,
        string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var c = doc.RootElement;
        return new ExternalIdentity(
            c.TryGetProperty("sub", out var s) ? s.GetString() ?? "" : "",
            c.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "",
            c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
    }

    static byte[] DecodeSegment(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }

    internal static string Query(Dictionary<string, string> values) =>
        string.Join("&", values.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
}

/// <summary>GitHub no implementa OIDC: hay que pegarle a su API para saber quién entró.</summary>
public class GitHubIdentityProvider(ProviderOptions options, IHttpClientFactory httpFactory) : IIdentityProvider
{
    public string Id => options.Id;
    public string DisplayName => string.IsNullOrWhiteSpace(options.DisplayName) ? "GitHub" : options.DisplayName;
    public string Kind => "github";

    public Task<string?> DiagnoseAsync(CancellationToken ct) => Task.FromResult(
        string.IsNullOrWhiteSpace(options.ClientId) ? "Falta el ClientId."
        : string.IsNullOrWhiteSpace(options.ClientSecret) ? "Falta el ClientSecret."
        : null);

    public Task<string> BuildAuthorizationUrlAsync(string state, string callbackUrl, CancellationToken ct)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = callbackUrl,
            ["scope"] = string.IsNullOrWhiteSpace(options.Scopes) ? "read:user user:email" : options.Scopes,
            ["state"] = state
        };
        return Task.FromResult("https://github.com/login/oauth/authorize?" + OidcIdentityProvider.Query(q));
    }

    public async Task<ExternalIdentity> ExchangeAsync(string code, string callbackUrl, CancellationToken ct)
    {
        using var client = httpFactory.CreateClient("idp");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = callbackUrl
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
        { Content = form };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new IdentityProviderException($"GitHub rechazó el canje del código ({(int)resp.StatusCode}).");

        using var tokenDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var accessToken = tokenDoc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(accessToken))
            throw new IdentityProviderException("GitHub no devolvió un access_token.");

        var user = await GetJsonAsync(client, "https://api.github.com/user", accessToken, ct)
            ?? throw new IdentityProviderException("No se pudo leer el usuario de GitHub.");

        var subject = user.TryGetProperty("id", out var id) ? id.ToString() : "";
        var name = user.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
            ? n.GetString() ?? "" : "";
        var login = user.TryGetProperty("login", out var l) ? l.GetString() ?? "" : "";
        var email = user.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString() ?? "" : "";

        // el mail del perfil puede ser privado: ahí hay que ir a /user/emails y quedarse con el
        // primario verificado
        if (string.IsNullOrWhiteSpace(email))
            email = await PrimaryEmailAsync(client, accessToken, ct) ?? "";

        return new ExternalIdentity(subject, email, string.IsNullOrWhiteSpace(name) ? login : name);
    }

    static async Task<string?> PrimaryEmailAsync(HttpClient client, string accessToken, CancellationToken ct)
    {
        var emails = await GetJsonAsync(client, "https://api.github.com/user/emails", accessToken, ct);
        if (emails is not { ValueKind: JsonValueKind.Array }) return null;

        foreach (var item in emails.Value.EnumerateArray())
        {
            var primary = item.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True;
            var verified = item.TryGetProperty("verified", out var v) && v.ValueKind == JsonValueKind.True;
            if (primary && verified && item.TryGetProperty("email", out var em))
                return em.GetString();
        }
        return null;
    }

    static async Task<JsonElement?> GetJsonAsync(HttpClient client, string url, string accessToken,
        CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.UserAgent.ParseAdd("EasyRest-Sync-Server");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await client.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) return null;

        // el JsonDocument se descarta al salir: hay que clonar el elemento
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }
}

public class IdentityProviderException(string message) : Exception(message);

/// <summary>Arma los providers a partir de la configuración.</summary>
public class IdentityProviderRegistry
{
    readonly Dictionary<string, IIdentityProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registro armado a mano. Lo usan los tests para enchufar un IdP falso sin tener
    /// que levantar un Keycloak.</summary>
    public IdentityProviderRegistry(IEnumerable<IIdentityProvider> providers)
    {
        foreach (var p in providers) _providers[p.Id] = p;
    }

    public IdentityProviderRegistry(AuthOptions options, IHttpClientFactory httpFactory)
    {
        foreach (var p in options.Providers)
        {
            if (string.IsNullOrWhiteSpace(p.Id)) continue;
            IIdentityProvider provider = p.Kind.ToLowerInvariant() switch
            {
                "github" => new GitHubIdentityProvider(p, httpFactory),
                _ => new OidcIdentityProvider(p, httpFactory)
            };
            _providers[p.Id] = provider;
        }
    }

    public IReadOnlyCollection<IIdentityProvider> All => _providers.Values;

    public IIdentityProvider? Find(string id) =>
        id != null && _providers.TryGetValue(id, out var p) ? p : null;
}
