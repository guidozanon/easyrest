using EasyRest.Sync.Server.Admin;
using EasyRest.Sync.Server.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyRest.Sync.Server.Pages.Admin;

/// <summary>Vuelta del login: se canjea el código con el verifier guardado y se deja la sesión
/// en la cookie. Sólo entran administradores del server; el resto se va con un mensaje claro y
/// no con un 403 pelado.</summary>
public class CallbackModel(AuthService auth) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? code, CancellationToken ct)
    {
        var verifier = Request.Cookies[AdminSession.PkceCookieName];
        Response.Cookies.Delete(AdminSession.PkceCookieName);

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(verifier))
            return Error("El login no se completó: probá de nuevo.");

        try
        {
            var (tokens, user) = await auth.ExchangeCodeAsync(code, verifier, ct);

            if (!user.IsServerAdmin)
                return Error("Tu usuario no es administrador de este server.");

            AdminSession.SignIn(HttpContext, tokens.AccessToken, tokens.AccessExpiresAt);
            return Redirect("/Admin");
        }
        catch (AuthException ex)
        {
            return Error(ex.Message);
        }
    }

    IActionResult Error(string message) => Redirect("/Admin/Login?error=" + Uri.EscapeDataString(message));
}
