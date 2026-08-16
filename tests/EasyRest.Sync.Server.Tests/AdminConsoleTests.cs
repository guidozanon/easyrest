using System.Net;
using System.Net.Http.Json;
using EasyRest.Sync.Server.Admin;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>La consola de administración. La sesión es el mismo access token opaco de la API,
/// metido en una cookie, así que los tests pueden loguearse por la API y usar ese token — no hace
/// falta simular el ida y vuelta del navegador para verificar los permisos.</summary>
public class AdminConsoleTests : IDisposable
{
    readonly SyncServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    HttpClient ConsoleClient(TestUser? user)
    {
        var http = _factory.CreateRawClient();
        if (user != null)
            http.DefaultRequestHeaders.Add("Cookie", $"{AdminSession.CookieName}={user.Tokens.AccessToken}");
        return http;
    }

    [Fact]
    public async Task Sin_sesion_manda_al_login()
    {
        var http = ConsoleClient(null);

        var resp = await http.GetAsync("/Admin");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("/Admin/Login", resp.Headers.Location!.ToString());
    }

    [Fact]
    public async Task El_login_se_ve_sin_estar_logueado()
    {
        var http = ConsoleClient(null);

        var resp = await http.GetAsync("/Admin/Login");
        var html = await resp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Fake IdP", html);   // el provider configurado, salido de /meta
    }

    [Fact]
    public async Task Un_usuario_comun_no_entra_a_la_consola()
    {
        await _factory.LoginAsync("consola-primero");           // se queda con el admin
        var comun = await _factory.LoginAsync("consola-comun");
        var http = ConsoleClient(comun);

        foreach (var url in new[] { "/Admin", "/Admin/Users", "/Admin/Workspaces" })
            Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task El_admin_ve_el_resumen_con_el_diagnostico_de_auth()
    {
        var admin = await _factory.LoginAsync("consola-admin");
        var http = ConsoleClient(admin);

        var html = await (await http.GetAsync("/Admin")).Content.ReadAsStringAsync();

        Assert.Contains("consola-admin@test.local", html);
        Assert.Contains("/api/v1/auth/callback", html);   // el redirect a registrar en el IdP
        Assert.Contains("Fake IdP", html);
    }

    [Fact]
    public async Task El_resumen_avisa_si_la_url_publica_no_coincide()
    {
        var admin = await _factory.LoginAsync("consola-url");
        var http = ConsoleClient(admin);

        // el test entra por localhost y Auth:PublicUrl dice http://localhost: coincide.
        // Se fuerza otro Host para simular el error clásico de instalación.
        var request = new HttpRequestMessage(HttpMethod.Get, "/Admin");
        request.Headers.Host = "sync.otraempresa.com";
        var html = await (await http.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("Auth:PublicUrl", html);
    }

    [Fact]
    public async Task El_listado_de_usuarios_muestra_a_todos()
    {
        var admin = await _factory.LoginAsync("consola-lista-admin");
        await _factory.LoginAsync("consola-lista-otro");
        var http = ConsoleClient(admin);

        var html = await (await http.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();

        Assert.Contains("consola-lista-admin@test.local", html);
        Assert.Contains("consola-lista-otro@test.local", html);
    }

    [Fact]
    public async Task Los_workspaces_se_ven_con_su_dueño_y_sus_miembros()
    {
        var admin = await _factory.LoginAsync("consola-ws-admin");
        var duenio = await _factory.LoginAsync("consola-ws-duenio");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo de pruebas");
        var http = ConsoleClient(admin);

        var lista = await (await http.GetAsync("/Admin/Workspaces")).Content.ReadAsStringAsync();
        var detalle = await (await http.GetAsync($"/Admin/Workspaces?id={ws}")).Content.ReadAsStringAsync();

        Assert.Contains("Equipo de pruebas", lista);
        Assert.Contains("consola-ws-duenio@test.local", lista);
        Assert.Contains("consola-ws-duenio@test.local", detalle);
        Assert.Contains("Owner", detalle);
    }

    [Fact]
    public async Task La_consola_nunca_muestra_valores_de_secretos()
    {
        var admin = await _factory.LoginAsync("consola-secretos");
        var ws = await _factory.CreateWorkspaceAsync(admin, "Con secretos");
        await admin.Http.PutDocumentAsync(ws, "environments/prod.json", "environment",
            """{"id":"e","name":"Prod","secretKeys":["token"],"variables":[{"key":"token","value":""}]}""",
            secrets: new Dictionary<string, string> { ["token"] = "valor-ultra-secreto" });
        var http = ConsoleClient(admin);

        foreach (var url in new[] { "/Admin", "/Admin/Users", "/Admin/Workspaces", $"/Admin/Workspaces?id={ws}" })
            Assert.DoesNotContain("valor-ultra-secreto",
                await (await http.GetAsync(url)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Los_formularios_llevan_token_antiforgery()
    {
        var admin = await _factory.LoginAsync("consola-csrf");
        var http = ConsoleClient(admin);

        var html = await (await http.GetAsync("/Admin/Users")).Content.ReadAsStringAsync();

        Assert.Contains("__RequestVerificationToken", html);
    }

    [Fact]
    public async Task Un_post_sin_token_antiforgery_se_rechaza()
    {
        var admin = await _factory.LoginAsync("consola-csrf-post");
        var otro = await _factory.LoginAsync("consola-csrf-victima");
        var otroId = (await (await otro.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        var http = ConsoleClient(admin);

        var resp = await http.PostAsync("/Admin/Users?handler=ToggleDisabled",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["userId"] = otroId.ToString(),
                ["value"] = "true"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        // y la víctima sigue entrando
        Assert.True((await otro.Http.GetAsync("/api/v1/me")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task La_sesion_de_la_consola_muere_al_desactivar_al_usuario()
    {
        var admin = await _factory.LoginAsync("consola-baja-admin");
        var segundo = await _factory.LoginAsync("consola-baja-segundo");
        var segundoId = (await (await segundo.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{segundoId}", new { isServerAdmin = true });
        Assert.Equal(HttpStatusCode.OK, (await ConsoleClient(segundo).GetAsync("/Admin")).StatusCode);

        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{segundoId}", new { disabled = true });

        var resp = await ConsoleClient(segundo).GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }
}

/// <summary>Con Admin:Enabled en false no tiene que quedar ninguna página servida.</summary>
public class AdminConsoleDisabledTests : IDisposable
{
    readonly DisabledConsoleFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    class DisabledConsoleFactory : SyncServerFactory
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Admin:Enabled"] = "false" }));
            return base.CreateHost(builder);
        }
    }

    [Fact]
    public async Task La_consola_apagada_no_responde()
    {
        var http = _factory.CreateRawClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/Admin")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/Admin/Login")).StatusCode);
    }

    [Fact]
    public async Task La_api_sigue_funcionando_con_la_consola_apagada()
    {
        var http = _factory.CreateRawClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/v1/meta")).StatusCode);
    }
}
