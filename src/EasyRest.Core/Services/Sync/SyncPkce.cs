using System.Security.Cryptography;
using System.Text;

namespace EasyRest.Services.Sync;

/// <summary>El par verifier/challenge de PKCE, que es lo que permite ser un cliente público —una
/// app de escritorio o de teléfono— sin llevar un client secret adentro del binario.
///
/// El challenge viaja al abrir el navegador; el verifier se queda en la app y recién se manda al
/// canjear el código. Quien intercepte el código no puede usarlo sin el verifier.</summary>
public record SyncPkce(string Verifier, string Challenge, string State)
{
    /// <summary>El server sólo acepta S256, y compara contra base64url sin padding: esto tiene que
    /// dar exactamente lo mismo que <c>Tokens.VerifyPkce</c> del server.</summary>
    public static SyncPkce Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        // el state ata la respuesta del navegador a este intento y nada más
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        return new SyncPkce(verifier, challenge, state);
    }

    static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
