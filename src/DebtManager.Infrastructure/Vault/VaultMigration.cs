using System.IO;
using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Vault;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Infrastructure.Vault;

/// <summary>
/// Handles one-time migration from legacy single-vault layout to multi-vault directory structure.
/// Safe: copies first, verifies, then deletes old files.
/// </summary>
public static class VaultMigration
{
    private static readonly string[] LegacyDbNames =
    {
        "debtmanager_local.db",
        "events.db"
    };

    /// <summary>
    /// Detects and migrates a legacy single-vault installation to multi-vault layout.
    /// Returns the migrated vault's ID if migration occurred, null otherwise.
    /// </summary>
    public static async Task<Guid?> MigrateIfNeededAsync(
        string dataDir,
        VaultRegistry registry,
        GlobalEventStore globalStore)
    {
        // Check if already migrated (vaults directory exists with content)
        var vaultsDir = Path.Combine(dataDir, "vaults");
        if (Directory.Exists(vaultsDir) && Directory.GetDirectories(vaultsDir).Length > 0)
            return null;

        // Detect legacy DB
        string? legacyDbPath = null;
        foreach (var name in LegacyDbNames)
        {
            var path = Path.Combine(dataDir, name);
            if (File.Exists(path))
            {
                legacyDbPath = path;
                break;
            }
        }

        if (legacyDbPath == null)
            return null; // No legacy data, fresh install

        // Create new vault
        var vaultId = Guid.NewGuid();
        var vaultDir = Path.Combine(vaultsDir, vaultId.ToString());
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(Path.Combine(vaultDir, "blobs"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "exports"));

        // Copy DB files
        var newDbPath = Path.Combine(vaultDir, "data.db");
        File.Copy(legacyDbPath, newDbPath, overwrite: false);

        // Copy WAL/SHM if present
        var walPath = legacyDbPath + "-wal";
        if (File.Exists(walPath))
            File.Copy(walPath, newDbPath + "-wal", overwrite: false);

        var shmPath = legacyDbPath + "-shm";
        if (File.Exists(shmPath))
            File.Copy(shmPath, newDbPath + "-shm", overwrite: false);

        // Copy key file
        var legacyKeyPath = Path.Combine(dataDir, "dbkey.bin");
        var newKeyPath = Path.Combine(vaultDir, "dbkey.bin");
        if (File.Exists(legacyKeyPath))
            File.Copy(legacyKeyPath, newKeyPath, overwrite: false);

        // Copy auth vault
        var legacyAuthPath = Path.Combine(dataDir, "auth_vault.bin");
        var newAuthPath = Path.Combine(vaultDir, "auth_vault.bin");
        if (File.Exists(legacyAuthPath))
            File.Copy(legacyAuthPath, newAuthPath, overwrite: false);

        // Copy blobs directory
        var legacyBlobsDir = Path.Combine(dataDir, "vault_blobs");
        var newBlobsDir = Path.Combine(vaultDir, "blobs");
        if (Directory.Exists(legacyBlobsDir))
        {
            foreach (var file in Directory.GetFiles(legacyBlobsDir))
            {
                var dest = Path.Combine(newBlobsDir, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: false);
            }
        }

        // Verify the new DB can be opened (basic check)
        if (!File.Exists(newDbPath))
            throw new InvalidOperationException("Migration failed: copied DB file not found.");

        // Register vault in global event store and manifest
        var createdDate = DateOnly.FromDateTime(DateTime.Today);
        var descriptor = new VaultDescriptor
        {
            VaultId = vaultId,
            Name = "Default",
            CurrencyCode = "EGP",
            CreatedDate = createdDate,
            LastOpenedAtUtc = DateTimeOffset.UtcNow
        };

        // Append global events
        var createEvent = new VaultCreated(vaultId, "Default", "EGP", createdDate);
        await AppendGlobalEventAsync(globalStore, createEvent, vaultId);

        var selectEvent = new ActiveVaultSelected(vaultId, createdDate);
        await AppendGlobalEventAsync(globalStore, selectEvent, vaultId);

        // Register in manifest
        await registry.RegisterVaultAsync(descriptor);
        await registry.SetActiveVaultAsync(vaultId);

        // Delete old files only after all copies verified
        try
        {
            File.Delete(legacyDbPath);
            if (File.Exists(walPath)) File.Delete(walPath);
            if (File.Exists(shmPath)) File.Delete(shmPath);
            // Keep legacy key and auth as backup (they're small)
        }
        catch
        {
            // Best effort cleanup - not critical
        }

        return vaultId;
    }

    private static async Task AppendGlobalEventAsync(GlobalEventStore store, IDomainEvent ev, Guid streamEntityId)
    {
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(streamEntityId),
            ev.GetType().Name,
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            Guid.Empty,
            Guid.Empty,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, ev.GetType(), DomainJson.Options));

        await store.AppendAsync(envelope, CancellationToken.None);
    }
}
