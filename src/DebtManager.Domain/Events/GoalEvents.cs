using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A financial goal was created.
/// </summary>
public sealed record FinancialGoalCreated(
    Guid GoalId,
    string Name,
    string GoalType,
    Money TargetAmount,
    DateOnly TargetDate,
    string? Notes,
    string[] Tags,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A financial goal was modified.
/// </summary>
public sealed record FinancialGoalModified(
    Guid GoalId,
    string Name,
    string GoalType,
    Money TargetAmount,
    DateOnly TargetDate,
    string? Notes,
    string[] Tags,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A financial goal was archived.
/// </summary>
public sealed record FinancialGoalArchived(
    Guid GoalId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A contribution was recorded toward a goal.
/// </summary>
public sealed record GoalContributionRecorded(
    Guid GoalId,
    Guid ContributionId,
    Guid AccountId,
    Money Amount,
    DateOnly EffectiveDate,
    string Reference
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A goal contribution was reversed.
/// </summary>
public sealed record GoalContributionReversed(
    Guid GoalId,
    Guid ContributionId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);
