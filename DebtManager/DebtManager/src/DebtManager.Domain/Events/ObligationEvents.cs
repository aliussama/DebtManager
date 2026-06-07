using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record ObligationCreated(
    Guid ObligationId,
    string Name,
    string ObligationType,
    Money Principal,
    DateOnly StartDate,
    string CurrencyCode
) : DomainEvent(StartDate);
