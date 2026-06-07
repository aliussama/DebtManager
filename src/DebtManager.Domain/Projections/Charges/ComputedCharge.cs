using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections.Charges;

public enum ChargeType
{
    Interest,
    Penalty,
    Fee,
    Tax,
    Other
}
public sealed record ComputedCharge(
    Guid ChargeId,
    Guid ObligationId,
    Guid? InstallmentKey,
    ChargeType Type,
    Money Amount,
    DateOnly EffectiveDate,
    string Label,
    string RuleKey,
    Guid RulePackVersionId
);
