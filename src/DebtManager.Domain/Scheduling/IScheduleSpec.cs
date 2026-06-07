using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Scheduling;

/// <summary>
/// Contract for all schedule specifications.
/// Schedules define WHEN and HOW an obligation unfolds.
/// Time is first-class — never inferred.
/// </summary>
public interface IScheduleSpec
{
    Guid ScheduleId { get; }
    Guid ObligationId { get; }

    /// <summary>
    /// Expand the schedule into concrete installments for a date range.
    /// </summary>
    /// <param name="from">Start of the expansion window (inclusive).</param>
    /// <param name="to">End of the expansion window (inclusive).</param>
    /// <returns>Sequence of installments within the date range.</returns>
    IEnumerable<Installment> Expand(DateOnly from, DateOnly to);
}