using System.Security.Cryptography;
using System.Text;

namespace DebtManager.Infrastructure.Security;

/// <summary>
/// Provides encryption for sensitive data in transit and at rest.
/// Uses AES-256-GCM for authenticated encryption.
/// </summary>
public sealed class PayloadEncryptor
{
    private const int NonceSize = 12; // 96 bits for GCM
    private const int TagSize = 16;   // 128 bits for GCM tag

    private readonly byte[] _key;

    public PayloadEncryptor(IKeyStore keyStore)
    {
        _key = keyStore.GetOrCreateKey();
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 256 bits (32 bytes).");
    }

    /// <summary>
    /// Encrypt plaintext using AES-256-GCM.
    /// Returns: nonce (12 bytes) + ciphertext + tag (16 bytes), base64 encoded.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            return plaintext;

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine: nonce + ciphertext + tag
        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypt ciphertext that was encrypted with Encrypt().
    /// </summary>
    public string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return encryptedBase64;

        var combined = Convert.FromBase64String(encryptedBase64);

        if (combined.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted data: too short.");

        var nonce = new byte[NonceSize];
        var ciphertextLength = combined.Length - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];
        var tag = new byte[TagSize];

        Buffer.BlockCopy(combined, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(combined, NonceSize, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(combined, NonceSize + ciphertextLength, tag, 0, TagSize);

        var plaintext = new byte[ciphertextLength];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// Hash a value for secure comparison (e.g., API keys).
    /// Uses SHA-256 with salt.
    /// </summary>
    public static string HashForComparison(string value, string salt)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(salt + value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Constant-time comparison to prevent timing attacks.
    /// </summary>
    public static bool SecureEquals(string a, string b)
    {
        if (a == null || b == null)
            return a == b;

        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
