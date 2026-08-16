using System.Net;
using System.Net.Http.Json;
using System.Text;
using EasyRest.Sync.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

public class SecretTests : IClassFixture<SyncServerFactory>
{
    readonly SyncServerFactory _factory;

    public SecretTests(SyncServerFactory factory) => _factory = factory;

    const string EnvJson = """
        {"id":"e1","name":"Producción",
         "secretKeys":["token"],
         "variables":[{"key":"baseUrl","value":"https://api.example.com","enabled":true},
                      {"key":"token","value":"","enabled":true}]}
        """;

    [Fact]
    public async Task El_ambiente_guarda_y_devuelve_los_secretos_a_quien_puede_verlos()
    {
        var user = await _factory.LoginAsync("sec-duenio");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");

        var doc = await (await user.Http.PutDocumentAsync(ws, "environments/prod.json", "environment",
            EnvJson, secrets: new Dictionary<string, string> { ["token"] = "super-secreto" }))
            .ReadDocumentAsync();
        var secrets = await (await user.Http.GetAsync(
            $"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets")).Content.ReadAsync<SecretsResponse>();

        Assert.Equal("super-secreto", secrets!.Secrets["token"]);
    }

    [Fact]
    public async Task Los_secretos_estan_cifrados_en_reposo()
    {
        var user = await _factory.LoginAsync("sec-reposo");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        await user.Http.PutDocumentAsync(ws, "environments/cifrado.json", "environment", EnvJson,
            secrets: new Dictionary<string, string> { ["token"] = "valor-en-claro" });

        await _factory.WithDbAsync(async db =>
        {
            var row = await db.SecretValues.FirstAsync(s => s.Key == "token");
            var asText = Encoding.UTF8.GetString(row.Ciphertext);

            Assert.DoesNotContain("valor-en-claro", asText);
            Assert.NotEmpty(row.Nonce);
            Assert.NotEmpty(row.Tag);
        });
    }

    [Fact]
    public async Task Un_miembro_sin_permiso_no_ve_los_valores()
    {
        var duenio = await _factory.LoginAsync("sec-admin");
        var invitado = await _factory.LoginAsync("sec-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var doc = await (await duenio.Http.PutDocumentAsync(ws, "environments/gate.json", "environment",
            EnvJson, secrets: new Dictionary<string, string> { ["token"] = "no-lo-ves" })).ReadDocumentAsync();
        await InviteAsync(duenio, invitado, ws, WorkspaceRole.Member, canReadSecrets: false);

        var comoInvitado = await invitado.Http.GetAsync($"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets");
        var documento = await (await invitado.Http.GetAsync(
            $"/api/v1/workspaces/{ws}/documents/{doc.Id}")).ReadDocumentAsync();

        Assert.Equal(HttpStatusCode.Forbidden, comoInvitado.StatusCode);
        // y el valor tampoco viaja escondido dentro del documento
        Assert.DoesNotContain("no-lo-ves", documento.Content);
    }

    [Fact]
    public async Task Un_miembro_sin_permiso_no_puede_pisar_los_secretos()
    {
        var duenio = await _factory.LoginAsync("sec-pisar-admin");
        var invitado = await _factory.LoginAsync("sec-pisar-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var doc = await (await duenio.Http.PutDocumentAsync(ws, "environments/pisar.json", "environment",
            EnvJson, secrets: new Dictionary<string, string> { ["token"] = "el-bueno" })).ReadDocumentAsync();
        await InviteAsync(duenio, invitado, ws, WorkspaceRole.Member, canReadSecrets: false);

        // el invitado edita la parte no secreta del ambiente y manda secretos vacíos
        await invitado.Http.PutDocumentAsync(ws, "environments/pisar.json", "environment", EnvJson,
            ifMatch: doc.Rev, secrets: new Dictionary<string, string> { ["token"] = "" });

        var secrets = await (await duenio.Http.GetAsync(
            $"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets")).Content.ReadAsync<SecretsResponse>();
        Assert.Equal("el-bueno", secrets!.Secrets["token"]);
    }

    [Fact]
    public async Task El_override_por_ambiente_gana_sobre_el_default_del_miembro()
    {
        var duenio = await _factory.LoginAsync("sec-override-admin");
        var invitado = await _factory.LoginAsync("sec-override-invitado");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var doc = await (await duenio.Http.PutDocumentAsync(ws, "environments/override.json", "environment",
            EnvJson, secrets: new Dictionary<string, string> { ["token"] = "visible" })).ReadDocumentAsync();
        var userId = await InviteAsync(duenio, invitado, ws, WorkspaceRole.Member, canReadSecrets: false);

        await duenio.Http.PutAsJsonAsync(
            $"/api/v1/workspaces/{ws}/documents/{doc.Id}/secret-access",
            new { userId, canRead = true });
        var secrets = await invitado.Http.GetAsync($"/api/v1/workspaces/{ws}/documents/{doc.Id}/secrets");

        Assert.True(secrets.IsSuccessStatusCode);
        Assert.Equal("visible",
            (await secrets.Content.ReadAsync<SecretsResponse>())!.Secrets["token"]);
    }

    [Fact]
    public async Task Un_secreto_dentro_del_documento_se_rechaza()
    {
        var user = await _factory.LoginAsync("sec-inline");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");

        var conValor = """
            {"id":"e2","name":"Mal",
             "secretKeys":["token"],
             "variables":[{"key":"token","value":"esto-no-va-aca","enabled":true}]}
            """;
        var resp = await user.Http.PutDocumentAsync(ws, "environments/mal.json", "environment", conValor);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("token", (await resp.ReadJsonAsync()).GetProperty("detail").GetString()!);
    }

    [Fact]
    public async Task Borrar_el_ambiente_borra_sus_secretos()
    {
        var user = await _factory.LoginAsync("sec-borrar");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var doc = await (await user.Http.PutDocumentAsync(ws, "environments/borrar.json", "environment",
            EnvJson, secrets: new Dictionary<string, string> { ["token"] = "efimero" })).ReadDocumentAsync();

        var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/workspaces/{ws}/documents/{doc.Id}");
        del.Headers.TryAddWithoutValidation("If-Match", doc.Rev);
        await user.Http.SendAsync(del);

        await _factory.WithDbAsync(async db =>
            Assert.False(await db.SecretValues.AnyAsync(s => s.DocumentId == doc.Id)));
    }

    /// <summary>Invita a alguien y devuelve su userId ya como miembro.</summary>
    async Task<Guid> InviteAsync(TestUser admin, TestUser invitado, Guid workspaceId,
        WorkspaceRole role, bool canReadSecrets)
    {
        var invitation = await (await admin.Http.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspaceId}/invitations",
                new { role = role.ToString(), canReadSecrets }))
            .Content.ReadAsync<InvitationResponse>();

        var accepted = await invitado.Http.PostAsJsonAsync("/api/v1/invitations/accept",
            new { token = invitation!.Token });
        accepted.EnsureSuccessStatusCode();

        var me = await (await invitado.Http.GetAsync("/api/v1/me")).ReadJsonAsync();
        return me.GetProperty("id").GetGuid();
    }
}
