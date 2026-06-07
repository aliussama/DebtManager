using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record PaymentUnapplied(
    Guid ObligationId,
    Guid PaymentEventId,
    Money Amount,
    DateOnly EffectiveDate,
    string? Reason
) : DomainEvent(EffectiveDate);
