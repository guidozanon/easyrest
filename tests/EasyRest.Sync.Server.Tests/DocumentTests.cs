using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

public class DocumentTests : IClassFixture<SyncServerFactory>
{
    readonly SyncServerFactory _factory;

    public DocumentTests(SyncServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Crear_un_documento_le_asigna_revision_y_secuencia()
    {
        var user = await _factory.LoginAsync("doc-crear");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");

        var doc = await (await user.Http.PutDocumentAsync(ws, "collections/API/login.req.json",
            "request", """{"method":"GET"}""")).ReadDocumentAsync();

        Assert.NotEmpty(doc.Rev);
        Assert.True(doc.Seq > 0);
        Assert.False(doc.Deleted);
        Assert.Equal("collections/API/login.req.json", doc.Path);
    }

    [Fact]
    public async Task Crear_dos_veces_el_mismo_path_sin_If_Match_da_conflicto()
    {
        var user = await _factory.LoginAsync("doc-dup");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        await user.Http.PutDocumentAsync(ws, "collections/a.req.json", "request", "{}");

        var repetido = await user.Http.PutDocumentAsync(ws, "collections/a.req.json", "request", """{"x":1}""");

        Assert.Equal(HttpStatusCode.Conflict, repetido.StatusCode);
    }

    [Fact]
    public async Task Actualizar_con_la_revision_correcta_funciona_y_con_una_vieja_no()
    {
        var user = await _factory.LoginAsync("doc-rev");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var v1 = await (await user.Http.PutDocumentAsync(ws, "collections/b.req.json", "request", "{}"))
            .ReadDocumentAsync();

        var v2 = await (await user.Http.PutDocumentAsync(ws, "collections/b.req.json", "request",
            """{"v":2}""", ifMatch: v1.Rev)).ReadDocumentAsync();
        var conflicto = await user.Http.PutDocumentAsync(ws, "collections/b.req.json", "request",
            """{"v":3}""", ifMatch: v1.Rev);

        Assert.NotEqual(v1.Rev, v2.Rev);
        Assert.True(v2.Seq > v1.Seq);
        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
    }

    [Fact]
    public async Task El_conflicto_devuelve_la_version_del_server_para_no_perder_la_edicion()
    {
        var user = await _factory.LoginAsync("doc-conflicto");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var v1 = await (await user.Http.PutDocumentAsync(ws, "collections/c.req.json", "request", "{}"))
            .ReadDocumentAsync();
        await user.Http.PutDocumentAsync(ws, "collections/c.req.json", "request", """{"server":true}""",
            ifMatch: v1.Rev);

        var conflicto = await user.Http.PutDocumentAsync(ws, "collections/c.req.json", "request",
            """{"local":true}""", ifMatch: v1.Rev);
        var body = await conflicto.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, conflicto.StatusCode);
        Assert.Equal("""{"server":true}""", body.GetProperty("current").GetProperty("content").GetString());
    }

    [Fact]
    public async Task If_Match_asterisco_pisa_sin_preguntar()
    {
        var user = await _factory.LoginAsync("doc-force");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        await user.Http.PutDocumentAsync(ws, "collections/d.req.json", "request", "{}");

        var forzado = await user.Http.PutDocumentAsync(ws, "collections/d.req.json", "request",
            """{"forzado":true}""", ifMatch: "*");

        Assert.True(forzado.IsSuccessStatusCode);
    }

    [Fact]
    public async Task El_cursor_devuelve_solo_lo_posterior()
    {
        var user = await _factory.LoginAsync("doc-cursor");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        await user.Http.PutDocumentAsync(ws, "collections/uno.req.json", "request", "{}");
        var primeraTanda = await (await user.Http.GetAsync($"/api/v1/workspaces/{ws}/changes?since=0"))
            .Content.ReadAsync<ChangesResponse>();

        await user.Http.PutDocumentAsync(ws, "collections/dos.req.json", "request", "{}");
        var delta = await (await user.Http.GetAsync($"/api/v1/workspaces/{ws}/changes?since={primeraTanda!.Cursor}"))
            .Content.ReadAsync<ChangesResponse>();

        Assert.Single(delta!.Documents);
        Assert.Equal("collections/dos.req.json", delta.Documents[0].Path);
        Assert.True(delta.Cursor > primeraTanda.Cursor);
    }

    [Fact]
    public async Task El_borrado_viaja_como_tombstone()
    {
        var user = await _factory.LoginAsync("doc-borrar");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var doc = await (await user.Http.PutDocumentAsync(ws, "collections/e.req.json", "request", "{}"))
            .ReadDocumentAsync();
        var antes = await (await user.Http.GetAsync($"/api/v1/workspaces/{ws}/changes?since=0"))
            .Content.ReadAsync<ChangesResponse>();

        var request = new HttpRequestMessage(HttpMethod.Delete,
            $"/api/v1/workspaces/{ws}/documents/{doc.Id}");
        request.Headers.TryAddWithoutValidation("If-Match", doc.Rev);
        var borrado = await user.Http.SendAsync(request);

        var delta = await (await user.Http.GetAsync($"/api/v1/workspaces/{ws}/changes?since={antes!.Cursor}"))
            .Content.ReadAsync<ChangesResponse>();

        Assert.True(borrado.IsSuccessStatusCode);
        Assert.Single(delta!.Documents);
        Assert.True(delta.Documents[0].Deleted);
        Assert.Null(delta.Documents[0].Content);
    }

    [Fact]
    public async Task Borrar_con_una_revision_vieja_no_borra()
    {
        var user = await _factory.LoginAsync("doc-borrar-viejo");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var v1 = await (await user.Http.PutDocumentAsync(ws, "collections/f.req.json", "request", "{}"))
            .ReadDocumentAsync();
        await user.Http.PutDocumentAsync(ws, "collections/f.req.json", "request", """{"v":2}""", ifMatch: v1.Rev);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/workspaces/{ws}/documents/{v1.Id}");
        request.Headers.TryAddWithoutValidation("If-Match", v1.Rev);
        var resp = await user.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Un_path_borrado_se_puede_volver_a_crear()
    {
        var user = await _factory.LoginAsync("doc-revivir");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        var doc = await (await user.Http.PutDocumentAsync(ws, "collections/g.req.json", "request", "{}"))
            .ReadDocumentAsync();
        var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/workspaces/{ws}/documents/{doc.Id}");
        del.Headers.TryAddWithoutValidation("If-Match", doc.Rev);
        await user.Http.SendAsync(del);

        var recreado = await user.Http.PutDocumentAsync(ws, "collections/g.req.json", "request", """{"otra":1}""");

        Assert.True(recreado.IsSuccessStatusCode);
        Assert.False((await recreado.ReadDocumentAsync()).Deleted);
    }

    [Fact]
    public async Task El_cursor_pagina_cuando_hay_mas_de_lo_pedido()
    {
        var user = await _factory.LoginAsync("doc-paginado");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");
        for (var i = 0; i < 5; i++)
            await user.Http.PutDocumentAsync(ws, $"collections/p{i}.req.json", "request", "{}");

        var page = await (await user.Http.GetAsync($"/api/v1/workspaces/{ws}/changes?since=0&limit=2"))
            .Content.ReadAsync<ChangesResponse>();

        Assert.Equal(2, page!.Documents.Length);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task Un_path_que_se_escapa_del_workspace_se_rechaza()
    {
        var user = await _factory.LoginAsync("doc-path");
        var ws = await _factory.CreateWorkspaceAsync(user, "Equipo");

        var resp = await user.Http.PutDocumentAsync(ws, "../../etc/passwd", "request", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Un_workspace_ajeno_no_existe_para_los_demas()
    {
        var duenio = await _factory.LoginAsync("doc-duenio");
        var ajeno = await _factory.LoginAsync("doc-ajeno");
        var ws = await _factory.CreateWorkspaceAsync(duenio, "Privado");
        await duenio.Http.PutDocumentAsync(ws, "collections/secreta.req.json", "request", "{}");

        var lista = await ajeno.Http.GetAsync($"/api/v1/workspaces/{ws}/documents");
        var escritura = await ajeno.Http.PutDocumentAsync(ws, "collections/intruso.req.json", "request", "{}");

        Assert.Equal(HttpStatusCode.NotFound, lista.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, escritura.StatusCode);
    }
}
