using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

public sealed record PaymentAllocationReversed(
    Guid OriginalPaymentEventId,
    Guid ObligationId,
    Guid InstallmentKey,
    Money Amount,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
