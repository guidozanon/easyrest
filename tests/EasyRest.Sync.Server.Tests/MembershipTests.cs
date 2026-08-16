using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EasyRest.Sync.Server.Data;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

public class MembershipTests : IClassFixture<SyncServerFactory>
{
    readonly SyncServerFactory _factory;

    public MembershipTests(SyncServerFactory factory) => _factory = factory;

    [Fact]
    public async Task La_invitacion_da_acceso_con_el_rol_indicado()
    {
        var duenio = await _factory.LoginAsync("inv-duenio");
        var invitado = await _factory.LoginAsync("inv-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");

        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Admin", canReadSecrets = true })).Content.ReadAsync<InvitationResponse>();
        var accepted = await invitado.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invitation!.Token });

        var workspace = await accepted.Content.ReadAsync<WorkspaceResponse>();
        Assert.Equal(WorkspaceRole.Admin, workspace!.Role);
        Assert.True(workspace.CanReadSecrets);
    }

    [Fact]
    public async Task La_invitacion_es_de_un_solo_uso()
    {
        var duenio = await _factory.LoginAsync("inv-uso-duenio");
        var uno = await _factory.LoginAsync("inv-uso-uno");
        var dos = await _factory.LoginAsync("inv-uso-dos");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Member", canReadSecrets = false })).Content.ReadAsync<InvitationResponse>();

        await uno.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation!.Token });
        var segunda = await dos.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invitation.Token });

        Assert.Equal(HttpStatusCode.BadRequest, segunda.StatusCode);
    }

    [Fact]
    public async Task La_invitacion_dirigida_a_un_mail_no_la_usa_otro()
    {
        var duenio = await _factory.LoginAsync("inv-mail-duenio");
        var otro = await _factory.LoginAsync("inv-mail-otro");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
                new { email = "esperado@test.local", role = "Member", canReadSecrets = false }))
            .Content.ReadAsync<InvitationResponse>();

        var resp = await otro.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invitation!.Token });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task La_invitacion_revocada_no_sirve()
    {
        var duenio = await _factory.LoginAsync("inv-revoc-duenio");
        var invitado = await _factory.LoginAsync("inv-revoc-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Member", canReadSecrets = false })).Content.ReadAsync<InvitationResponse>();

        await duenio.Http.DeleteAsync($"/api/v1/workspaces/{ws}/invitations/{invitation!.Id}");
        var resp = await invitado.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invitation.Token });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Un_member_no_puede_invitar()
    {
        var duenio = await _factory.LoginAsync("rol-duenio");
        var miembro = await _factory.LoginAsync("rol-miembro");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Member", canReadSecrets = false })).Content.ReadAsync<InvitationResponse>();
        await miembro.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation!.Token });

        var resp = await miembro.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Member", canReadSecrets = true });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_sacar_al_owner()
    {
        var duenio = await _factory.LoginAsync("owner-duenio");
        var admin = await _factory.LoginAsync("owner-admin");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var invitation = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/invitations",
            new { role = "Admin", canReadSecrets = true })).Content.ReadAsync<InvitationResponse>();
        await admin.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation!.Token });
        var duenioId = (await (await duenio.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var resp = await admin.Http.DeleteAsync($"/api/v1/workspaces/{ws}/members/{duenioId}");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task El_token_de_servicio_entra_y_respeta_su_permiso_de_secretos()
    {
        var duenio = await _factory.LoginAsync("svc-duenio");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "CI");
        var doc = await (await duenio.Http.PutDocumentAsync(ws, "environments/ci.json", "environment",
            """{"id":"e","name":"CI","secretKeys":["token"],"variables":[{"key":"token","value":""}]}""",
            secrets: new Dictionary<string, string> { ["token"] = "para-ci" })).ReadDocumentAsync();

        var sinSecretos = await CreateServiceClientAsync(duenio, ws, "runner", canReadSecrets: false);
        var conSecretos = await CreateServiceClientAsync(duenio, ws, "deploy", canReadSecrets: true);

        var listado = await sinSecretos.GetAsync($"/api/v1/workspaces/{ws}/documents");
        var negado = await sinSecretos.GetAsync($"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets");
        var permitido = await conSecretos.GetAsync($"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets");

        Assert.True(listado.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, negado.StatusCode);
        Assert.Equal("para-ci",
            (await permitido.Content.ReadAsync<SecretsResponse>())!.Secrets["token"]);
    }

    [Fact]
    public async Task El_token_de_servicio_solo_ve_su_workspace()
    {
        var duenio = await _factory.LoginAsync("svc-aislado");
        var suyo = await _factory.CreateWorkspaceAsync(duenio, "Suyo");
        var otro = await _factory.CreateWorkspaceAsync(duenio, "Otro");
        var http = await CreateServiceClientAsync(duenio, suyo, "acotado", canReadSecrets: false);

        var propio = await http.GetAsync($"/api/v1/workspaces/{suyo}/documents");
        var ajeno = await http.GetAsync($"/api/v1/workspaces/{otro}/documents");

        Assert.True(propio.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ajeno.StatusCode);
    }

    [Fact]
    public async Task El_token_de_servicio_no_crea_workspaces()
    {
        var duenio = await _factory.LoginAsync("svc-no-crea");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var http = await CreateServiceClientAsync(duenio, ws, "sin-permiso", canReadSecrets: false);

        var resp = await http.PostAsJsonAsync("/api/v1/workspaces", new { name = "Nuevo" });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task El_token_revocado_deja_de_servir()
    {
        var duenio = await _factory.LoginAsync("svc-revocado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var created = await (await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/tokens",
                new { name = "temporal", role = "Member", canReadSecrets = false }))
            .Content.ReadAsync<ServiceTokenResponse>();
        var http = _factory.CreateRawClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);

        await duenio.Http.DeleteAsync($"/api/v1/workspaces/{ws}/tokens/{created.Id}");
        var resp = await http.GetAsync($"/api/v1/workspaces/{ws}/documents");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task El_listado_de_workspaces_muestra_solo_los_propios()
    {
        var uno = await _factory.LoginAsync("lista-uno");
        var dos = await _factory.LoginAsync("lista-dos");
        await _factory.CreateWorkspaceAsync(uno, "De uno");
        await _factory.CreateWorkspaceAsync(dos, "De dos");

        var lista = await (await uno.Http.GetAsync("/api/v1/workspaces"))
            .Content.ReadAsync<WorkspaceResponse[]>();

        Assert.Single(lista!);
        Assert.Equal("De uno", lista![0].Name);
    }

    async Task<HttpClient> CreateServiceClientAsync(TestUser admin, Guid workspaceId, string name,
        bool canReadSecrets)
    {
        var created = await (await admin.Http.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/tokens",
                new { name, role = "Member", canReadSecrets }))
            .Content.ReadAsync<ServiceTokenResponse>();

        var http = _factory.CreateRawClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created!.Token);
        return http;
    }
}
