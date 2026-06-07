using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Scheduling;

/// <summary>
/// Schedule specification for recurring installments (monthly, quarterly, annual, custom).
/// Generates installments based on a recurrence rule, not fixed dates.
/// </summary>
public sealed class RecurringScheduleSpec : IScheduleSpec
{
    public Guid ScheduleId { get; }
    public Guid ObligationId { get; }
    public RecurrencePattern Pattern { get; }
    public int DayOfMonth { get; }
    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }
    public int? MaxOccurrences { get; }
    public Money InstallmentAmount { get; }
    public WeekendAdjustment WeekendAdjustment { get; }

    public RecurringScheduleSpec(
        Guid scheduleId,
        Guid obligationId,
        RecurrencePattern pattern,
        int dayOfMonth,
        DateOnly startDate,
        DateOnly? endDate,
        int? maxOccurrences,
        Money installmentAmount,
        WeekendAdjustment weekendAdjustment = WeekendAdjustment.None)
    {
        if (scheduleId == Guid.Empty)
            throw new ArgumentException("Schedule ID cannot be empty.", nameof(scheduleId));
        if (obligationId == Guid.Empty)
            throw new ArgumentException("Obligation ID cannot be empty.", nameof(obligationId));
        if (dayOfMonth < 1 || dayOfMonth > 31)
            throw new ArgumentOutOfRangeException(nameof(dayOfMonth), "Day of month must be between 1 and 31.");
        if (endDate.HasValue && endDate.Value < startDate)
            throw new ArgumentException("End date cannot be before start date.", nameof(endDate));
        if (maxOccurrences.HasValue && maxOccurrences.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(maxOccurrences), "Max occurrences must be at least 1.");

        ScheduleId = scheduleId;
        ObligationId = obligationId;
        Pattern = pattern;
        DayOfMonth = dayOfMonth;
        StartDate = startDate;
        EndDate = endDate;
        MaxOccurrences = maxOccurrences;
        InstallmentAmount = installmentAmount;
        WeekendAdjustment = weekendAdjustment;
    }

    /// <summary>
    /// Expand the recurring schedule into concrete installments for a date range.
    /// </summary>
    public IEnumerable<Installment> Expand(DateOnly from, DateOnly to)
    {
        var occurrences = GenerateOccurrences(from, to).ToList();
        var index = 0;

        foreach (var dueDate in occurrences)
        {
            var adjustedDate = AdjustForWeekend(dueDate);
            var installmentKey = GenerateInstallmentKey(index);

            yield return new Installment(
                InstallmentKey: installmentKey,
                ObligationId: ObligationId,
                DueDate: adjustedDate,
                ExpectedAmount: InstallmentAmount,
                ScheduleOrigin: $"recurring:{ScheduleId}",
                Tags: new[] { Pattern.ToString().ToLowerInvariant() }
            );

            index++;
        }
    }

    private IEnumerable<DateOnly> GenerateOccurrences(DateOnly from, DateOnly to)
    {
        var current = GetFirstOccurrence();
        var count = 0;

        while (current <= to && (!MaxOccurrences.HasValue || count < MaxOccurrences.Value))
        {
            if (current >= from && (!EndDate.HasValue || current <= EndDate.Value))
            {
                yield return current;
            }

            current = GetNextOccurrence(current);
            count++;

            // Safety: prevent infinite loops
            if (count > 10000) yield break;
        }
    }

    private DateOnly GetFirstOccurrence()
    {
        // Find the first occurrence on or after StartDate
        var candidate = new DateOnly(StartDate.Year, StartDate.Month, ClampDay(StartDate.Year, StartDate.Month));

        if (candidate < StartDate)
        {
            candidate = GetNextOccurrence(candidate);
        }

        return candidate;
    }

    private DateOnly GetNextOccurrence(DateOnly current)
    {
        return Pattern switch
        {
            RecurrencePattern.Monthly => AddMonths(current, 1),
            RecurrencePattern.Quarterly => AddMonths(current, 3),
            RecurrencePattern.SemiAnnual => AddMonths(current, 6),
            RecurrencePattern.Annual => AddMonths(current, 12),
            RecurrencePattern.Weekly => current.AddDays(7),
            RecurrencePattern.Biweekly => current.AddDays(14),
            _ => AddMonths(current, 1)
        };
    }

    private DateOnly AddMonths(DateOnly date, int months)
    {
        var newDate = date.AddMonths(months);
        var targetDay = ClampDay(newDate.Year, newDate.Month);
        return new DateOnly(newDate.Year, newDate.Month, targetDay);
    }

    private int ClampDay(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        return Math.Min(DayOfMonth, daysInMonth);
    }

    private DateOnly AdjustForWeekend(DateOnly date)
    {
        var dayOfWeek = date.DayOfWeek;

        return WeekendAdjustment switch
        {
            WeekendAdjustment.NextBusinessDay => dayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(2),
                DayOfWeek.Sunday => date.AddDays(1),
                _ => date
            },
            WeekendAdjustment.PreviousBusinessDay => dayOfWeek switch
            {
                DayOfWeek.Saturday => date.AddDays(-1),
                DayOfWeek.Sunday => date.AddDays(-2),
                _ => date
            },
            _ => date
        };
    }

    private string GenerateInstallmentKey(int index)
    {
        // Deterministic key: schedule + pattern + index
        return $"{ScheduleId:N}:{Pattern}:{index:D4}";
    }
}

/// <summary>
/// Recurrence pattern for scheduled installments.
/// </summary>
public enum RecurrencePattern
{
    Weekly,
    Biweekly,
    Monthly,
    Quarterly,
    SemiAnnual,
    Annual
}

/// <summary>
/// How to adjust due dates that fall on weekends.
/// </summary>
public enum WeekendAdjustment
{
    /// <summary>No adjustment — due date remains as calculated.</summary>
    None,

    /// <summary>Move to the next Monday.</summary>
    NextBusinessDay,

    /// <summary>Move to the previous Friday.</summary>
    PreviousBusinessDay
}