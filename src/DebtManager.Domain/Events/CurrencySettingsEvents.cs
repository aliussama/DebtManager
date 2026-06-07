using DebtManager.Domain.Fx;

namespace DebtManager.Domain.Events;

public sealed record ReportingCurrencySet(
    Guid ProfileId,
    string ReportingCurrencyCode,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record FxPolicySet(
    Guid ProfileId,
    FxValuationPolicy Policy,
    int MaxAgeDays,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record CurrencySettingsArchived(
    Guid ProfileId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
