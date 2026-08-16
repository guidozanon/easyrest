using System.Security.Cryptography;
using System.Text;
using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Crypto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace EasyRest.Sync.Server.Pages.Admin;

/// <summary>Login de la consola. Usa el mismo flujo PKCE que la app —no hay un segundo camino de
/// autenticación que auditar— sólo que el redirect vuelve al propio server y la sesión queda en
/// una cookie en vez de en el token de la app.</summary>
public class LoginModel(AuthService auth, IdentityProviderRegistry providers, IOptions<AuthOptions> options)
    : PageModel
{
    public List<IIdentityProvider> Providers { get; private set; } = new();

    [BindProperty(SupportsGet = true, Name = "error")]
    public string? ErrorMsg { get; set; }

    public void OnGet() => Providers = providers.All.ToList();

    public async Task<IActionResult> OnPostStartAsync(string provider, CancellationToken ct)
    {
        Providers = providers.All.ToList();

        var verifier = Tokens.Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Tokens.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        try
        {
            var url = await auth.StartAsync(provider, CallbackUrl, challenge, "admin", ct);

            // el verifier no puede viajar al IdP: queda en una cookie de vida corta y se usa
            // recién al canjear el código
            Response.Cookies.Append(AdminSession.PkceCookieName, verifier, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddMinutes(10),
                Path = "/"
            });

            return Redirect(url);
        }
        catch (AuthException ex)
        {
            ErrorMsg = ex.Message;
            return Page();
        }
    }

    public IActionResult OnPostLogout()
    {
        AdminSession.SignOut(HttpContext);
        return Redirect("/Admin/Login");
    }

    string CallbackUrl => options.Value.PublicUrl.TrimEnd('/') + "/Admin/Callback";
}
