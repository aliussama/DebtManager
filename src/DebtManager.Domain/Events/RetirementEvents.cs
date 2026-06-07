using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A retirement profile was defined.
/// </summary>
public sealed record RetirementProfileDefined(
    Guid ProfileId,
    string ProfileName,
    DateOnly RetirementDate,
    Money DesiredMonthlySpending,
    int LifeExpectancyYears,
    string WithdrawalStrategy,
    decimal SafeWithdrawalRate,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Retirement planning assumptions were set.
/// </summary>
public sealed record RetirementAssumptionsSet(
    Guid AssumptionsId,
    string Name,
    decimal ExpectedAnnualReturnRate,
    decimal ExpectedAnnualInflation,
    decimal ExpectedAnnualSalaryGrowth,
    Money CurrentMonthlySavings,
    string ReportingCurrencyCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: Retirement assumptions were archived.
/// </summary>
public sealed record RetirementAssumptionsArchived(
    Guid AssumptionsId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
