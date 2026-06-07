namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: Full replacement of tags for any entity.
/// Previous tags are completely replaced — this is NOT a delta operation.
/// EntityType is a stable string identifier (e.g. "Account", "Obligation", "Bill", etc).
/// </summary>
public sealed record EntityTagsReplaced(
    Guid EntityId,
    string EntityType,
    IReadOnlyList<string> Tags,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
