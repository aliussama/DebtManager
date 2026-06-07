using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record ChargeAllocated(
    Guid ObligationId,
    Guid ChargeId,
    Guid PaymentEventId,
    Money Amount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
