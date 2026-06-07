using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Desktop.Services;

/// <summary>
/// Production implementation of vault backup / restore.
/// Creates a ZIP-based .dmvault archive containing the SQLite database,
/// associated WAL/SHM files, and a manifest with integrity hashes.
/// </summary>
public sealed class VaultBackupService : IVaultBackupService
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFileDialogService _fileDialog;
    private readonly IToastService? _toastService;
    private readonly IAppReloadService? _reloadService;

    /// <summary>
    /// When true, the next RestoreAsync call will simulate extraction failure
    /// (used for testing rollback behavior).
    /// </summary>
    internal bool InjectRestoreFault { get; set; }

    public VaultBackupService(
        string dbPath,
        SqliteConnectionFactory connectionFactory,
        IFileDialogService fileDialog,
        IToastService? toastService = null,
        IAppReloadService? reloadService = null)
    {
        _dbPath = dbPath;
        _connectionFactory = connectionFactory;
        _fileDialog = fileDialog;
        _toastService = toastService;
        _reloadService = reloadService;
    }

    public async Task BackupAsync(CancellationToken ct)
    {
        var defaultName = $"DebtManagerBackup_{DateTime.Now:yyyyMMdd_HHmm}.dmvault";
        var filter = "DebtManager Vault (*.dmvault)|*.dmvault|All Files (*.*)|*.*";

        var savePath = _fileDialog.ShowSaveFileDialog(defaultName, filter, "Save Vault Backup");
        if (string.IsNullOrEmpty(savePath))
            return; // user cancelled

        try
        {
            await Task.Run(() => CreateBackup(savePath), ct);
            _toastService?.Success($"Backup created: {Path.GetFileName(savePath)}");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Backup failed", ex);
            throw;
        }
    }

    public string? PickRestoreFile()
    {
        var filter = "DebtManager Vault (*.dmvault)|*.dmvault|All Files (*.*)|*.*";
        return _fileDialog.ShowOpenFileDialog("Select Vault Backup", filter);
    }

    public async Task RestoreAsync(string dmvaultPath, CancellationToken ct)
    {
        try
        {
            await Task.Run(() => ExecuteRestore(dmvaultPath), ct);
            _reloadService?.RequestReload();
            _toastService?.Success("Vault restored successfully");
        }
        catch (Exception ex)
        {
            _toastService?.Error("Restore failed", ex);
            throw;
        }
    }

    // ---- internal helpers ----

    internal void CreateBackup(string destinationPath)
    {
        // 1) WAL checkpoint to merge journal into main DB
        RunWalCheckpoint();

        // 2) Build manifest + hashes
        var manifest = new BackupManifest
        {
            CreatedAtUtc = DateTime.UtcNow,
            AppVersion = GetAppVersion(),
            SchemaVersion = 1,
            Files = new List<BackupFileEntry>()
        };

        var dbFiles = GetDatabaseFiles();

        foreach (var file in dbFiles)
        {
            manifest.Files.Add(new BackupFileEntry
            {
                EntryName = "vault" + GetSuffix(file),
                Sha256 = ComputeSha256(file)
            });
        }

        // 3) Write ZIP
        if (File.Exists(destinationPath))
            File.Delete(destinationPath);

        using var zip = ZipFile.Open(destinationPath, ZipArchiveMode.Create);

        foreach (var file in dbFiles)
        {
            var entryName = "vault" + GetSuffix(file);
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }

        var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using var sw = new StreamWriter(manifestEntry.Open());
        sw.Write(manifestJson);
    }

    internal void ExecuteRestore(string dmvaultPath)
    {
        // 1) Validate archive
        ValidateArchive(dmvaultPath);

        // 2) WAL checkpoint before closing
        RunWalCheckpoint();

        // 3) Create safety backup of current files
        var backupDir = CreateSafetyBackup();

        try
        {
            // 4) Delete current DB files
            DeleteDatabaseFiles();

            // 5) Check for injected fault (testing)
            if (InjectRestoreFault)
                throw new IOException("Injected restore fault for testing");

            // 6) Extract from archive
            ExtractVaultFiles(dmvaultPath);

            // 7) Integrity check on restored DB
            RunIntegrityCheck();
        }
        catch
        {
            // Rollback: restore from safety backup
            try
            {
                RollbackFromSafetyBackup(backupDir);
            }
            catch (Exception rollbackEx)
            {
                throw new AggregateException(
                    "Restore failed AND rollback failed. Manual recovery needed from: " + backupDir,
                    rollbackEx);
            }

            throw;
        }

        // 8) Cleanup safety backup (restore succeeded)
        CleanupSafetyBackup(backupDir);
    }

    private void ValidateArchive(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Backup file not found", path);

        using var zip = ZipFile.OpenRead(path);

        // Must have manifest.json
        var manifestEntry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Invalid backup: missing manifest.json");

        // Must have vault.db
        _ = zip.GetEntry("vault.db")
            ?? throw new InvalidDataException("Invalid backup: missing vault.db");

        // Parse and validate manifest
        using var sr = new StreamReader(manifestEntry.Open());
        var manifestJson = sr.ReadToEnd();
        var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson)
            ?? throw new InvalidDataException("Invalid backup: corrupt manifest");

        // Validate hashes
        foreach (var fileEntry in manifest.Files)
        {
            var zipEntry = zip.GetEntry(fileEntry.EntryName);
            if (zipEntry == null) continue;

            using var stream = zipEntry.Open();
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(stream));

            if (!string.Equals(hash, fileEntry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Invalid backup: hash mismatch for {fileEntry.EntryName}");
        }
    }

    private void ExtractVaultFiles(string dmvaultPath)
    {
        using var zip = ZipFile.OpenRead(dmvaultPath);
        var dbDir = Path.GetDirectoryName(_dbPath)!;

        foreach (var entry in zip.Entries)
        {
            if (entry.Name == "manifest.json") continue;

            var targetPath = entry.Name switch
            {
                "vault.db" => _dbPath,
                "vault.db-wal" => _dbPath + "-wal",
                "vault.db-shm" => _dbPath + "-shm",
                _ => null
            };

            if (targetPath == null) continue;

            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private void RunIntegrityCheck()
    {
        using var conn = _connectionFactory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var result = cmd.ExecuteScalar()?.ToString();
        conn.Close();

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Restored database failed integrity check: {result}");
    }

    private void RunWalCheckpoint()
    {
        if (!File.Exists(_dbPath)) return;

        try
        {
            using var conn = _connectionFactory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
            conn.Close();
        }
        catch
        {
            // Best-effort; backup will still include WAL files
        }
    }

    private string CreateSafetyBackup()
    {
        var appDir = Path.GetDirectoryName(_dbPath)!;
        var backupDir = Path.Combine(appDir, "Backups", $"restore_backup_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(backupDir);

        foreach (var file in GetDatabaseFiles())
        {
            var dest = Path.Combine(backupDir, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
        }

        return backupDir;
    }

    private void RollbackFromSafetyBackup(string backupDir)
    {
        DeleteDatabaseFiles();

        var dbFileName = Path.GetFileName(_dbPath);
        foreach (var file in Directory.GetFiles(backupDir))
        {
            var fileName = Path.GetFileName(file);
            var targetDir = Path.GetDirectoryName(_dbPath)!;
            var destPath = Path.Combine(targetDir, fileName);
            File.Copy(file, destPath, overwrite: true);
        }
    }

    private void DeleteDatabaseFiles()
    {
        var walPath = _dbPath + "-wal";
        var shmPath = _dbPath + "-shm";

        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (File.Exists(walPath)) File.Delete(walPath);
        if (File.Exists(shmPath)) File.Delete(shmPath);
    }

    private void CleanupSafetyBackup(string backupDir)
    {
        try
        {
            Directory.Delete(backupDir, recursive: true);
        }
        catch
        {
            // Non-critical; old backup files remain
        }
    }

    private List<string> GetDatabaseFiles()
    {
        var files = new List<string>();
        if (File.Exists(_dbPath)) files.Add(_dbPath);
        if (File.Exists(_dbPath + "-wal")) files.Add(_dbPath + "-wal");
        if (File.Exists(_dbPath + "-shm")) files.Add(_dbPath + "-shm");
        return files;
    }

    private static string GetSuffix(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var dbName = Path.GetFileNameWithoutExtension(name.Split('-')[0]);
        var ext = name.Contains('-') ? "-" + name.Split('-', 2)[1] : Path.GetExtension(name);

        // Return ".db", ".db-wal", ".db-shm"
        if (name.EndsWith("-wal")) return ".db-wal";
        if (name.EndsWith("-shm")) return ".db-shm";
        return ".db";
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

    // ---- manifest model ----

    internal sealed class BackupManifest
    {
        public DateTime CreatedAtUtc { get; set; }
        public string AppVersion { get; set; } = string.Empty;
        public int SchemaVersion { get; set; }
        public List<BackupFileEntry> Files { get; set; } = new();
    }

    internal sealed class BackupFileEntry
    {
        public string EntryName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}
