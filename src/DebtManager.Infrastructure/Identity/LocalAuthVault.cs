using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Infrastructure.Security;

namespace DebtManager.Infrastructure.Identity;

/// <summary>
/// Local auth vault stored as an AES-256-GCM encrypted file.
/// Stores device identity and optional remembered session token.
/// Never stores passwords or raw hashes.
/// </summary>
public sealed class LocalAuthVault
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const byte FormatVersion = 1;

    private readonly byte[] _key;
    private readonly string _filePath;

    public LocalAuthVault(IKeyStore keyStore, string? filePath = null)
    {
        _key = keyStore.GetOrCreateKey();
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption key must be 256 bits (32 bytes).");

        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager", "auth_vault.bin");
    }

    public Guid GetOrCreateDeviceId()
    {
        var data = LoadData();
        if (data.DeviceId == Guid.Empty)
        {
            data.DeviceId = Guid.NewGuid();
            SaveData(data);
        }
        return data.DeviceId;
    }

    public void SaveRememberedSession(Guid sessionId, Guid userId)
    {
        var data = LoadData();
        data.RememberedSessionId = sessionId;
        data.RememberedUserId = userId;
        SaveData(data);
    }

    public (Guid SessionId, Guid UserId)? LoadRememberedSession()
    {
        var data = LoadData();
        if (data.RememberedSessionId == null || data.RememberedUserId == null)
            return null;
        return (data.RememberedSessionId.Value, data.RememberedUserId.Value);
    }

    public void ClearRememberedSession()
    {
        var data = LoadData();
        data.RememberedSessionId = null;
        data.RememberedUserId = null;
        SaveData(data);
    }

    private AuthVaultData LoadData()
    {
        if (!File.Exists(_filePath))
            return new AuthVaultData();

        try
        {
            var fileBytes = File.ReadAllBytes(_filePath);
            if (fileBytes.Length < 1 + NonceSize + TagSize)
                return new AuthVaultData();

            var version = fileBytes[0];
            if (version != FormatVersion)
                return new AuthVaultData();

            var nonce = new byte[NonceSize];
            var ciphertextLen = fileBytes.Length - 1 - NonceSize - TagSize;
            var ciphertext = new byte[ciphertextLen];
            var tag = new byte[TagSize];

            Buffer.BlockCopy(fileBytes, 1, nonce, 0, NonceSize);
            Buffer.BlockCopy(fileBytes, 1 + NonceSize, ciphertext, 0, ciphertextLen);
            Buffer.BlockCopy(fileBytes, 1 + NonceSize + ciphertextLen, tag, 0, TagSize);

            var plaintext = new byte[ciphertextLen];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            var json = Encoding.UTF8.GetString(plaintext);
            return JsonSerializer.Deserialize<AuthVaultData>(json) ?? new AuthVaultData();
        }
        catch
        {
            return new AuthVaultData();
        }
    }

    private void SaveData(AuthVaultData data)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(data);
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Format: [version][nonce][ciphertext][tag]
        var result = new byte[1 + NonceSize + ciphertext.Length + TagSize];
        result[0] = FormatVersion;
        Buffer.BlockCopy(nonce, 0, result, 1, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, 1 + NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, 1 + NonceSize + ciphertext.Length, TagSize);

        File.WriteAllBytes(_filePath, result);
    }

    private sealed class AuthVaultData
    {
        public Guid DeviceId { get; set; }
        public Guid? RememberedSessionId { get; set; }
        public Guid? RememberedUserId { get; set; }
    }
}
