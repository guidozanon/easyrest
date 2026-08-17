using EasyRest.Sync.Server.Auth;
using Microsoft.Extensions.Options;

namespace EasyRest.Sync.Server.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        // Lo que la app consulta antes de mostrar el login: qué providers tiene este server.
        // Así "pluggable" no significa que el cliente conozca a Google ni a GitHub.
        app.MapGet("/api/v1/meta", (IdentityProviderRegistry providers, IOptions<AuthOptions> options) =>
            Results.Ok(new MetaResponse(
                "easyrest-sync",
                typeof(MetaResponse).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
                new[] { 1 },
                new[] { "documents", "secrets", "invitations", "service-tokens" },
                new MetaAuth(
                    providers.All.Select(p => new MetaProvider(p.Id, p.DisplayName, p.Kind)).ToArray(),
                    options.Value.AllowedRedirectSchemes.ToArray()))));

        // Paso 1: la app abre esto en el navegador del sistema (nunca en un webview embebido).
        app.MapGet("/api/v1/auth/start", async (
            string provider, string redirect_uri, string code_challenge, string? state,
            AuthService auth, CancellationToken ct) =>
        {
            try
            {
                var url = await auth.StartAsync(provider, redirect_uri, code_challenge, state ?? "", ct);
                return Results.Redirect(url);
            }
            catch (AuthException ex)
            {
                return Api.Invalid(ex.Message);
            }
        });

        // Paso 2: vuelve el IdP acá y nosotros devolvemos a la app su authorization code.
        app.MapGet("/api/v1/auth/callback", async (
            string? code, string? state, string? error, string? error_description,
            AuthService auth, CancellationToken ct) =>
        {
            // El `error` solo es siempre genérico ("invalid_request"): lo que dice qué pasó es la
            // descripción —ahí viajan los AADSTS de Entra y los mensajes de Google—, así que se
            // muestra. Es la diferencia entre saber qué arreglar y probar a ciegas.
            if (!string.IsNullOrEmpty(error))
                return Api.Invalid(string.IsNullOrWhiteSpace(error_description)
                    ? $"El IdP devolvió un error: {error}"
                    : $"El IdP devolvió un error: {error} — {error_description}");
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return Api.Invalid("Faltan code o state.");

            try
            {
                return Results.Redirect(await auth.CallbackAsync(state, code, ct));
            }
            catch (AuthException ex)
            {
                return Api.Invalid(ex.Message);
            }
            catch (IdentityProviderException ex)
            {
                return Api.Invalid(ex.Message);
            }
        });

        // Paso 3: la app canjea el code con su code_verifier. También refresca.
        app.MapPost("/api/v1/auth/token", async (
            TokenRequest request, AuthService auth, IOptions<AuthOptions> options, CancellationToken ct) =>
        {
            try
            {
                var grant = request.GrantType ?? "authorization_code";
                var (tokens, user) = grant switch
                {
                    "refresh_token" => await auth.RefreshAsync(
                        request.RefreshToken ?? throw new AuthException("Falta refresh_token."), ct),
                    "authorization_code" => await auth.ExchangeCodeAsync(
                        request.Code ?? throw new AuthException("Falta code."),
                        request.CodeVerifier ?? throw new AuthException("Falta code_verifier."), ct),
                    _ => throw new AuthException($"grant_type '{grant}' no soportado.")
                };

                return Results.Ok(new TokenResponse(
                    tokens.AccessToken,
                    tokens.RefreshToken,
                    "Bearer",
                    options.Value.AccessTokenMinutes * 60,
                    new UserResponse(user.Id, user.Email, user.DisplayName, user.Provider, user.IsServerAdmin)));
            }
            catch (AuthException ex)
            {
                return Api.Invalid(ex.Message);
            }
        });

        app.MapPost("/api/v1/auth/logout", async (HttpContext http, AuthService auth, CancellationToken ct) =>
        {
            var header = http.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                await auth.LogoutAsync(header["Bearer ".Length..].Trim(), ct);
            return Results.NoContent();
        });

        app.MapGet("/api/v1/me", (Caller? caller) =>
        {
            if (caller?.User is { } user)
                return Results.Ok(new UserResponse(user.Id, user.Email, user.DisplayName, user.Provider, user.IsServerAdmin));

            return caller?.Service != null
                ? Results.Ok(new UserResponse(Guid.Empty, "", $"service:{caller.Service.Name}", "service-token"))
                : Api.Unauthorized();
        });
    }
}
