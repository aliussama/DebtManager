namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: Money was transferred between two accounts.
/// Single event debits FromAccount and credits ToAccount.
/// </summary>
public sealed record TransferRecorded(
    Guid TransferId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    string CurrencyCode,
    DateOnly EffectiveDate,
    string Reference
) : DomainEvent(EffectiveDate);
