namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A data quality scan was executed and results recorded.
/// </summary>
public sealed record DataQualityScanRecorded(
    DateOnly EffectiveDate,
    Guid ScanId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string AppVersion,
    string RuleSetVersion,
    string SummaryJson
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A data quality issue was acknowledged by the user.
/// </summary>
public sealed record DataQualityIssueAcknowledged(
    DateOnly EffectiveDate,
    Guid IssueId,
    string Note
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A data quality issue was resolved (manually or via fix).
/// </summary>
public sealed record DataQualityIssueResolved(
    DateOnly EffectiveDate,
    Guid IssueId,
    string ResolutionKind,
    string ResolutionDetailsJson
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: An automated fix was applied for a data quality issue.
/// </summary>
public sealed record DataQualityAutoFixApplied(
    DateOnly EffectiveDate,
    Guid FixId,
    Guid IssueId,
    string FixKind,
    Guid[] AppliedEventIds,
    string Notes
) : DomainEvent(EffectiveDate);
