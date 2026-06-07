using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Events;

/// <summary>
/// Immutable event: A schedule was modified (rescheduled, deferred, restructured).
/// The original schedule is preserved — this event records the change.
/// </summary>
public sealed record ScheduleModified(
    Guid EventId,
    Guid ObligationId,
    Guid OriginalScheduleId,
    Guid NewScheduleId,
    ScheduleModificationType ModificationType,
    DateOnly EffectiveDate,
    string Reason,
    ScheduleModificationDetails Details,
    DateTimeOffset OccurredAt,
    Guid ActorUserId,
    Guid DeviceId
) : IDomainEvent;

/// <summary>
/// Type of schedule modification.
/// </summary>
public enum ScheduleModificationType
{
    /// <summary>Due dates shifted forward.</summary>
    Deferral,

    /// <summary>Payment amounts changed (up or down).</summary>
    AmountAdjustment,

    /// <summary>Term extended or shortened.</summary>
    TermChange,

    /// <summary>Full restructure (new terms, potentially new principal).</summary>
    Restructure,

    /// <summary>Frequency changed (e.g., monthly to quarterly).</summary>
    FrequencyChange,

    /// <summary>Grace period applied.</summary>
    GracePeriodApplied,

    /// <summary>Early settlement triggered schedule closure.</summary>
    EarlySettlement,

    /// <summary>Other modification not categorized above.</summary>
    Other
}

/// <summary>
/// Details of what changed in the schedule modification.
/// </summary>
public sealed record ScheduleModificationDetails(
    int? DaysDeferred,
    int? NewTermMonths,
    Money? NewInstallmentAmount,
    decimal? NewInterestRate,
    RecurrencePattern? NewPattern,
    DateOnly? NewEndDate,
    Dictionary<string, object>? AdditionalData
);