using System.Net;
using System.Net.Http;

namespace EasyRest.Services.Sync;

/// <summary>Una sesión viva contra un servidor de sync: el cliente HTTP más la cuenta guardada,
/// con el refresh del token resuelto adentro.
///
/// El resto de la app no debería tocar <see cref="SyncApiClient"/> a mano: si lo hace, se queda sin
/// refresh y la sesión se le muere en silencio a la hora.</summary>
public class SyncConnection : IDisposable
{
    /// <summary>Margen para refrescar antes de que venza. Sin esto, una request que sale justo
    /// sobre el vencimiento se come un 401 evitable.</summary>
    static readonly TimeSpan Margen = TimeSpan.FromMinutes(2);

    readonly SemaphoreSlim _refrescando = new(1, 1);
    readonly SyncAccountStore _store;

    SyncConnection(SyncAccount account, SyncApiClient api, SyncAccountStore store)
    {
        Account = account;
        Api = api;
        _store = store;
        api.AccessToken = account.AccessToken;
        api.TokenRefresher = RefrescarAsync;
    }

    public SyncAccount Account { get; }
    public SyncApiClient Api { get; }

    /// <summary>Sesión guardada para ese server, o null si nunca se inició sesión.</summary>
    public static SyncConnection? Restore(string serverUrl, SyncAccountStore? store = null,
        HttpClient? http = null)
    {
        store ??= SyncAccountStore.Default;
        var account = store.Find(serverUrl);
        return account == null
            ? null
            : new SyncConnection(account, new SyncApiClient(account.ServerUrl, http), store);
    }

    /// <summary>Después de un login exitoso: guarda la cuenta y devuelve la conexión.</summary>
    public static SyncConnection Establish(string serverUrl, SyncSession session,
        SyncAccountStore? store = null, HttpClient? http = null)
    {
        store ??= SyncAccountStore.Default;
        var account = new SyncAccount { ServerUrl = serverUrl.TrimEnd('/') };
        account.Apply(session);
        store.Save(account);
        return new SyncConnection(account, new SyncApiClient(account.ServerUrl, http), store);
    }

    /// <summary>Cierra la sesión local. No invalida el refresh token en el server: para eso está
    /// /auth/logout, que se llama aparte porque puede fallar sin red y el olvido local igual tiene
    /// que ocurrir.</summary>
    public void Forget() => _store.Remove(Account.ServerUrl);

    /// <summary>Refresca si está por vencer. Conviene llamarlo antes de una tanda larga de
    /// requests, aunque el 401 igual está cubierto.</summary>
    public async Task EnsureFreshAsync(CancellationToken ct = default)
    {
        if (DateTime.UtcNow + Margen < Account.ExpiresAtUtc) return;
        await RefrescarAsync(ct);
    }

    async Task<string?> RefrescarAsync(CancellationToken ct)
    {
        await _refrescando.WaitAsync(ct);
        try
        {
            // otra llamada pudo haber refrescado mientras esperábamos el semáforo
            if (DateTime.UtcNow + Margen < Account.ExpiresAtUtc) return Account.AccessToken;
            if (string.IsNullOrEmpty(Account.RefreshToken)) return null;

            try
            {
                var session = await Api.RefreshAsync(Account.RefreshToken, ct);
                Account.Apply(session);
                _store.Save(Account);
                return Account.AccessToken;
            }
            catch (SyncApiException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            {
                // el refresh fue revocado o rotado por otra instancia: hay que volver a loguearse,
                // y dejar la cuenta guardada sólo confundiría
                Forget();
                return null;
            }
        }
        finally
        {
            _refrescando.Release();
        }
    }

    public void Dispose()
    {
        Api.Dispose();
        _refrescando.Dispose();
        GC.SuppressFinalize(this);
    }
}
