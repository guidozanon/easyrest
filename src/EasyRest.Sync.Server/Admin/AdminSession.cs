using EasyRest.Sync.Server.Auth;
using EasyRest.Sync.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace EasyRest.Sync.Server.Admin;

public class AdminOptions
{
    /// <summary>La consola se puede apagar entera, para quien quiera exponer sólo la API.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>La sesión de la consola. Guarda en una cookie el mismo access token opaco que usa la
/// app: así revocarlo o desactivar a la persona corta también la consola, sin un segundo sistema
/// de sesiones que mantener sincronizado.</summary>
public static class AdminSession
{
    public const string CookieName = "easyrest_admin";
    public const string PkceCookieName = "easyrest_admin_pkce";

    public static void SignIn(HttpContext context, string accessToken, DateTime expiresAt) =>
        context.Response.Cookies.Append(CookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            Path = "/"
        });

    public static void SignOut(HttpContext context)
    {
        context.Response.Cookies.Delete(CookieName);
        context.Response.Cookies.Delete(PkceCookieName);
    }

    public static string? Token(HttpContext context) => context.Request.Cookies[CookieName];
}

/// <summary>Base de todas las páginas de la consola: resuelve quién entró y corta si no es
/// administrador del server. Va como filtro para que valga en todos los handlers, incluidos los
/// POST — que una página se olvide de chequear no puede ser una opción.</summary>
public abstract class AdminPageModel : PageModel
{
    public User CurrentUser { get; private set; } = null!;

    /// <summary>Mensaje para mostrar después de una acción (se pasa por query string).</summary>
    [BindProperty(SupportsGet = true)]
    public string? Aviso { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ErrorMsg { get; set; }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        var auth = HttpContext.RequestServices.GetRequiredService<AuthService>();
        var token = AdminSession.Token(HttpContext);
        var caller = token == null
            ? null
            : await auth.ResolveCallerAsync($"Bearer {token}", HttpContext.RequestAborted);

        if (caller?.User is not { } user)
        {
            AdminSession.SignOut(HttpContext);
            context.Result = Redirect("/Admin/Login");
            return;
        }

        if (!user.IsServerAdmin)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }

        CurrentUser = user;
        await next();
    }
}
