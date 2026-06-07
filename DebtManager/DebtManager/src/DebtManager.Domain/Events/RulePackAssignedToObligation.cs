namespace DebtManager.Domain.Events;

public sealed record RulePackAssignedToObligation(
    Guid ObligationId,
    string RulePackId,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
