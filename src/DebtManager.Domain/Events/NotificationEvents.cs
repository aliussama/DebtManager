namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A notification rule was created.
/// </summary>
public sealed record NotificationRuleCreated(
    Guid RuleId,
    string RuleCode,
    string Area,
    string Severity,
    string ConfigJson,
    bool IsEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A notification rule was modified.
/// </summary>
public sealed record NotificationRuleModified(
    Guid RuleId,
    string ConfigJson,
    bool IsEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A notification rule was archived.
/// </summary>
public sealed record NotificationRuleArchived(
    Guid RuleId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A notification was acknowledged by the user.
/// </summary>
public sealed record NotificationAcknowledged(
    Guid NotificationId,
    string AckNote,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A notification was dismissed by the user.
/// </summary>
public sealed record NotificationDismissed(
    Guid NotificationId,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A notification was snoozed until a future date.
/// </summary>
public sealed record NotificationSnoozed(
    Guid NotificationId,
    DateOnly SnoozeUntil,
    string Reason,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A linked action was associated with a notification.
/// </summary>
public sealed record NotificationActionLinked(
    Guid NotificationId,
    string ActionType,
    string ActionRefJson,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
