using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyRest.Sync.Server;
using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EasyRest.Sync.Server.Tests;

/// <summary>IdP falso: devuelve como identidad lo que venga en el code, así cada test se loguea
/// como el usuario que quiera sin levantar un Keycloak.</summary>
public class FakeIdentityProvider : IIdentityProvider
{
    public string Id => "fake";
    public string DisplayName => "Fake IdP";
    public string Kind => "oidc";

    public Task<string> BuildAuthorizationUrlAsync(string state, string callbackUrl, CancellationToken ct) =>
        Task.FromResult($"https://idp.test/authorize?state={Uri.EscapeDataString(state)}");

    public Task<ExternalIdentity> ExchangeAsync(string code, string callbackUrl, CancellationToken ct) =>
        Task.FromResult(new ExternalIdentity(code, $"{code}@test.local", $"Usuario {code}"));
}

public class SyncServerFactory : WebApplicationFactory<Program>
{
    readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"easyrest-sync-test-{Guid.NewGuid():N}.db");

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EASYREST_MASTER_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Database:Provider"] = "sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["Auth:PublicUrl"] = "http://localhost",
            ["Auth:AllowedRedirectSchemes:0"] = "easyrest"
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IdentityProviderRegistry>();
            services.AddSingleton(new IdentityProviderRegistry(new IIdentityProvider[] { new FakeIdentityProvider() }));
        });

        return base.CreateHost(builder);
    }

    /// <summary>Cliente que no sigue redirects: el flujo de login se verifica leyendo los
    /// Location, que es justo lo que hace la app.</summary>
    public HttpClient CreateRawClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Login completo con PKCE contra el IdP falso. Devuelve un cliente ya autenticado.</summary>
    public async Task<TestUser> LoginAsync(string subject)
    {
        var http = CreateRawClient();
        var verifier = Tokens.Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Tokens.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var start = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                        "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb" +
                                        $"&code_challenge={challenge}&state=xyz");
        var state = QueryValue(start.Headers.Location!, "state");

        var callback = await http.GetAsync($"/api/v1/auth/callback?code={subject}&state={Uri.EscapeDataString(state)}");
        var code = QueryValue(callback.Headers.Location!, "code");

        var tokenResp = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            grantType = "authorization_code",
            code,
            codeVerifier = verifier
        });
        tokenResp.EnsureSuccessStatusCode();
        var tokens = await tokenResp.Content.ReadAsync<TokenResponse>()
                     ?? throw new InvalidOperationException("El server no devolvió tokens.");

        var authed = CreateRawClient();
        authed.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return new TestUser(authed, tokens);
    }

    public async Task<Guid> CreateWorkspaceAsync(TestUser user, string name)
    {
        var resp = await user.Http.PostAsJsonAsync("/api/v1/workspaces", new { name });
        resp.EnsureSuccessStatusCode();
        var ws = await resp.Content.ReadAsync<WorkspaceResponse>();
        return ws!.Id;
    }

    /// <summary>Acceso directo a la base, para verificar lo que no se ve por la API (por ejemplo
    /// que los secretos estén realmente cifrados en reposo).</summary>
    public async Task WithDbAsync(Func<SyncDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<SyncDbContext>());
    }

    public static string QueryValue(Uri uri, string key)
    {
        var query = uri.IsAbsoluteUri ? uri.Query : uri.OriginalString[uri.OriginalString.IndexOf('?')..];
        foreach (var pair in query.TrimStart('?').Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == key)
                return parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        throw new InvalidOperationException($"La URL {uri} no trae '{key}'.");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_dbPath)) File.Delete(_dbPath);
    }
}

public record TestUser(HttpClient Http, TokenResponse Tokens);

public static class HttpExtensions
{
    /// <summary>Las mismas opciones que usa el server: enums como texto. Sin esto los tests
    /// leerían "Admin" como un entero y fallarían por un detalle del cliente, no del server.</summary>
    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public static Task<T?> ReadAsync<T>(this HttpContent content) => content.ReadFromJsonAsync<T>(Json);

    /// <summary>PUT de documento con la revisión esperada en If-Match.</summary>
    public static async Task<HttpResponseMessage> PutDocumentAsync(this HttpClient http, Guid workspaceId,
        string path, string kind, string content, string? ifMatch = null,
        Dictionary<string, string>? secrets = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/workspaces/{workspaceId}/documents")
        {
            Content = JsonContent.Create(new { path, kind, content, secrets })
        };
        if (ifMatch != null) request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await http.SendAsync(request);
    }

    public static async Task<DocumentResponse> ReadDocumentAsync(this HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsync<DocumentResponse>()
               ?? throw new InvalidOperationException("Respuesta vacía.");
    }

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}
