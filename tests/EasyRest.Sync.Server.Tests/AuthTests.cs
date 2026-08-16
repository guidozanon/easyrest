using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EasyRest.Sync.Server.Crypto;
using Xunit;

namespace EasyRest.Sync.Server.Tests;

public class AuthTests : IClassFixture<SyncServerFactory>
{
    readonly SyncServerFactory _factory;

    public AuthTests(SyncServerFactory factory) => _factory = factory;

    [Fact]
    public async Task Meta_publica_los_providers_configurados()
    {
        var http = _factory.CreateRawClient();

        var meta = await (await http.GetAsync("/api/v1/meta")).ReadJsonAsync();

        Assert.Equal("easyrest-sync", meta.GetProperty("server").GetString());
        Assert.Contains(1, meta.GetProperty("apiVersions").EnumerateArray().Select(x => x.GetInt32()));
        var providers = meta.GetProperty("auth").GetProperty("providers").EnumerateArray().ToList();
        Assert.Single(providers);
        Assert.Equal("fake", providers[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task Login_con_pkce_devuelve_una_sesion_usable()
    {
        var user = await _factory.LoginAsync("ana");

        var me = await (await user.Http.GetAsync("/api/v1/me")).ReadJsonAsync();

        Assert.Equal("ana@test.local", me.GetProperty("email").GetString());
    }

    [Fact]
    public async Task El_code_no_sirve_con_otro_verifier()
    {
        var http = _factory.CreateRawClient();
        var challenge = Tokens.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes("el-verifier-bueno")));

        var start = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                        "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb" +
                                        $"&code_challenge={challenge}&state=xyz");
        var state = SyncServerFactory.QueryValue(start.Headers.Location!, "state");
        var callback = await http.GetAsync($"/api/v1/auth/callback?code=mario&state={Uri.EscapeDataString(state)}");
        var code = SyncServerFactory.QueryValue(callback.Headers.Location!, "code");

        var resp = await http.PostAsJsonAsync("/api/v1/auth/token", new
        {
            grantType = "authorization_code",
            code,
            codeVerifier = "otro-verifier-cualquiera"
        });

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task El_code_es_de_un_solo_uso()
    {
        var http = _factory.CreateRawClient();
        var verifier = Tokens.Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Tokens.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var start = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                        "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb" +
                                        $"&code_challenge={challenge}&state=xyz");
        var state = SyncServerFactory.QueryValue(start.Headers.Location!, "state");
        var callback = await http.GetAsync($"/api/v1/auth/callback?code=leo&state={Uri.EscapeDataString(state)}");
        var code = SyncServerFactory.QueryValue(callback.Headers.Location!, "code");

        var first = await http.PostAsJsonAsync("/api/v1/auth/token",
            new { grantType = "authorization_code", code, codeVerifier = verifier });
        var second = await http.PostAsJsonAsync("/api/v1/auth/token",
            new { grantType = "authorization_code", code, codeVerifier = verifier });

        Assert.True(first.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task El_refresh_rota_y_el_anterior_deja_de_servir()
    {
        var user = await _factory.LoginAsync("rota");

        var refreshed = await _factory.CreateRawClient().PostAsJsonAsync("/api/v1/auth/token",
            new { grantType = "refresh_token", refreshToken = user.Tokens.RefreshToken });
        refreshed.EnsureSuccessStatusCode();
        var reused = await _factory.CreateRawClient().PostAsJsonAsync("/api/v1/auth/token",
            new { grantType = "refresh_token", refreshToken = user.Tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task El_logout_invalida_el_access_token()
    {
        var user = await _factory.LoginAsync("chau");

        await user.Http.PostAsync("/api/v1/auth/logout", null);
        var me = await user.Http.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Sin_token_no_se_entra()
    {
        var http = _factory.CreateRawClient();

        var me = await http.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData("https://atacante.example/cb")]        // no es loopback
    [InlineData("http://atacante.example/cb")]         // http pero remoto
    [InlineData("otroesquema://cb")]                   // esquema no registrado
    public async Task Rechaza_redirects_que_no_sean_loopback_ni_esquema_registrado(string redirect)
    {
        var http = _factory.CreateRawClient();

        var resp = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                       $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
                                       "&code_challenge=abc&state=xyz");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Acepta_el_esquema_propio_registrado_para_movil()
    {
        var http = _factory.CreateRawClient();

        var resp = await http.GetAsync("/api/v1/auth/start?provider=fake" +
                                       "&redirect_uri=easyrest%3A%2F%2Fauth" +
                                       "&code_challenge=abc&state=xyz");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
    }

    [Fact]
    public async Task Un_provider_que_no_existe_no_arranca_login()
    {
        var http = _factory.CreateRawClient();

        var resp = await http.GetAsync("/api/v1/auth/start?provider=inventado" +
                                       "&redirect_uri=http%3A%2F%2F127.0.0.1%3A5599%2Fcb" +
                                       "&code_challenge=abc&state=xyz");

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Un_token_inventado_no_sirve()
    {
        var http = _factory.CreateRawClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "no-existo");

        var me = await http.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }
}
