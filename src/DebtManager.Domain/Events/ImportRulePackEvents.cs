namespace DebtManager.Domain.Events;

public sealed record ImportRulePackCreated(
    Guid PackId,
    string Name,
    string Description,
    bool IsEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportRulePackModified(
    Guid PackId,
    string Name,
    string Description,
    bool IsEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportRulePackArchived(
    Guid PackId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportRuleDefined(
    Guid PackId,
    Guid RuleId,
    int Version,
    string RuleKind,
    string MatchSpecJson,
    string ActionSpecJson,
    int Priority,
    bool IsEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportRuleArchived(
    Guid PackId,
    Guid RuleId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportRuleTested(
    Guid PackId,
    Guid RuleId,
    int Version,
    Guid BatchId,
    int CandidatesCount,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

public sealed record ImportAutoActionExecuted(
    Guid BatchId,
    Guid ImportedTransactionId,
    string ActionKind,
    Guid? RelatedEntityId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
