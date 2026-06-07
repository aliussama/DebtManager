namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A recurring transaction template was created.
/// Kind: "income" or "expense".
/// Frequency: "Weekly", "Monthly", "Quarterly", "Yearly".
/// </summary>
public sealed record RecurringTransactionCreated(
    Guid RecurringId,
    string Kind,
    Guid AccountId,
    decimal Amount,
    string CurrencyCode,
    Guid? CategoryId,
    string? Notes,
    string? Reference,
    string Frequency,
    int Interval,
    DateOnly StartDate,
    DateOnly? EndDate,
    bool AutoPostEnabled,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A recurring transaction was modified.
/// PatchJson contains only the changed fields as JSON.
/// </summary>
public sealed record RecurringTransactionModified(
    Guid RecurringId,
    string PatchJson,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A recurring transaction was archived (deactivated).
/// </summary>
public sealed record RecurringTransactionArchived(
    Guid RecurringId,
    DateOnly EffectiveDate,
    string Reason
) : DomainEvent(EffectiveDate);

/// <summary>
/// Immutable event: A recurring template was posted (materialized as actual income/expense).
/// Links the template to the posted event.
/// </summary>
public sealed record RecurringTransactionPosted(
    Guid RecurringId,
    Guid PostedEventId,
    DateOnly EffectiveDate
) : DomainEvent(EffectiveDate);
