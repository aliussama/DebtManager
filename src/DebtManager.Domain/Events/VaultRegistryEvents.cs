namespace DebtManager.Domain.Events;

/// <summary>
/// Global vault registry events. Stored in the GLOBAL event store, NOT per-vault.
/// These events contain NO financial data.
/// </summary>
public sealed record VaultCreated(
    Guid VaultId,
    string Name,
    string CurrencyCode,
    DateOnly CreatedDate
) : DomainEvent(CreatedDate);

public sealed record VaultRenamed(
    Guid VaultId,
    string NewName,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record VaultArchived(
    Guid VaultId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ActiveVaultSelected(
    Guid VaultId,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
