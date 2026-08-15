using System.Text.Json.Serialization;
using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Crypto;
using EasyRest.Sync.Server.Data;
using EasyRest.Sync.Server.Endpoints;
using EasyRest.Sync.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Toda la config se puede pasar por env vars: Auth__Providers__0__ClientId, etc.
builder.Configuration.AddEnvironmentVariables();

// No hace nada si no está corriendo bajo systemd; si lo está, los logs salen con el formato
// que journald entiende (niveles incluidos) en vez de con timestamps duplicados.
builder.Host.UseSystemd();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// La master key cifra las claves de datos de cada workspace. Sin ella el server no arranca:
// guardar secretos en claro "hasta que lo configuremos bien" no es una opción.
var masterKeyRaw = builder.Configuration["EASYREST_MASTER_KEY"]
                   ?? builder.Configuration["Crypto:MasterKey"];
if (!SecretBox.TryParseMasterKey(masterKeyRaw, out var masterKey, out var masterKeyError))
    throw new InvalidOperationException(masterKeyError);
builder.Services.AddSingleton(new SecretBox(masterKey));

var provider = (builder.Configuration["Database:Provider"] ?? "sqlite").ToLowerInvariant();
var connectionString = builder.Configuration.GetConnectionString("Default")
                       ?? builder.Configuration["EASYREST_DB"]
                       ?? DatabaseSetup.DefaultConnectionString;
builder.Services.AddDbContext<SyncDbContext>(options =>
    DatabaseSetup.Configure(options, provider, connectionString));

builder.Services.AddHttpClient("idp");
builder.Services.AddSingleton<IdentityProviderRegistry>(sp => new IdentityProviderRegistry(
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AuthOptions>>().Value,
    sp.GetRequiredService<IHttpClientFactory>()));
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<AdminService>();

// La consola se puede apagar entera: es una superficie nueva sobre un servicio que guarda
// secretos, y quien exponga sólo la API no tiene por qué cargar con ella.
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
var adminEnabled = builder.Configuration.GetValue("Admin:Enabled", true);
if (adminEnabled) builder.Services.AddRazorPages();

var app = builder.Build();

// Migraciones al arrancar: es lo que hace que reinstalar sea actualizar. Con EnsureCreated,
// una instalación existente no podría recibir nunca un cambio de esquema.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();
    db.Database.Migrate();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();
app.MapWorkspaces();
app.MapInvitationAccept();
app.MapDocuments();
app.MapAdmin();
app.MapOwnershipTransfer();
if (adminEnabled) app.MapRazorPages();

app.Run();

/// <summary>Punto de entrada visible para WebApplicationFactory en los tests.</summary>
public partial class Program;
