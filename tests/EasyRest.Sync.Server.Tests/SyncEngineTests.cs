using System.Net.Http.Json;
using EasyRest.Services;
using EasyRest.Services.Sync;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>El engine del cliente contra el server real: se sincronizan carpetas de verdad en
/// disco, que es donde aparecen los problemas que un mock esconde.</summary>
public class SyncEngineTests : IClassFixture<SyncServerFactory>, IDisposable
{
    readonly SyncServerFactory _factory;
    readonly List<string> _tempDirs = new();

    public SyncEngineTests(SyncServerFactory factory) => _factory = factory;

    const string EnvJson = """
        {
          "id": "e1",
          "name": "Producción",
          "secretKeys": ["token"],
          "variables": [
            { "key": "baseUrl", "value": "https://api.example.com", "enabled": true },
            { "key": "token", "value": "tok-super-secreto", "enabled": true }
          ]
        }
        """;

    [Fact]
    public async Task Sube_lo_local_y_otro_dispositivo_lo_baja_igual()
    {
        var user = await _factory.LoginAsync("engine-uno");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/API/collection.json", """{"name":"API"}""");
        WriteFile(a, "collections/API/login.req.json", """{"method":"POST"}""");

        var subida = await a.Sync.SyncAsync();
        var bajada = await b.Sync.SyncAsync();

        Assert.True(subida.Ok);
        Assert.True(bajada.Ok);
        Assert.Equal("""{"name":"API"}""", ReadFile(b, "collections/API/collection.json"));
        Assert.Equal("""{"method":"POST"}""", ReadFile(b, "collections/API/login.req.json"));
    }

    [Fact]
    public async Task Una_edicion_viaja_al_otro_dispositivo()
    {
        var user = await _factory.LoginAsync("engine-editar");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/x.req.json", """{"v":1}""");
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();

        WriteFile(a, "collections/x.req.json", """{"v":2}""");
        await a.Sync.SyncAsync();
        var resultado = await b.Sync.SyncAsync();

        Assert.True(resultado.PulledRemote);
        Assert.Equal("""{"v":2}""", ReadFile(b, "collections/x.req.json"));
    }

    [Fact]
    public async Task Un_borrado_llega_al_otro_dispositivo()
    {
        var user = await _factory.LoginAsync("engine-borrar");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/temporal.req.json", "{}");
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();
        Assert.True(File.Exists(Path.Combine(b.Root, "collections/temporal.req.json")));

        File.Delete(Path.Combine(a.Root, "collections/temporal.req.json"));
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();

        Assert.False(File.Exists(Path.Combine(b.Root, "collections/temporal.req.json")));
    }

    [Fact]
    public async Task Con_ediciones_cruzadas_gana_lo_local_y_la_del_server_queda_al_lado()
    {
        var user = await _factory.LoginAsync("engine-conflicto");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/choque.req.json", """{"v":1}""");
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();

        // los dos editan el mismo archivo sin sincronizar en el medio
        WriteFile(a, "collections/choque.req.json", """{"lado":"a"}""");
        WriteFile(b, "collections/choque.req.json", """{"lado":"b"}""");
        await a.Sync.SyncAsync();
        var resultado = await b.Sync.SyncAsync();

        Assert.True(resultado.HasConflicts);
        Assert.Equal("""{"lado":"b"}""", ReadFile(b, "collections/choque.req.json"));
        var copia = Directory.GetFiles(Path.Combine(b.Root, "collections"), "*.remoto-*.json").Single();
        Assert.Equal("""{"lado":"a"}""", File.ReadAllText(copia));
    }

    [Fact]
    public async Task Tras_el_conflicto_la_version_local_termina_en_el_server()
    {
        var user = await _factory.LoginAsync("engine-conflicto-push");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/gana.req.json", """{"v":1}""");
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();
        WriteFile(a, "collections/gana.req.json", """{"lado":"a"}""");
        WriteFile(b, "collections/gana.req.json", """{"lado":"b"}""");
        await a.Sync.SyncAsync();

        await b.Sync.SyncAsync();          // resuelve dejando la local
        await b.Sync.SyncAsync();          // y la sube
        await a.Sync.SyncAsync();          // el otro dispositivo la recibe

        Assert.Equal("""{"lado":"b"}""", ReadFile(a, "collections/gana.req.json"));
    }

    [Fact]
    public async Task Con_KeepRemote_se_pisa_lo_local()
    {
        var user = await _factory.LoginAsync("engine-keepremote");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "collections/pisar.req.json", """{"v":1}""");
        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();
        WriteFile(a, "collections/pisar.req.json", """{"lado":"a"}""");
        WriteFile(b, "collections/pisar.req.json", """{"lado":"b"}""");
        await a.Sync.SyncAsync();

        await b.Sync.SyncAsync(ConflictResolution.KeepRemote);

        Assert.Equal("""{"lado":"a"}""", ReadFile(b, "collections/pisar.req.json"));
    }

    [Fact]
    public async Task El_ambiente_viaja_partido_y_se_rearma_en_el_otro_dispositivo()
    {
        var user = await _factory.LoginAsync("engine-env");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        var b = NewDevice(user, ws);
        WriteFile(a, "environments/prod.json", EnvJson);

        await a.Sync.SyncAsync();
        await b.Sync.SyncAsync();

        // en el server el valor no está dentro del documento…
        var documento = await a.Api.GetChangesAsync(ws, 0);
        var enServer = documento.Documents.Single(d => d.Path == "environments/prod.json");
        Assert.DoesNotContain("tok-super-secreto", enServer.Content);
        // …pero el otro dispositivo lo recibe completo por el endpoint de secretos
        Assert.Contains("tok-super-secreto", ReadFile(b, "environments/prod.json"));
    }

    [Fact]
    public async Task Sin_permiso_de_secretos_el_ambiente_baja_con_las_claves_vacias()
    {
        var duenio = await _factory.LoginAsync("engine-env-admin");
        var invitado = await _factory.LoginAsync("engine-env-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var a = NewDevice(duenio, ws);
        WriteFile(a, "environments/prod.json", EnvJson);
        await a.Sync.SyncAsync();
        await InviteAsync(duenio, invitado, ws, canReadSecrets: false);
        var b = NewDevice(invitado, ws);

        await b.Sync.SyncAsync();

        var bajado = ReadFile(b, "environments/prod.json");
        Assert.Contains("\"token\"", bajado);              // la clave sí
        Assert.DoesNotContain("tok-super-secreto", bajado); // el valor no
        Assert.Contains("https://api.example.com", bajado); // lo no secreto sí
    }

    [Fact]
    public async Task No_sube_nada_de_fuera_de_las_carpetas_sincronizadas()
    {
        var user = await _factory.LoginAsync("engine-appdata");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        // así se ve la raíz del workspace personal: los tokens locales viven acá
        WriteFile(a, "environments.json", """{"variables":[{"key":"token","value":"local"}]}""");
        WriteFile(a, "settings.json", """{"theme":"dark"}""");
        WriteFile(a, "collections/ok.req.json", "{}");

        await a.Sync.SyncAsync();

        var changes = await a.Api.GetChangesAsync(ws, 0);
        Assert.Equal(new[] { "collections/ok.req.json" }, changes.Documents.Select(d => d.Path).ToArray());
    }

    [Fact]
    public async Task Sincronizar_dos_veces_seguidas_no_hace_nada()
    {
        var user = await _factory.LoginAsync("engine-idempotente");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        WriteFile(a, "collections/idem.req.json", "{}");
        WriteFile(a, "environments/prod.json", EnvJson);
        await a.Sync.SyncAsync();

        var segunda = await a.Sync.SyncAsync();

        Assert.Equal("Todo al día.", segunda.Message);
        Assert.False(segunda.HasConflicts);
    }

    [Fact]
    public async Task El_estado_cuenta_los_cambios_pendientes_sin_red()
    {
        var user = await _factory.LoginAsync("engine-estado");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws);
        WriteFile(a, "collections/uno.req.json", "{}");
        WriteFile(a, "collections/dos.req.json", "{}");

        var antes = await a.Sync.StatusAsync();
        await a.Sync.SyncAsync();
        var despues = await a.Sync.StatusAsync();

        Assert.Equal(2, antes!.PendingChanges);
        Assert.Equal(0, despues!.PendingChanges);
    }

    [Fact]
    public async Task Sin_sesion_no_sincroniza_y_lo_dice()
    {
        var user = await _factory.LoginAsync("engine-sin-sesion");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var device = NewDevice(user, ws);
        device.Api.AccessToken = null;

        var resultado = await device.Sync.SyncAsync();

        Assert.False(resultado.Ok);
        Assert.Contains("sesión", resultado.Message);
    }

    [Theory]
    [InlineData("collections/API/collection.json", "collection")]
    [InlineData("collections/API/sub/folder.json", "folder")]
    [InlineData("collections/API/login.req.json", "request")]
    [InlineData("environments/prod.json", "environment")]
    public void El_tipo_sale_del_nombre_del_archivo(string path, string expected) =>
        Assert.Equal(expected, RemoteWorkspaceSync.KindOf(path));

    /// <summary>En la app los ambientes no cuelgan de la carpeta del workspace sino de AppData
    /// —con sync por git el workspace es el repo, y los tokens no van a un repo—, así que el
    /// motor recibe una raíz distinta para `environments/`. Si eso se rompe, los ambientes dejan
    /// de sincronizar sin que falle nada: la carpeta que el motor recorre simplemente no existe.
    /// Es exactamente lo que pasaba antes de que hubiera un archivo por ambiente.</summary>
    [Fact]
    public async Task Los_ambientes_sincronizan_aunque_vivan_fuera_del_workspace()
    {
        var user = await _factory.LoginAsync("engine-env-aparte");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var a = NewDevice(user, ws, ambientesAparte: true);
        var b = NewDevice(user, ws, ambientesAparte: true);
        File.WriteAllText(EnvFile(a, "e1.json"), EnvJson);

        Assert.True((await a.Sync.SyncAsync()).Ok);
        Assert.True((await b.Sync.SyncAsync()).Ok);

        // bajó a la raíz de ambientes del otro dispositivo, con su secreto rearmado…
        Assert.Contains("tok-super-secreto", File.ReadAllText(EnvFile(b, "e1.json")));
        // …y no a la carpeta del workspace, que es de las colecciones
        Assert.False(Directory.Exists(Path.Combine(b.Root, "environments")));
    }

    // ----- Andamiaje -----

    record Device(string Root, string EnvRoot, SyncApiClient Api, RemoteWorkspaceSync Sync);

    Device NewDevice(TestUser user, Guid workspaceId, bool ambientesAparte = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"easyrest-device-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        _tempDirs.Add(root);

        var envRoot = root;
        if (ambientesAparte)
        {
            envRoot = Path.Combine(Path.GetTempPath(), $"easyrest-envs-{Guid.NewGuid():N}");
            Directory.CreateDirectory(envRoot);
            _tempDirs.Add(envRoot);
        }

        var api = new SyncApiClient("http://localhost", _factory.CreateClient())
        {
            AccessToken = user.Tokens.AccessToken
        };
        var sync = new RemoteWorkspaceSync(root, api, workspaceId,
            Path.Combine(root, ".sync-state.json"), ambientesAparte ? envRoot : null);
        return new Device(root, envRoot, api, sync);
    }

    static string EnvFile(Device device, string name)
    {
        var full = Path.Combine(device.EnvRoot, "environments", name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return full;
    }

    async Task InviteAsync(TestUser admin, TestUser invitado, Guid workspaceId, bool canReadSecrets)
    {
        var api = new SyncApiClient("http://localhost", _factory.CreateClient())
        {
            AccessToken = admin.Tokens.AccessToken
        };
        var resp = await admin.Http.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/invitations",
            new { role = "Member", canReadSecrets });
        var invitation = await resp.Content.ReadAsync<InvitationResponse>();

        var accept = new SyncApiClient("http://localhost", _factory.CreateClient())
        {
            AccessToken = invitado.Tokens.AccessToken
        };
        await accept.AcceptInvitationAsync(invitation!.Token!);
        api.Dispose();
        accept.Dispose();
    }

    static void WriteFile(Device device, string relative, string content)
    {
        var full = Path.Combine(device.Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    static string ReadFile(Device device, string relative) =>
        File.ReadAllText(Path.Combine(device.Root, relative));

    public void Dispose()
    {
        foreach (var dir in _tempDirs.Where(Directory.Exists))
            Directory.Delete(dir, recursive: true);
    }
}
