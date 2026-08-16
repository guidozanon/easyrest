using System.Net;
using System.Net.Http.Json;
using EasyRest.Sync.Server.Data;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

/// <summary>Administración del server. Usa su propia factory porque varios casos dependen de
/// quién entró primero, y eso es estado global del server.</summary>
public class AdminTests : IDisposable
{
    readonly SyncServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task El_primero_que_entra_queda_como_admin_del_server()
    {
        var primero = await _factory.LoginAsync("admin-primero");
        var segundo = await _factory.LoginAsync("admin-segundo");

        var uno = await (await primero.Http.GetAsync("/api/v1/me")).ReadJsonAsync();
        var dos = await (await segundo.Http.GetAsync("/api/v1/me")).ReadJsonAsync();

        Assert.True(uno.GetProperty("isServerAdmin").GetBoolean());
        Assert.False(dos.GetProperty("isServerAdmin").GetBoolean());
    }

    [Fact]
    public async Task Un_usuario_comun_no_ve_la_administracion()
    {
        await _factory.LoginAsync("no-admin-primero");
        var comun = await _factory.LoginAsync("no-admin-comun");

        var usuarios = await comun.Http.GetAsync("/api/v1/admin/users");
        var workspaces = await comun.Http.GetAsync("/api/v1/admin/workspaces");

        Assert.Equal(HttpStatusCode.Forbidden, usuarios.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, workspaces.StatusCode);
    }

    [Fact]
    public async Task El_admin_lista_usuarios_y_workspaces_de_todo_el_server()
    {
        var admin = await _factory.LoginAsync("lista-admin");
        var otro = await _factory.LoginAsync("lista-otro");
        await _factory.CreateWorkspaceAsync(otro, "De otro");

        var usuarios = await (await admin.Http.GetAsync("/api/v1/admin/users"))
            .Content.ReadAsync<AdminUserResponse[]>();
        var workspaces = await (await admin.Http.GetAsync("/api/v1/admin/workspaces"))
            .Content.ReadAsync<AdminWorkspaceResponse[]>();

        Assert.Equal(2, usuarios!.Length);
        // el workspace es de otra persona y el admin del server igual lo ve
        var ws = Assert.Single(workspaces!);
        Assert.Equal("De otro", ws.Name);
        Assert.Equal("lista-otro@test.local", ws.OwnerEmail);
        Assert.Equal(1, ws.Members);
    }

    [Fact]
    public async Task Desactivar_a_alguien_le_corta_la_sesion_en_el_acto()
    {
        var admin = await _factory.LoginAsync("baja-admin");
        var victima = await _factory.LoginAsync("baja-victima");
        var victimaId = (await (await victima.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        Assert.True((await victima.Http.GetAsync("/api/v1/me")).IsSuccessStatusCode);

        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{victimaId}", new { disabled = true });

        // el token seguía siendo válido por tiempo: lo que corta es la desactivación
        var despues = await victima.Http.GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
    }

    [Fact]
    public async Task Un_usuario_desactivado_tampoco_puede_volver_a_entrar()
    {
        var admin = await _factory.LoginAsync("relogin-admin");
        var victima = await _factory.LoginAsync("relogin-victima");
        var victimaId = (await (await victima.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{victimaId}", new { disabled = true });

        await Assert.ThrowsAnyAsync<Exception>(() => _factory.LoginAsync("relogin-victima"));
    }

    [Fact]
    public async Task Reactivar_devuelve_el_acceso()
    {
        var admin = await _factory.LoginAsync("alta-admin");
        var usuario = await _factory.LoginAsync("alta-usuario");
        var id = (await (await usuario.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { disabled = true });

        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { disabled = false });
        var reentrada = await _factory.LoginAsync("alta-usuario");

        Assert.True((await reentrada.Http.GetAsync("/api/v1/me")).IsSuccessStatusCode);
    }

    [Fact]
    public async Task El_admin_no_se_puede_desactivar_a_si_mismo()
    {
        var admin = await _factory.LoginAsync("solo-admin");
        var id = (await (await admin.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var resp = await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{id}", new { disabled = true });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task No_se_puede_quitar_al_ultimo_administrador()
    {
        var admin = await _factory.LoginAsync("ultimo-admin");
        var id = (await (await admin.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var resp = await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{id}",
            new { isServerAdmin = false });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Se_puede_nombrar_otro_admin_y_recien_ahi_renunciar()
    {
        var admin = await _factory.LoginAsync("relevo-admin");
        var sucesor = await _factory.LoginAsync("relevo-sucesor");
        var adminId = (await (await admin.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();
        var sucesorId = (await (await sucesor.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{sucesorId}", new { isServerAdmin = true });
        var renuncia = await admin.Http.PatchAsJsonAsync($"/api/v1/admin/users/{adminId}",
            new { isServerAdmin = false });

        Assert.True(renuncia.IsSuccessStatusCode);
        Assert.True((await sucesor.Http.GetAsync("/api/v1/admin/users")).IsSuccessStatusCode);
    }

    // ----- Transferencia de ownership -----

    [Fact]
    public async Task El_owner_transfiere_y_queda_como_admin_del_workspace()
    {
        var duenio = await _factory.LoginAsync("transf-duenio");
        var sucesor = await _factory.LoginAsync("transf-sucesor");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var sucesorId = await InviteAsync(duenio, sucesor, ws, WorkspaceRole.Member);
        var duenioId = (await (await duenio.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var resp = await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/transfer-ownership",
            new { userId = sucesorId });

        Assert.True(resp.IsSuccessStatusCode);
        var miembros = await (await sucesor.Http.GetAsync($"/api/v1/workspaces/{ws}/members"))
            .Content.ReadAsync<MemberResponse[]>();
        Assert.Equal(WorkspaceRole.Owner, miembros!.Single(m => m.UserId == sucesorId).Role);
        Assert.Equal(WorkspaceRole.Admin, miembros.Single(m => m.UserId == duenioId).Role);
    }

    [Fact]
    public async Task El_nuevo_owner_puede_hacer_lo_que_solo_el_owner_hace()
    {
        var duenio = await _factory.LoginAsync("transf-poder-duenio");
        var sucesor = await _factory.LoginAsync("transf-poder-sucesor");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var sucesorId = await InviteAsync(duenio, sucesor, ws, WorkspaceRole.Member);
        await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/transfer-ownership",
            new { userId = sucesorId });

        // borrar el workspace es exclusivo del owner
        var borrado = await sucesor.Http.DeleteAsync($"/api/v1/workspaces/{ws}");

        Assert.Equal(HttpStatusCode.NoContent, borrado.StatusCode);
    }

    [Fact]
    public async Task Un_admin_del_workspace_no_puede_transferir()
    {
        var duenio = await _factory.LoginAsync("transf-no-admin-duenio");
        var admin = await _factory.LoginAsync("transf-no-admin-admin");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var adminId = await InviteAsync(duenio, admin, ws, WorkspaceRole.Admin);

        var resp = await admin.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/transfer-ownership",
            new { userId = adminId });

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task El_admin_del_server_rescata_un_workspace_del_que_no_es_miembro()
    {
        var admin = await _factory.LoginAsync("rescate-admin");     // primero: admin del server
        var duenio = await _factory.LoginAsync("rescate-duenio");
        var sucesor = await _factory.LoginAsync("rescate-sucesor");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Huérfano");
        var sucesorId = await InviteAsync(duenio, sucesor, ws, WorkspaceRole.Member);

        var resp = await admin.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/transfer-ownership",
            new { userId = sucesorId });

        Assert.True(resp.IsSuccessStatusCode);
    }

    [Fact]
    public async Task No_se_transfiere_a_alguien_que_no_es_miembro()
    {
        var duenio = await _factory.LoginAsync("transf-ajeno-duenio");
        var ajeno = await _factory.LoginAsync("transf-ajeno-ajeno");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Equipo");
        var ajenoId = (await (await ajeno.Http.GetAsync("/api/v1/me")).ReadJsonAsync())
            .GetProperty("id").GetGuid();

        var resp = await duenio.Http.PostAsJsonAsync($"/api/v1/workspaces/{ws}/transfer-ownership",
            new { userId = ajenoId });

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    async Task<Guid> InviteAsync(TestUser admin, TestUser invitado, Guid workspaceId, WorkspaceRole role)
    {
        var invitation = await (await admin.Http.PostAsJsonAsync(
                $"/api/v1/workspaces/{workspaceId}/invitations",
                new { role = role.ToString(), canReadSecrets = true }))
            .Content.ReadAsync<InvitationResponse>();
        await invitado.Http.PostAsJsonAsync("/api/v1/invitations/accept", new { token = invitation!.Token });

        var me = await (await invitado.Http.GetAsync("/api/v1/me")).ReadJsonAsync();
        return me.GetProperty("id").GetGuid();
    }
}
