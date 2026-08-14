namespace EasyRest.Sync.Server.Auth;

/// <summary>Configuración de login del server. Cada organización configura sus propios
/// providers; la app no sabe nada de Google ni de GitHub: pregunta por /api/v1/meta qué hay
/// disponible y dibuja los botones con eso.</summary>
public class AuthOptions
{
    /// <summary>URL pública del server, la que ve el IdP. Sin esto no se puede armar el
    /// redirect_uri del callback.</summary>
    public string PublicUrl { get; set; } = "";

    /// <summary>Esquemas propios permitidos como redirect de la app (además de loopback), para
    /// el login en móvil: por ejemplo "easyrest".</summary>
    public List<string> AllowedRedirectSchemes { get; set; } = new();

    public List<ProviderOptions> Providers { get; set; } = new();

    /// <summary>Duración del access token. El refresh vive mucho más y es revocable.</summary>
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Si está prendido, la primera persona que entra se queda como admin del server.
    /// Sólo afecta a quién puede crear workspaces cuando RestrictWorkspaceCreation está activo.</summary>
    public bool AllowOpenRegistration { get; set; } = true;

    /// <summary>Dominios de mail permitidos (vacío = cualquiera). Es el control más simple para
    /// un server de empresa detrás de un IdP público como Google.</summary>
    public List<string> AllowedEmailDomains { get; set; } = new();
}

public class ProviderOptions
{
    /// <summary>Identificador corto que usa la app: "google", "entra", "github"…</summary>
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>"oidc" para cualquier IdP con discovery (Google, Entra, Okta, Keycloak,
    /// Authentik…), "github" para GitHub, que es OAuth2 plano sin OIDC.</summary>
    public string Kind { get; set; } = "oidc";

    /// <summary>Sólo para oidc: la autoridad con /.well-known/openid-configuration.</summary>
    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Scopes { get; set; } = "";
}
