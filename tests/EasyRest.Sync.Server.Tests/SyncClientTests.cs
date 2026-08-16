using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EasyRest.Services.Sync;
using EasyRest.Sync.Server.Crypto;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>El cliente que usa la app, contra el server de verdad en memoria.
///
/// Los SyncEngineTests ya cubren el motor de documentos; acá va lo otro: el login con PKCE, la
/// sesión que se refresca sola y la administración de miembros e invitaciones, que es lo que la
/// UI necesita para existir.</summary>
public class SyncClientTests : IClassFixture<SyncServerFactory>, IDisposable
{
    readonly SyncServerFactory _factory;
    readonly List<string> _tempFiles = new();

    public SyncClientTests(SyncServerFactory factory) => _factory = factory;

    SyncApiClient ApiFor(TestUser user) =>
        new("http://localhost", _factory.CreateClient()) { AccessToken = user.Tokens.AccessToken };

    SyncAccountStore TempStore()
    {
        var path = Path.Combine(Path.GetTempPath(), $"easyrest-sessions-{Guid.NewGuid():N}.json");
        _tempFiles.Add(path);
        return new SyncAccountStore(path);
    }

    // ----- Login -----

    [Fact]
    public async Task El_pkce_del_cliente_lo_acepta_el_server()
    {
        // Si el cliente y el server no calculan el challenge igual, el login falla en producción y
        // en ningún test: por eso se genera con SyncPkce, que es lo que usa la app.
        var pkce = SyncPkce.Create();
        Assert.Equal(pkce.Challenge, Tokens.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(pkce.Verifier))));

        var (api, session) = await LoginConPkceAsync("ana", pkce);

        Assert.Equal("ana@test.local", session.User.Email);
        Assert.False(string.IsNullOrEmpty(session.AccessToken));
        Assert.False(string.IsNullOrEmpty(session.RefreshToken));

        // el token que quedó puesto sirve de verdad
        var yo = await api.GetMeAsync();
        Assert.Equal(session.User.Id, yo.Id);
        api.Dispose();
    }

    [Fact]
    public async Task El_login_url_lleva_el_challenge_y_el_state()
    {
        using var api = new SyncApiClient("http://localhost", _factory.CreateClient());
        var pkce = SyncPkce.Create();

        var url = api.BuildLoginUrl("fake", "http://127.0.0.1:5599/cb", pkce.Challenge, pkce.State);

        Assert.Contains($"code_challenge={Uri.EscapeDataString(pkce.Challenge)}", url);
        Assert.Contains($"state={Uri.EscapeDataString(pkce.State)}", url);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb", url);
    }

    // ----- Sesión -----

    [Fact]
    public async Task La_sesion_sobrevive_al_reinicio()
    {
        var (api, session) = await LoginConPkceAsync("beto", SyncPkce.Create());
        api.Dispose();

        var store = TempStore();
        SyncConnection.Establish("http://localhost", session, store, _factory.CreateClient()).Dispose();

        // "otro arranque de la app": nada en memoria, todo desde disco
        using var restaurada = SyncConnection.Restore("http://localhost", store, _factory.CreateClient());

        Assert.NotNull(restaurada);
        Assert.Equal("beto@test.local", restaurada!.Account.Email);
        var yo = await restaurada.Api.GetMeAsync();
        Assert.Equal(session.User.Id, yo.Id);
    }

    [Fact]
    public async Task La_url_del_server_se_compara_sin_la_barra_final()
    {
        var (api, session) = await LoginConPkceAsync("caro", SyncPkce.Create());
        api.Dispose();

        var store = TempStore();
        SyncConnection.Establish("http://localhost/", session, store, _factory.CreateClient()).Dispose();

        Assert.NotNull(SyncConnection.Restore("http://localhost", store, _factory.CreateClient()));
        Assert.NotNull(SyncConnection.Restore("http://LOCALHOST/", store, _factory.CreateClient()));
    }

    [Fact]
    public async Task Un_token_vencido_se_refresca_solo_y_la_llamada_sale_igual()
    {
        var (api, session) = await LoginConPkceAsync("dani", SyncPkce.Create());
        api.Dispose();

        var store = TempStore();
        using var conexión = SyncConnection.Establish("http://localhost", session, store,
            _factory.CreateClient());

        // vencido de verdad: si sólo se rompiera Api.AccessToken, el refresher cortaría antes y
        // devolvería el token guardado sin ir al server, que es otro camino
        conexión.Account.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        conexión.Api.AccessToken = "un-token-que-no-existe";
        var refreshViejo = conexión.Account.RefreshToken;

        var yo = await conexión.Api.GetMeAsync();

        Assert.Equal(session.User.Id, yo.Id);
        // el refresh rota: el anterior queda quemado
        Assert.NotEqual(refreshViejo, conexión.Account.RefreshToken);
        Assert.Equal(conexión.Account.AccessToken, conexión.Api.AccessToken);
        Assert.True(conexión.Account.ExpiresAtUtc > DateTime.UtcNow);
        // y quedó guardado, así que el próximo arranque no vuelve a pedir login
        Assert.Equal(conexión.Account.AccessToken, store.Find("http://localhost")!.AccessToken);
    }

    [Fact]
    public async Task Un_access_token_desfasado_se_cura_sin_ir_al_server()
    {
        var (api, session) = await LoginConPkceAsync("emi", SyncPkce.Create());
        api.Dispose();

        var store = TempStore();
        using var conexión = SyncConnection.Establish("http://localhost", session, store,
            _factory.CreateClient());

        // el token guardado sigue vigente; sólo el del cliente quedó viejo
        var refreshViejo = conexión.Account.RefreshToken;
        conexión.Api.AccessToken = "quedó-viejo";

        await conexión.Api.GetMeAsync();

        Assert.Equal(conexión.Account.AccessToken, conexión.Api.AccessToken);
        // no se gastó un refresh al pedo
        Assert.Equal(refreshViejo, conexión.Account.RefreshToken);
    }

    [Fact]
    public async Task Si_el_refresh_ya_no_sirve_se_olvida_la_sesion()
    {
        var (api, session) = await LoginConPkceAsync("edu", SyncPkce.Create());
        api.Dispose();

        var store = TempStore();
        using var conexión = SyncConnection.Establish("http://localhost", session, store,
            _factory.CreateClient());

        conexión.Account.ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-5);
        conexión.Api.AccessToken = "no-sirve";
        conexión.Account.RefreshToken = "tampoco-sirve";

        var ex = await Assert.ThrowsAsync<SyncApiException>(() => conexión.Api.GetMeAsync());

        Assert.Equal(HttpStatusCode.Unauthorized, ex.Status);
        // no queda una cuenta muerta en disco fingiendo que hay sesión
        Assert.Null(store.Find("http://localhost"));
    }

    // ----- Miembros -----

    [Fact]
    public async Task Listar_cambiar_rol_y_sacar_a_alguien()
    {
        var dueño = await _factory.LoginAsync("flor");
        var otro = await _factory.LoginAsync("gabi");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Equipo");
        await SumarMiembroAsync(dueño, otro, workspaceId);

        using var api = ApiFor(dueño);

        var miembros = await api.GetMembersAsync(workspaceId);
        Assert.Equal(2, miembros.Length);
        Assert.Contains(miembros, m => m.Email == "flor@test.local" && m.Role == "Owner");
        var invitado = Assert.Single(miembros, m => m.Email == "gabi@test.local");
        Assert.Equal("Member", invitado.Role);
        Assert.False(invitado.CanReadSecrets);

        var ascendido = await api.UpdateMemberAsync(workspaceId, invitado.UserId, "Admin",
            canReadSecrets: true);
        Assert.Equal("Admin", ascendido.Role);
        Assert.True(ascendido.CanReadSecrets);

        await api.RemoveMemberAsync(workspaceId, invitado.UserId);
        Assert.Single(await api.GetMembersAsync(workspaceId));
    }

    [Fact]
    public async Task Al_owner_no_se_lo_puede_sacar()
    {
        var dueño = await _factory.LoginAsync("hugo");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Solo");
        using var api = ApiFor(dueño);

        var yo = Assert.Single(await api.GetMembersAsync(workspaceId));

        var ex = await Assert.ThrowsAsync<SyncApiException>(
            () => api.RemoveMemberAsync(workspaceId, yo.UserId));
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
    }

    [Fact]
    public async Task Transferir_la_propiedad_deja_al_anterior_como_admin()
    {
        var dueño = await _factory.LoginAsync("ivan");
        var sucesor = await _factory.LoginAsync("juli");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Traspaso");
        await SumarMiembroAsync(dueño, sucesor, workspaceId);

        using var api = ApiFor(dueño);
        var quiénEs = (await api.GetMembersAsync(workspaceId)).First(m => m.Email == "juli@test.local");

        var nuevo = await api.TransferOwnershipAsync(workspaceId, quiénEs.UserId);

        Assert.Equal("Owner", nuevo.Role);
        var después = await api.GetMembersAsync(workspaceId);
        Assert.Equal("Admin", Assert.Single(después, m => m.Email == "ivan@test.local").Role);
    }

    // ----- Invitaciones -----

    [Fact]
    public async Task Crear_listar_y_revocar_una_invitacion()
    {
        var dueño = await _factory.LoginAsync("kari");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Invitaciones");
        using var api = ApiFor(dueño);

        var invitación = await api.CreateInvitationAsync(workspaceId, "nuevo@test.local", "Member",
            canReadSecrets: true, expiresInHours: 48);

        // el token en claro llega una única vez, justo acá
        Assert.False(string.IsNullOrEmpty(invitación.Token));
        Assert.Equal("nuevo@test.local", invitación.Email);
        Assert.True(invitación.CanReadSecrets);

        var listadas = await api.GetInvitationsAsync(workspaceId);
        var guardada = Assert.Single(listadas);
        Assert.Null(guardada.Token);
        Assert.False(guardada.Accepted);

        await api.RevokeInvitationAsync(workspaceId, guardada.Id);
        Assert.Empty(await api.GetInvitationsAsync(workspaceId));
    }

    [Fact]
    public async Task Una_invitacion_revocada_ya_no_se_puede_aceptar()
    {
        var dueño = await _factory.LoginAsync("lucho");
        var invitado = await _factory.LoginAsync("mara");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Revocada");

        using var api = ApiFor(dueño);
        var invitación = await api.CreateInvitationAsync(workspaceId, null, "Member");
        await api.RevokeInvitationAsync(workspaceId, invitación.Id);

        using var deInvitado = ApiFor(invitado);
        await Assert.ThrowsAsync<SyncApiException>(
            () => deInvitado.AcceptInvitationAsync(invitación.Token!));
    }

    [Fact]
    public async Task Un_miembro_comun_no_puede_invitar()
    {
        var dueño = await _factory.LoginAsync("nico");
        var común = await _factory.LoginAsync("olga");
        var workspaceId = await _factory.CreateWorkspaceAsync(dueño, "Permisos");
        await SumarMiembroAsync(dueño, común, workspaceId);

        using var api = ApiFor(común);

        var ex = await Assert.ThrowsAsync<SyncApiException>(
            () => api.CreateInvitationAsync(workspaceId, null, "Member"));
        Assert.Equal(HttpStatusCode.Forbidden, ex.Status);
    }

    // ----- Andamiaje -----

    /// <summary>El login completo tal como lo hace la app: PKCE propio, /auth/start, el callback
    /// del IdP y el canje del código.</summary>
    async Task<(SyncApiClient Api, SyncSession Session)> LoginConPkceAsync(string sujeto, SyncPkce pkce)
    {
        var http = _factory.CreateRawClient();
        var api = new SyncApiClient("http://localhost", http);

        var start = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                        "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb" +
                                        $"&code_challenge={Uri.EscapeDataString(pkce.Challenge)}" +
                                        $"&state={Uri.EscapeDataString(pkce.State)}");
        var estadoDelIdp = SyncServerFactory.QueryValue(start.Headers.Location!, "state");

        var callback = await http.GetAsync(
            $"/api/v1/auth/callback?code={sujeto}&state={Uri.EscapeDataString(estadoDelIdp)}");
        var code = SyncServerFactory.QueryValue(callback.Headers.Location!, "code");

        return (api, await api.ExchangeCodeAsync(code, pkce.Verifier));
    }

    async Task SumarMiembroAsync(TestUser admin, TestUser invitado, Guid workspaceId)
    {
        using var deAdmin = ApiFor(admin);
        var invitación = await deAdmin.CreateInvitationAsync(workspaceId, null, "Member");
        using var deInvitado = ApiFor(invitado);
        await deInvitado.AcceptInvitationAsync(invitación.Token!);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
            try { File.Delete(file); } catch (IOException) { }
    }
}
