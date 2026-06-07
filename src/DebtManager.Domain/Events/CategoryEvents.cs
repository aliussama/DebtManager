namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A user-defined category was created.
/// Kind: "income" or "expense".
/// </summary>
public sealed record CategoryCreated(
    Guid CategoryId,
    string Name,
    string Kind,
    Guid? ParentCategoryId,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A category was renamed.
/// </summary>
public sealed record CategoryRenamed(
    Guid CategoryId,
    string NewName,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A category was archived.
/// </summary>
public sealed record CategoryArchived(
    Guid CategoryId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
