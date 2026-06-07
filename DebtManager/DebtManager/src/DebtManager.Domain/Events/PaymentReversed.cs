using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record PaymentReversed(
    Guid OriginalPaymentEventId,
    Guid ObligationId,
    Money Amount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
