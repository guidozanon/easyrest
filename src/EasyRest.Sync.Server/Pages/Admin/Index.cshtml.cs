using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EasyRest.Sync.Server.Pages.Admin;

public class IndexModel(
    SyncDbContext db,
    IdentityProviderRegistry providers,
    IOptions<AuthOptions> options,
    IConfiguration configuration) : AdminPageModel
{
    public record ProviderStatus(string Id, string DisplayName, string Kind, string? Problem);

    public int Users { get; private set; }
    public int Workspaces { get; private set; }
    public int Documents { get; private set; }
    public int ServiceTokens { get; private set; }

    public List<ProviderStatus> Providers { get; private set; } = new();
    public string CallbackUrl { get; private set; } = "";
    public string PublicUrl { get; private set; } = "";
    public string? PublicUrlWarning { get; private set; }
    public string DatabaseProvider { get; private set; } = "";
    public string LastMigration { get; private set; } = "";
    public List<string> AllowedDomains { get; private set; } = new();

    public async Task OnGetAsync(CancellationToken ct)
    {
        Users = await db.Users.CountAsync(ct);
        Workspaces = await db.Workspaces.CountAsync(ct);
        Documents = await db.Documents.CountAsync(d => !d.Deleted, ct);
        ServiceTokens = await db.ServiceTokens.CountAsync(t => !t.Revoked, ct);

        // el diagnóstico pega contra cada IdP, así que va en paralelo y con timeout adentro
        Providers = (await Task.WhenAll(providers.All.Select(async p =>
                new ProviderStatus(p.Id, p.DisplayName, p.Kind, await Safe(p, ct)))))
            .OrderBy(p => p.Id).ToList();

        PublicUrl = options.Value.PublicUrl;
        CallbackUrl = PublicUrl.TrimEnd('/') + "/api/v1/auth/callback";
        AllowedDomains = options.Value.AllowedEmailDomains;
        DatabaseProvider = configuration["Database:Provider"] ?? "sqlite";
        LastMigration = (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "(ninguna)";

        PublicUrlWarning = CheckPublicUrl();
    }

    /// <summary>El error más común al instalar: la URL pública no coincide con el host real, o
    /// es http, y entonces el IdP rechaza el redirect y el login falla sin explicación.</summary>
    string? CheckPublicUrl()
    {
        if (!Uri.TryCreate(PublicUrl, UriKind.Absolute, out var configured))
            return "Auth:PublicUrl no es una URL válida: el login no va a funcionar.";

        var actual = $"{Request.Scheme}://{Request.Host}";
        if (!string.Equals(actual, PublicUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return $"Estás entrando por {actual} pero Auth:PublicUrl dice {PublicUrl}. " +
                   "El IdP va a recibir el redirect configurado, no por el que entraste.";

        if (configured.Scheme == "https" || configured.IsLoopback) return null;
        return "Auth:PublicUrl no es https: la mayoría de los IdP rechazan redirects http que no " +
               "sean localhost. Poné el server detrás de un proxy con TLS.";
    }

    static async Task<string?> Safe(IIdentityProvider provider, CancellationToken ct)
    {
        try
        {
            return await provider.DiagnoseAsync(ct);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
