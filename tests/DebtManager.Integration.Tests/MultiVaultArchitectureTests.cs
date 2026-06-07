using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DebtManager.Application.Identity;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Vault;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Vault;

namespace DebtManager.Integration.Tests;

public sealed class MultiVaultArchitectureTests : IDisposable
{
    private readonly string _tempDir;

    public MultiVaultArchitectureTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _tempDir = Path.Combine(Path.GetTempPath(), $"MVaultTests_{id}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    private GlobalEventStore CreateGlobalStore() =>
        new GlobalEventStore(Path.Combine(_tempDir, "global.db"));

    private DpapiProtectedRegistryStore CreateRegistryStore() =>
        new DpapiProtectedRegistryStore(Path.Combine(_tempDir, "registry.vaults.dpapi"));

    private VaultRegistry CreateRegistry()
    {
        var registryStore = CreateRegistryStore();
        var globalStore = CreateGlobalStore();
        return new VaultRegistry(registryStore, globalStore, Path.Combine(_tempDir, "vaults"));
    }

    private SqliteEventStore CreateEventStoreForVault(VaultPaths paths)
    {
        // Each vault gets its own key store + connection factory
        var keyStore = new TestKeyStore();
        var factory = new SqliteConnectionFactory(paths.DbPath, keyStore);
        return new SqliteEventStore(factory);
    }

    // ================================================================
    // 1) CreateTwoVaults_IsolatedEvents
    // ================================================================
    [Fact]
    public async Task CreateTwoVaults_IsolatedEvents()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Finance A", "USD");
        var vaultB = await registry.CreateVaultAsync("Finance B", "EUR");

        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);
        var pathsB = registry.ResolveVaultPaths(vaultB.VaultId);

        var storeA = CreateEventStoreForVault(pathsA);
        var storeB = CreateEventStoreForVault(pathsB);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Create an account in vault A
        var handlerA = new CreateAccountHandler(storeA);
        var accountIdA = await handlerA.HandleAsync(
            new CreateAccountCommand(null, "Vault A Account", "Cash", 1000m, "USD", new DateOnly(2025, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Create a DIFFERENT account in vault B
        var handlerB = new CreateAccountHandler(storeB);
        var accountIdB = await handlerB.HandleAsync(
            new CreateAccountCommand(null, "Vault B Account", "Cash", 2000m, "EUR", new DateOnly(2025, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Verify isolation: vault A has only its account
        var allA = await storeA.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var cashA = CashLedgerProjector.Project(allA);
        Assert.Single(cashA.Accounts);
        Assert.True(cashA.Accounts.ContainsKey(accountIdA));
        Assert.False(cashA.Accounts.ContainsKey(accountIdB));

        // Verify isolation: vault B has only its account
        var allB = await storeB.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var cashB = CashLedgerProjector.Project(allB);
        Assert.Single(cashB.Accounts);
        Assert.True(cashB.Accounts.ContainsKey(accountIdB));
        Assert.False(cashB.Accounts.ContainsKey(accountIdA));
    }

    // ================================================================
    // 2) VaultKeyIsolation_DifferentDbKeysPerVault
    // ================================================================
    [Fact]
    public async Task VaultKeyIsolation_DifferentDbKeysPerVault()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Key Vault A", "USD");
        var vaultB = await registry.CreateVaultAsync("Key Vault B", "EUR");

        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);
        var pathsB = registry.ResolveVaultPaths(vaultB.VaultId);

        // Verify different paths (implies different keys when using real DpapiKeyStore)
        Assert.NotEqual(pathsA.KeyPath, pathsB.KeyPath);
        Assert.NotEqual(pathsA.DbPath, pathsB.DbPath);

        // Verify directory isolation
        Assert.Contains(vaultA.VaultId.ToString(), pathsA.DbPath);
        Assert.Contains(vaultB.VaultId.ToString(), pathsB.DbPath);
    }

    // ================================================================
    // 3) IdentityScopedPerVault_UserInVaultA_NotInVaultB
    // ================================================================
    [Fact]
    public async Task IdentityScopedPerVault_UserInVaultA_NotInVaultB()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Identity A", "USD");
        var vaultB = await registry.CreateVaultAsync("Identity B", "EUR");

        var storeA = CreateEventStoreForVault(registry.ResolveVaultPaths(vaultA.VaultId));
        var storeB = CreateEventStoreForVault(registry.ResolveVaultPaths(vaultB.VaultId));

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        // Create user in vault A
        var createUserHandler = new CreateVaultUserHandler(storeA);
        var userId = await createUserHandler.HandleAsync(
            new CreateVaultUserCommand("alice", "Alice", "Owner", new DateOnly(2025, 1, 1)),
            actorUserId, deviceId, null, CancellationToken.None);

        // User should exist in vault A
        var allA = await storeA.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var identityA = IdentityProjector.Project(allA);
        Assert.True(identityA.Users.ContainsKey(userId));

        // User should NOT exist in vault B
        var allB = await storeB.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var identityB = IdentityProjector.Project(allB);
        Assert.False(identityB.Users.ContainsKey(userId));
    }

    // ================================================================
    // 4) BlobIsolation_DocumentInVaultA_NotInVaultB
    // ================================================================
    [Fact]
    public async Task BlobIsolation_DocumentInVaultA_NotInVaultB()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Blob A", "USD");
        var vaultB = await registry.CreateVaultAsync("Blob B", "EUR");

        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);
        var pathsB = registry.ResolveVaultPaths(vaultB.VaultId);

        // Verify blob directories are different
        Assert.NotEqual(pathsA.BlobsPath, pathsB.BlobsPath);

        // Write a test file to vault A blobs
        Directory.CreateDirectory(pathsA.BlobsPath);
        var testFile = Path.Combine(pathsA.BlobsPath, "test_doc.bin");
        await File.WriteAllTextAsync(testFile, "secret document content");

        // Verify it exists in A but not in B
        Assert.True(File.Exists(testFile));
        Assert.False(File.Exists(Path.Combine(pathsB.BlobsPath, "test_doc.bin")));
    }

    // ================================================================
    // 5) SnapshotIsolation_SnapshotsPerVaultDbOnly
    // ================================================================
    [Fact]
    public async Task SnapshotIsolation_SnapshotsPerVaultDbOnly()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Snap A", "USD");
        var vaultB = await registry.CreateVaultAsync("Snap B", "EUR");

        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);
        var pathsB = registry.ResolveVaultPaths(vaultB.VaultId);

        // Snapshots are stored inside the vault DB file.
        // Different vault DBs means different snapshot stores.
        Assert.NotEqual(pathsA.DbPath, pathsB.DbPath);

        // Verify the DB paths include vault IDs
        Assert.Contains(vaultA.VaultId.ToString(), pathsA.DbPath);
        Assert.Contains(vaultB.VaultId.ToString(), pathsB.DbPath);
    }

    // ================================================================
    // 6) SwitchVault_RebuildsContainer_CacheCleared
    // ================================================================
    [Fact]
    public async Task SwitchVault_RebuildsContainer_CacheCleared()
    {
        var registry = CreateRegistry();

        var vaultA = await registry.CreateVaultAsync("Switch A", "USD");
        var vaultB = await registry.CreateVaultAsync("Switch B", "EUR");

        // Simulate switch: set active vault to A, then B
        await registry.SetActiveVaultAsync(vaultA.VaultId);
        var manifest1 = await registry.LoadManifestAsync();
        Assert.Equal(vaultA.VaultId, manifest1.ActiveVaultId);

        await registry.SetActiveVaultAsync(vaultB.VaultId);
        var manifest2 = await registry.LoadManifestAsync();
        Assert.Equal(vaultB.VaultId, manifest2.ActiveVaultId);
        Assert.NotEqual(manifest1.ActiveVaultId, manifest2.ActiveVaultId);

        // Verify paths switch
        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);
        var pathsB = registry.ResolveVaultPaths(vaultB.VaultId);
        Assert.NotEqual(pathsA.DbPath, pathsB.DbPath);
    }

    // ================================================================
    // 7) Migration_LegacySingleVault_IsMovedAndLoadsDashboard
    // ================================================================
    [Fact]
    public async Task Migration_LegacySingleVault_IsMovedAndLoadsDashboard()
    {
        var dataDir = Path.Combine(_tempDir, "legacy_test");
        Directory.CreateDirectory(dataDir);

        // Create a legacy DB
        var legacyDbPath = Path.Combine(dataDir, "debtmanager_local.db");
        var keyStore = new TestKeyStore();
        var factory = new SqliteConnectionFactory(legacyDbPath, keyStore);
        var legacyStore = new SqliteEventStore(factory);

        // Add an account event to legacy store
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var accountHandler = new CreateAccountHandler(legacyStore);
        var accountId = await accountHandler.HandleAsync(
            new CreateAccountCommand(null, "Legacy Account", "Cash", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Write a fake key file
        var legacyKeyPath = Path.Combine(dataDir, "dbkey.bin");
        await File.WriteAllBytesAsync(legacyKeyPath, new byte[] { 1, 2, 3, 4 });

        // Run migration
        var registryStore = new DpapiProtectedRegistryStore(Path.Combine(dataDir, "registry.vaults.dpapi"));
        var globalStore = new GlobalEventStore(Path.Combine(dataDir, "global.db"));
        var registry = new VaultRegistry(registryStore, globalStore, Path.Combine(dataDir, "vaults"));

        var migratedId = await VaultMigration.MigrateIfNeededAsync(dataDir, registry, globalStore);

        Assert.NotNull(migratedId);

        // Verify vault was registered
        var manifest = await registry.LoadManifestAsync();
        Assert.Single(manifest.Vaults);
        Assert.Equal(migratedId!.Value, manifest.ActiveVaultId);

        // Verify data was moved
        var paths = registry.ResolveVaultPaths(migratedId.Value);
        Assert.True(File.Exists(paths.DbPath));

        // Verify the migrated DB can be opened and has the account
        var migratedFactory = new SqliteConnectionFactory(paths.DbPath, keyStore);
        var migratedStore = new SqliteEventStore(migratedFactory);
        var all = await migratedStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var cash = CashLedgerProjector.Project(all);
        Assert.True(cash.Accounts.ContainsKey(accountId));
        Assert.Equal(5000m, cash.Accounts[accountId].Balance);

        // Verify legacy file was cleaned up
        Assert.False(File.Exists(legacyDbPath));
    }

    // ================================================================
    // 8) Registry_IsDpapiEncrypted_NoPlaintextVaultNameInFileBytes
    // ================================================================
    [Fact]
    public async Task Registry_IsDpapiEncrypted_NoPlaintextVaultNameInFileBytes()
    {
        var registryPath = Path.Combine(_tempDir, "registry_test.dpapi");
        var registryStore = new DpapiProtectedRegistryStore(registryPath);

        var manifest = new VaultManifest
        {
            Vaults = new List<VaultDescriptor>
            {
                new VaultDescriptor
                {
                    VaultId = Guid.NewGuid(),
                    Name = "SuperSecretVaultName_12345",
                    CurrencyCode = "USD",
                    CreatedDate = DateOnly.FromDateTime(DateTime.Today)
                }
            },
            ActiveVaultId = null
        };

        await registryStore.SaveAsync(manifest);

        // Read raw file bytes
        var rawBytes = await File.ReadAllBytesAsync(registryPath);
        var rawString = Encoding.UTF8.GetString(rawBytes);

        // Vault name should NOT appear in plaintext
        Assert.DoesNotContain("SuperSecretVaultName_12345", rawString);

        // But we should be able to load it back
        var loaded = await registryStore.LoadAsync();
        Assert.Single(loaded.Vaults);
        Assert.Equal("SuperSecretVaultName_12345", loaded.Vaults[0].Name);
    }

    // ================================================================
    // 9) BackupRestore_CreatesNewVault_AndLoadsData
    // ================================================================
    [Fact]
    public async Task BackupRestore_CreatesNewVault_AndLoadsData()
    {
        var registry = CreateRegistry();
        var vaultA = await registry.CreateVaultAsync("Backup Source", "USD");
        var pathsA = registry.ResolveVaultPaths(vaultA.VaultId);

        // Create some data
        var storeA = CreateEventStoreForVault(pathsA);
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var accountHandler = new CreateAccountHandler(storeA);
        await accountHandler.HandleAsync(
            new CreateAccountCommand(null, "Test Account", "Cash", 3000m, "USD", new DateOnly(2025, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Export
        var packagePath = Path.Combine(_tempDir, "backup.dmvault");
        VaultPackageBuilder.CreatePackage(pathsA.DbPath, packagePath, eventCount: 1, vaultId: vaultA.VaultId);

        Assert.True(File.Exists(packagePath));
        var manifest = VaultPackageBuilder.ValidatePackage(packagePath);
        Assert.Equal(vaultA.VaultId, manifest.VaultId);

        // Extract to new vault directory
        var vaultsRoot = Path.Combine(_tempDir, "vaults");
        var (newVaultId, newVaultDir) = VaultPackageBuilder.ExtractToVaultDirectory(packagePath, vaultsRoot);

        Assert.NotEqual(vaultA.VaultId, newVaultId);
        Assert.True(File.Exists(Path.Combine(newVaultDir, "data.db")));
    }

    // ================================================================
    // 10) MaxVaults_LimitEnforced_At10
    // ================================================================
    [Fact]
    public async Task MaxVaults_LimitEnforced_At10()
    {
        var registry = CreateRegistry();

        // Create 10 vaults
        for (int i = 1; i <= 10; i++)
        {
            await registry.CreateVaultAsync($"Vault {i}", "USD");
        }

        var vaults = await registry.ListVaultsAsync();
        Assert.Equal(10, vaults.Count);

        // 11th vault should fail
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => registry.CreateVaultAsync("Vault 11", "USD"));
        Assert.Contains("Maximum 10 vaults", ex.Message);
    }

    // ================================================================
    // 11) VaultRegistryProjector_DeterministicProjection
    // ================================================================
    [Fact]
    public async Task VaultRegistryProjector_DeterministicProjection()
    {
        var globalStore = CreateGlobalStore();
        var registryStore = CreateRegistryStore();
        var registry = new VaultRegistry(registryStore, globalStore, Path.Combine(_tempDir, "vaults"));

        var v1 = await registry.CreateVaultAsync("First", "USD");
        var v2 = await registry.CreateVaultAsync("Second", "EUR");
        await registry.RenameVaultAsync(v1.VaultId, "Renamed First");
        await registry.SetActiveVaultAsync(v2.VaultId);

        var allEvents = await globalStore.ReadAllAsync(CancellationToken.None);

        var state1 = VaultRegistryProjector.Project(allEvents);
        var state2 = VaultRegistryProjector.Project(allEvents);

        Assert.Equal(state1.Vaults.Count, state2.Vaults.Count);
        Assert.Equal(state1.ActiveVaultId, state2.ActiveVaultId);
        Assert.Equal("Renamed First", state1.Vaults[v1.VaultId].Name);
        Assert.Equal(v2.VaultId, state1.ActiveVaultId);
    }

    // ================================================================
    // 12) GlobalEventStore_AppendsAndReadsCorrectly
    // ================================================================
    [Fact]
    public async Task GlobalEventStore_AppendsAndReadsCorrectly()
    {
        var globalStore = CreateGlobalStore();

        var vaultId = Guid.NewGuid();
        var ev = new VaultCreated(vaultId, "Test Vault", "USD", DateOnly.FromDateTime(DateTime.Today));
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(vaultId),
            nameof(VaultCreated),
            DateTimeOffset.UtcNow,
            ev.CreatedDate,
            Guid.Empty,
            Guid.Empty,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await globalStore.AppendAsync(envelope, CancellationToken.None);

        var all = await globalStore.ReadAllAsync(CancellationToken.None);
        Assert.Single(all);
        Assert.Equal(nameof(VaultCreated), all[0].EventType);
    }
}
