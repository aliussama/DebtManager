using System.IO;
using System.Security.Cryptography;
using DebtManager.Domain.Documents;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Infrastructure.Documents;

public sealed class EncryptedFileDocumentBlobStore : IDocumentBlobStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const byte FormatVersion = 1;

    private readonly string _blobDir;
    private readonly byte[] _key;

    public EncryptedFileDocumentBlobStore(IKeyStore keyStore, string? blobDir = null)
    {
        _key = keyStore.GetOrCreateKey();
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 256 bits (32 bytes).");

        if (blobDir == null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _blobDir = Path.Combine(appData, "DebtManager", "vault_blobs");
        }
        else
        {
            _blobDir = blobDir;
        }

        Directory.CreateDirectory(_blobDir);
    }

    public async Task SaveAsync(string storageKey, byte[] plaintext, CancellationToken ct)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        // Format: [version:1] [nonce:12] [tag:16] [ciphertext:N]
        var result = new byte[1 + NonceSize + TagSize + ciphertext.Length];
        result[0] = FormatVersion;
        Buffer.BlockCopy(nonce, 0, result, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, result, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, result, 1 + NonceSize + TagSize, ciphertext.Length);

        var filePath = GetFilePath(storageKey);
        await File.WriteAllBytesAsync(filePath, result, ct);
    }

    public async Task<byte[]?> LoadAsync(string storageKey, CancellationToken ct)
    {
        var filePath = GetFilePath(storageKey);
        if (!File.Exists(filePath))
            return null;

        var combined = await File.ReadAllBytesAsync(filePath, ct);

        if (combined.Length < 1 + NonceSize + TagSize)
            throw new CryptographicException("Invalid encrypted blob: too short.");

        var version = combined[0];
        if (version != FormatVersion)
            throw new CryptographicException($"Unsupported blob format version: {version}");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertextLength = combined.Length - 1 - NonceSize - TagSize;
        var ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(combined, 1, nonce, 0, NonceSize);
        Buffer.BlockCopy(combined, 1 + NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(combined, 1 + NonceSize + TagSize, ciphertext, 0, ciphertextLength);

        var plaintext = new byte[ciphertextLength];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct)
    {
        return Task.FromResult(File.Exists(GetFilePath(storageKey)));
    }

    public Task PurgeAsync(string storageKey, CancellationToken ct)
    {
        var filePath = GetFilePath(storageKey);
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }

    public static bool VerifyIntegrity(byte[] plaintext, string expectedSha256Hex)
    {
        var hash = SHA256.HashData(plaintext);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(hex, expectedSha256Hex, StringComparison.OrdinalIgnoreCase);
    }

    private string GetFilePath(string storageKey) => Path.Combine(_blobDir, storageKey + ".bin");
}
