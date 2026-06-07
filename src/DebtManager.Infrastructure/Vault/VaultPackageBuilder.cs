using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DebtManager.Infrastructure.Vault;

/// <summary>
/// Creates and validates .dmvault packages for export/import.
/// A package is a ZIP containing events.db, optional snapshots/blobs, and a manifest.json.
/// Import always targets a NEW folder — never overwrites in-place.
/// </summary>
public sealed class VaultPackageBuilder
{
    public sealed class PackageManifest
    {
        public DateTime CreatedAtUtc { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public int SchemaVersion { get; set; } = 1;
        public int EventCount { get; set; }
        public Guid? VaultId { get; set; }
        public List<PackageFileEntry> Files { get; set; } = new();
    }

    public sealed class PackageFileEntry
    {
        public string EntryName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Create an export package (.dmvault) from the given database path.
    /// </summary>
    public static void CreatePackage(string dbPath, string destinationPath, int eventCount = 0, Guid? vaultId = null)
    {
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        var manifest = new PackageManifest
        {
            CreatedAtUtc = DateTime.UtcNow,
            AppVersion = GetAppVersion(),
            EventCount = eventCount,
            VaultId = vaultId,
            Files = new List<PackageFileEntry>()
        };

        using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);

        // Add main DB
        if (File.Exists(dbPath))
            AddFileToPackage(zip, manifest, dbPath, "events.db");

        // Add WAL if present
        var walPath = dbPath + "-wal";
        if (File.Exists(walPath))
            AddFileToPackage(zip, manifest, walPath, "events.db-wal");

        // Add SHM if present
        var shmPath = dbPath + "-shm";
        if (File.Exists(shmPath))
            AddFileToPackage(zip, manifest, shmPath, "events.db-shm");

        // Write manifest
        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var sw = new StreamWriter(manifestEntry.Open());
        sw.Write(manifestJson);
    }

    /// <summary>
    /// Validate a .dmvault package: check manifest, hash integrity.
    /// Returns the manifest on success, throws on failure.
    /// </summary>
    public static PackageManifest ValidatePackage(string packagePath)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Package not found", packagePath);

        using var zip = ZipFile.OpenRead(packagePath);

        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Invalid package: missing manifest.json");

        using var sr = new StreamReader(manifestEntry.Open());
        var manifestJson = sr.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<PackageManifest>(manifestJson)
            ?? throw new InvalidDataException("Invalid package: corrupt manifest");

        // Validate file hashes
        foreach (var fileEntry in manifest.Files)
        {
            var zipEntry = zip.GetEntry(fileEntry.EntryName);
            if (zipEntry == null)
                throw new InvalidDataException($"Invalid package: missing file '{fileEntry.EntryName}'");

            using var stream = zipEntry.Open();
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(stream));

            if (!string.Equals(hash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid package: hash mismatch for '{fileEntry.EntryName}'");
        }

        return manifest;
    }

    /// <summary>
    /// Extract a validated package into a NEW target directory.
    /// </summary>
    public static string ExtractToNewVault(string packagePath, string baseDataDir)
    {
        var targetDir = Path.Combine(baseDataDir, $"vault_import_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(targetDir);

        using var zip = ZipFile.OpenRead(packagePath);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name == "manifest.json") continue;
            var targetPath = Path.Combine(targetDir, entry.Name);
            entry.ExtractToFile(targetPath, overwrite: false);
        }

        return targetDir;
    }

    /// <summary>
    /// Extract a validated package into a proper vault directory structure.
    /// Returns the new vault ID and directory path.
    /// </summary>
    public static (Guid VaultId, string VaultDir) ExtractToVaultDirectory(string packagePath, string vaultsRoot)
    {
        var vaultId = Guid.NewGuid();
        var vaultDir = Path.Combine(vaultsRoot, vaultId.ToString());
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(Path.Combine(vaultDir, "blobs"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "exports"));

        using var zip = ZipFile.OpenRead(packagePath);
        foreach (var entry in zip.Entries)
        {
            if (entry.Name == "manifest.json") continue;

            // Map events.db -> data.db for new vault layout
            var fileName = entry.Name switch
            {
                "events.db" => "data.db",
                "events.db-wal" => "data.db-wal",
                "events.db-shm" => "data.db-shm",
                _ => entry.Name
            };

            var targetPath = Path.Combine(vaultDir, fileName);
            entry.ExtractToFile(targetPath, overwrite: false);
        }

        return (vaultId, vaultDir);
    }

    private static void AddFileToPackage(ZipArchive zip, PackageManifest manifest, string filePath, string entryName)
    {
        zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Optimal);
        var fi = new FileInfo(filePath);
        manifest.Files.Add(new PackageFileEntry
        {
            EntryName = entryName,
            Sha256 = ComputeSha256(filePath),
            SizeBytes = fi.Length
        });
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string GetAppVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? asm.GetName().Version?.ToString()
            ?? "1.0.0";
    }
}
