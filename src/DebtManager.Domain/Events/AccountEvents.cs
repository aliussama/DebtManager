using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A cash/bank account was created.
/// </summary>
public sealed record AccountCreated(
    Guid AccountId,
    string Name,
    string AccountType,
    string CurrencyCode,
    decimal OpeningBalance,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An account was archived (hidden from active views, but ledger history preserved).
/// </summary>
public sealed record AccountArchived(
    Guid AccountId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
