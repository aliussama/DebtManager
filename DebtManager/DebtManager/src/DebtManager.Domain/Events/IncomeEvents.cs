using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record IncomeReceived(
    Money Amount,
    DateOnly EffectiveDate,
    string Source
) : DomainEvent(EffectiveDate);
