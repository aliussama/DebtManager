using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record IncomeRecorded(
    Guid AccountId,
    Money Amount,
    DateOnly EffectiveDate,
    string Source
) : IDomainEvent;

public sealed record ExpenseRecorded(
    Guid AccountId,
    Money Amount,
    DateOnly EffectiveDate,
    string Category,
    string Notes
) : IDomainEvent;
