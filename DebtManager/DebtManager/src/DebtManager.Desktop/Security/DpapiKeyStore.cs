using DebtManager.Infrastructure.Security;
using System.IO;
using System.Security.Cryptography;

namespace DebtManager.Desktop.Security;

public sealed class DpapiKeyStore : IKeyStore
{
    private readonly string _keyPath;

    public DpapiKeyStore()
    {
        _keyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager",
            "dbkey.bin");
    }

    public byte[] GetOrCreateKey()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);

        if (File.Exists(_keyPath))
        {
            var protectedKey = File.ReadAllBytes(_keyPath);
            return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var protectedBytes = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_keyPath, protectedBytes);
        return key;
    }
}
