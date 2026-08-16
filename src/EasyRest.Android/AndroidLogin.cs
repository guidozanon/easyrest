using Android.App;
using Android.Content;
using EasyRest.Services.Sync;

namespace EasyRest.Android;

/// <summary>El login OAuth en el teléfono. Mismo PKCE que el escritorio, pero el código no vuelve
/// por un puerto local sino por un esquema propio: <c>easyrest://login</c>, que Android entrega a
/// <see cref="LoginActivity"/>.
///
/// El server ya lo contempla: acepta loopback para escritorio y esquemas registrados en
/// Auth:AllowedRedirectSchemes para móvil. Hay que agregar "easyrest" a esa lista en el server.
///
/// Se abre el navegador del sistema y no una WebView adentro de la app: así el login usa las
/// sesiones que la persona ya tiene y la app nunca ve la contraseña. Una WebView propia acá sería
/// justo el patrón que los proveedores de identidad piden no usar.</summary>
public static class AndroidLogin
{
    public const string RedirectUri = "easyrest://login";

    /// <summary>El login sale de la app y vuelve por otra actividad, así que hace falta un punto de
    /// encuentro. Es estático porque sólo puede haber un login en curso: si arranca otro, el
    /// anterior se cancela.</summary>
    static TaskCompletionSource<global::Android.Net.Uri>? _esperando;

    public static async Task<SyncSession> RunAsync(Context contexto, SyncApiClient api,
        string providerId, CancellationToken ct = default)
    {
        var pkce = SyncPkce.Create();

        _esperando?.TrySetCanceled();
        var espera = new TaskCompletionSource<global::Android.Net.Uri>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _esperando = espera;

        var url = api.BuildLoginUrl(providerId, RedirectUri, pkce.Challenge, pkce.State);
        var abrir = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
        abrir.AddFlags(ActivityFlags.NewTask);
        contexto.StartActivity(abrir);

        await using var _ = ct.Register(() => espera.TrySetCanceled(ct));
        var vuelta = await espera.Task;

        var error = vuelta.GetQueryParameter("error");
        if (error != null) throw new InvalidOperationException($"El login falló: {error}");

        // sin esta comparación, cualquier app o página podría empujar un código ajeno
        if (vuelta.GetQueryParameter("state") != pkce.State)
            throw new InvalidOperationException("La respuesta no corresponde a este intento de login.");

        var code = vuelta.GetQueryParameter("code")
                   ?? throw new InvalidOperationException("El navegador volvió sin código.");

        return await api.ExchangeCodeAsync(code, pkce.Verifier, ct);
    }

    /// <summary>La llama <see cref="LoginActivity"/> cuando Android le entrega el redirect.</summary>
    internal static void Recibir(global::Android.Net.Uri uri) => _esperando?.TrySetResult(uri);
}

/// <summary>Recibe el <c>easyrest://login</c> del navegador y se cierra sola. No dibuja nada: sólo
/// existe para que Android tenga a quién entregarle el redirect.</summary>
[Activity(
    Name = "com.rentlysoft.easyrest.LoginActivity",
    Exported = true,
    NoHistory = true,
    LaunchMode = global::Android.Content.PM.LaunchMode.SingleTask,
    Theme = "@style/EasyRestTheme")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "easyrest",
    DataHost = "login")]
public class LoginActivity : Activity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Entregar(Intent);
        Finish();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Entregar(intent);
        Finish();
    }

    void Entregar(Intent? intent)
    {
        if (intent?.Data is { } data) AndroidLogin.Recibir(data);
    }
}
