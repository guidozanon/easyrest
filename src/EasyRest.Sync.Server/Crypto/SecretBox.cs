using System.Security.Cryptography;
using System.Text;

namespace EasyRest.Sync.Server.Crypto;

/// <summary>Cifrado de los valores secretos con envelope encryption: cada workspace tiene su
/// propia clave de datos (DEK), y esa clave se guarda envuelta con la master key del server.
/// Rotar la master key es re-envolver las DEK, sin tocar los secretos.
///
/// La master key sale de EASYREST_MASTER_KEY (32 bytes en base64) y nunca se persiste.</summary>
public class SecretBox
{
    const int NonceSize = 12;   // AES-GCM
    const int TagSize = 16;
    public const int KeySize = 32;

    readonly byte[] _masterKey;

    public SecretBox(byte[] masterKey)
    {
        if (masterKey.Length != KeySize)
            throw new ArgumentException($"La master key tiene que ser de {KeySize} bytes.", nameof(masterKey));
        _masterKey = masterKey;
    }

    /// <summary>Lee la master key de la config. Devuelve null con un motivo si no sirve: el
    /// server tiene que negarse a arrancar antes que guardar secretos sin cifrar.</summary>
    public static bool TryParseMasterKey(string? value, out byte[] key, out string? error)
    {
        key = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Falta EASYREST_MASTER_KEY. Generá una con: openssl rand -base64 32";
            return false;
        }

        try
        {
            key = Convert.FromBase64String(value.Trim());
        }
        catch (FormatException)
        {
            error = "EASYREST_MASTER_KEY no es base64 válido. Generá una con: openssl rand -base64 32";
            return false;
        }

        if (key.Length != KeySize)
        {
            error = $"EASYREST_MASTER_KEY tiene {key.Length} bytes y tiene que tener {KeySize}. " +
                    "Generá una con: openssl rand -base64 32";
            return false;
        }

        return true;
    }

    /// <summary>Clave de datos nueva para un workspace, ya envuelta con la master key.</summary>
    public byte[] CreateWrappedDataKey()
    {
        var dek = RandomNumberGenerator.GetBytes(KeySize);
        try
        {
            return Wrap(dek);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    byte[] Wrap(byte[] dataKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[dataKey.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Encrypt(nonce, dataKey, cipher, tag);

        // nonce | tag | ciphertext, todo junto: la DEK envuelta es una sola columna
        var result = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, result, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, result, NonceSize + TagSize, cipher.Length);
        return result;
    }

    byte[] Unwrap(byte[] wrapped)
    {
        if (wrapped.Length != NonceSize + TagSize + KeySize)
            throw new CryptographicException("La clave del workspace está corrupta.");

        var nonce = wrapped.AsSpan(0, NonceSize);
        var tag = wrapped.AsSpan(NonceSize, TagSize);
        var cipher = wrapped.AsSpan(NonceSize + TagSize);
        var dek = new byte[KeySize];
        using var aes = new AesGcm(_masterKey, TagSize);
        aes.Decrypt(nonce, cipher, tag, dek);
        return dek;
    }

    /// <summary>Cifra un valor con la clave del workspace. El id del documento y la clave de la
    /// variable van como associated data: un ciphertext no se puede mover de un ambiente a otro
    /// ni de una variable a otra sin que falle el descifrado.</summary>
    public (byte[] Nonce, byte[] Ciphertext, byte[] Tag) Seal(
        byte[] wrappedDataKey, Guid documentId, string key, string plaintext)
    {
        var dek = Unwrap(wrappedDataKey);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var plain = Encoding.UTF8.GetBytes(plaintext);
            var cipher = new byte[plain.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(dek, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(documentId, key));
            return (nonce, cipher, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public string Open(byte[] wrappedDataKey, Guid documentId, string key,
        byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var dek = Unwrap(wrappedDataKey);
        try
        {
            var plain = new byte[ciphertext.Length];
            using var aes = new AesGcm(dek, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plain, AssociatedData(documentId, key));
            return Encoding.UTF8.GetString(plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    static byte[] AssociatedData(Guid documentId, string key) =>
        Encoding.UTF8.GetBytes($"{documentId:N}/{key}");
}
