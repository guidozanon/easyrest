using System.Security.Cryptography;
using System.Text;

namespace EasyRest.Sync.Server.Crypto;

/// <summary>Generación y hash de los tokens opacos (sesiones, invitaciones, service tokens).
/// En la base sólo vive el SHA-256: el valor en claro se muestra una única vez.</summary>
public static class Tokens
{
    /// <summary>Token nuevo, 32 bytes de aleatorio en base64url.</summary>
    public static string Create() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>Comparación en tiempo constante de dos hashes.</summary>
    public static bool HashEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>Verificación del PKCE: S256 es el único método que aceptamos (plain está
    /// desaconsejado y no aporta nada acá).</summary>
    public static bool VerifyPkce(string codeChallenge, string codeVerifier)
    {
        if (string.IsNullOrEmpty(codeChallenge) || string.IsNullOrEmpty(codeVerifier)) return false;
        var computed = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(codeChallenge));
    }

    /// <summary>Revisión nueva para un documento: opaca a propósito, para que nadie le arme
    /// lógica encima.</summary>
    public static string NewRev() => Base64Url(RandomNumberGenerator.GetBytes(12));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
