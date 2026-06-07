using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record PaymentAllocated(
    Guid PaymentEventId,          // links to PaymentMade's EventId in your event store later
    Guid ObligationId,
    Guid InstallmentKey,
    Money Amount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
