namespace DebtManager.Domain.Events;

public sealed record IncomeReversed(
    Guid OriginalEventId,
    Guid AccountId,
    decimal Amount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record ExpenseReversed(
    Guid OriginalEventId,
    Guid AccountId,
    decimal Amount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record TransferReversed(
    Guid OriginalEventId,
    Guid FromAccountId,
    Guid ToAccountId,
    decimal Amount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
