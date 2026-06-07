using DebtManager.Domain.Vault;

namespace DebtManager.Domain.Projections;

/// <summary>
/// Projected state of the global vault registry.
/// </summary>
public sealed class VaultRegistryState
{
    public Dictionary<Guid, VaultDescriptor> Vaults { get; } = new();
    public Guid? ActiveVaultId { get; set; }

    public VaultDescriptor? GetActive()
    {
        if (ActiveVaultId.HasValue && Vaults.TryGetValue(ActiveVaultId.Value, out var v))
            return v;
        return null;
    }

    public IReadOnlyList<VaultDescriptor> GetAllActive() =>
        Vaults.Values.Where(v => !v.IsArchived).OrderBy(v => v.CreatedDate).ToList();

    public VaultDescriptor? GetById(Guid vaultId) =>
        Vaults.TryGetValue(vaultId, out var v) ? v : null;
}
