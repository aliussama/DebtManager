namespace DebtManager.Domain.Events;

public sealed record ForecastScenarioCreated(
    Guid ScenarioId,
    string Name,
    string Notes,
    DateOnly HorizonStart,
    DateOnly HorizonEnd,
    string Granularity,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ForecastScenarioModified(
    Guid ScenarioId,
    string Name,
    string Notes,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ForecastScenarioArchived(
    Guid ScenarioId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

public sealed record ForecastScenarioChangeAdded(
    Guid ScenarioId,
    Guid ChangeId,
    string ChangeKind,
    string PayloadJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ForecastScenarioChangeRemoved(
    Guid ScenarioId,
    Guid ChangeId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
