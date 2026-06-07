using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record PaymentMade(
    Guid ObligationId,
    Money Amount,
    DateOnly EffectiveDate,
    string? Reference
) : DomainEvent(EffectiveDate);
