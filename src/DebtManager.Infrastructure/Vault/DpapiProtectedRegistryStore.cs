using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Domain.Vault;

namespace DebtManager.Infrastructure.Vault;

/// <summary>
/// Stores the vault registry manifest encrypted with DPAPI (CurrentUser scope).
/// No plaintext vault names or paths are ever written to disk.
/// </summary>
public sealed class DpapiProtectedRegistryStore
{
    private const byte FormatVersion = 1;
    private readonly string _filePath;

    public DpapiProtectedRegistryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager", "registry.vaults.dpapi");
    }

    public string FilePath => _filePath;

    public async Task SaveAsync(VaultManifest manifest)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = false });
        var plaintext = Encoding.UTF8.GetBytes(json);
        var protectedBytes = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

        var result = new byte[1 + protectedBytes.Length];
        result[0] = FormatVersion;
        Buffer.BlockCopy(protectedBytes, 0, result, 1, protectedBytes.Length);

        await File.WriteAllBytesAsync(_filePath, result);
    }

    public async Task<VaultManifest> LoadAsync()
    {
        if (!File.Exists(_filePath))
            return new VaultManifest();

        try
        {
            var fileBytes = await File.ReadAllBytesAsync(_filePath);
            if (fileBytes.Length < 2)
                return new VaultManifest();

            var version = fileBytes[0];
            if (version != FormatVersion)
                return new VaultManifest();

            var protectedBytes = new byte[fileBytes.Length - 1];
            Buffer.BlockCopy(fileBytes, 1, protectedBytes, 0, protectedBytes.Length);

            var plaintext = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plaintext);
            return JsonSerializer.Deserialize<VaultManifest>(json) ?? new VaultManifest();
        }
        catch
        {
            return new VaultManifest();
        }
    }
}
