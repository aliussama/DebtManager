using System.IO;
using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Vault;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Infrastructure.Vault;

/// <summary>
/// Central registry for vault management. Persists manifest via DPAPI-encrypted store
/// and appends global events for audit/replay.
/// </summary>
public sealed class VaultRegistry
{
    private readonly DpapiProtectedRegistryStore _registryStore;
    private readonly GlobalEventStore _globalStore;
    private readonly string _vaultsRoot;

    public VaultRegistry(
        DpapiProtectedRegistryStore registryStore,
        GlobalEventStore globalStore,
        string? vaultsRoot = null)
    {
        _registryStore = registryStore;
        _globalStore = globalStore;
        _vaultsRoot = vaultsRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager", "vaults");
    }

    public async Task<VaultDescriptor> CreateVaultAsync(string name, string currencyCode)
    {
        VaultValidation.ValidateName(name);
        VaultValidation.ValidateCurrencyCode(currencyCode);

        var manifest = await _registryStore.LoadAsync();
        if (manifest.Vaults.Count >= VaultValidation.MaxVaults)
            throw new InvalidOperationException($"Maximum {VaultValidation.MaxVaults} vaults allowed.");

        var vaultId = Guid.NewGuid();
        var createdDate = DateOnly.FromDateTime(DateTime.Today);

        // Create directory structure
        var vaultDir = Path.Combine(_vaultsRoot, vaultId.ToString());
        Directory.CreateDirectory(vaultDir);
        Directory.CreateDirectory(Path.Combine(vaultDir, "blobs"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "exports"));

        var descriptor = new VaultDescriptor
        {
            VaultId = vaultId,
            Name = name.Trim(),
            CurrencyCode = currencyCode,
            CreatedDate = createdDate
        };

        // Append global event
        var ev = new VaultCreated(vaultId, name.Trim(), currencyCode, createdDate);
        await AppendGlobalEventAsync(ev, vaultId);

        // Update manifest
        var updatedVaults = manifest.Vaults.ToList();
        updatedVaults.Add(descriptor);
        var updatedManifest = manifest with { Vaults = updatedVaults };
        await _registryStore.SaveAsync(updatedManifest);

        return descriptor;
    }

    public async Task RenameVaultAsync(Guid vaultId, string newName)
    {
        VaultValidation.ValidateName(newName);

        var manifest = await _registryStore.LoadAsync();
        var vault = manifest.Vaults.FirstOrDefault(v => v.VaultId == vaultId)
            ?? throw new InvalidOperationException($"Vault {vaultId} not found.");

        var ev = new VaultRenamed(vaultId, newName.Trim(), DateOnly.FromDateTime(DateTime.Today));
        await AppendGlobalEventAsync(ev, vaultId);

        var updatedVaults = manifest.Vaults.Select(v =>
            v.VaultId == vaultId ? v with { Name = newName.Trim() } : v).ToList();
        await _registryStore.SaveAsync(manifest with { Vaults = updatedVaults });
    }

    public async Task ArchiveVaultAsync(Guid vaultId, string reason)
    {
        var manifest = await _registryStore.LoadAsync();
        var vault = manifest.Vaults.FirstOrDefault(v => v.VaultId == vaultId)
            ?? throw new InvalidOperationException($"Vault {vaultId} not found.");

        var ev = new VaultArchived(vaultId, reason, DateOnly.FromDateTime(DateTime.Today));
        await AppendGlobalEventAsync(ev, vaultId);

        var updatedVaults = manifest.Vaults.Select(v =>
            v.VaultId == vaultId ? v with { IsArchived = true } : v).ToList();

        var activeId = manifest.ActiveVaultId == vaultId ? (Guid?)null : manifest.ActiveVaultId;
        await _registryStore.SaveAsync(manifest with { Vaults = updatedVaults, ActiveVaultId = activeId });
    }

    public async Task SetActiveVaultAsync(Guid vaultId)
    {
        var manifest = await _registryStore.LoadAsync();
        var vault = manifest.Vaults.FirstOrDefault(v => v.VaultId == vaultId)
            ?? throw new InvalidOperationException($"Vault {vaultId} not found.");

        if (vault.IsArchived)
            throw new InvalidOperationException("Cannot activate an archived vault.");

        var ev = new ActiveVaultSelected(vaultId, DateOnly.FromDateTime(DateTime.Today));
        await AppendGlobalEventAsync(ev, vaultId);

        var updatedVaults = manifest.Vaults.Select(v =>
            v.VaultId == vaultId ? v with { LastOpenedAtUtc = DateTimeOffset.UtcNow } : v).ToList();
        await _registryStore.SaveAsync(manifest with { Vaults = updatedVaults, ActiveVaultId = vaultId });
    }

    public async Task<IReadOnlyList<VaultDescriptor>> ListVaultsAsync()
    {
        var manifest = await _registryStore.LoadAsync();
        return manifest.Vaults;
    }

    public async Task<VaultManifest> LoadManifestAsync() =>
        await _registryStore.LoadAsync();

    public VaultPaths ResolveVaultPaths(Guid vaultId)
    {
        var vaultDir = Path.Combine(_vaultsRoot, vaultId.ToString());
        return new VaultPaths(
            DbPath: Path.Combine(vaultDir, "data.db"),
            KeyPath: Path.Combine(vaultDir, "dbkey.bin"),
            AuthVaultPath: Path.Combine(vaultDir, "auth_vault.bin"),
            BlobsPath: Path.Combine(vaultDir, "blobs"),
            ExportsPath: Path.Combine(vaultDir, "exports")
        );
    }

    /// <summary>
    /// Register an externally-created vault (e.g. from migration or restore).
    /// </summary>
    public async Task RegisterVaultAsync(VaultDescriptor descriptor)
    {
        var manifest = await _registryStore.LoadAsync();
        if (manifest.Vaults.Any(v => v.VaultId == descriptor.VaultId))
            return; // already registered

        if (manifest.Vaults.Count >= VaultValidation.MaxVaults)
            throw new InvalidOperationException($"Maximum {VaultValidation.MaxVaults} vaults allowed.");

        var updatedVaults = manifest.Vaults.ToList();
        updatedVaults.Add(descriptor);
        await _registryStore.SaveAsync(manifest with { Vaults = updatedVaults });
    }

    private async Task AppendGlobalEventAsync(IDomainEvent ev, Guid streamEntityId)
    {
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(streamEntityId),
            ev.GetType().Name,
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            Guid.Empty, // system actor
            Guid.Empty, // no device context
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, ev.GetType(), DomainJson.Options));

        await _globalStore.AppendAsync(envelope, CancellationToken.None);
    }
}
