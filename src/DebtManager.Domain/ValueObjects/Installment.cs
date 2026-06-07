namespace DebtManager.Domain.ValueObjects;

/// <summary>
/// A single required slice of an obligation with a due date.
/// Installments can exist before money exists — they represent obligations, not payments.
/// </summary>
public sealed record Installment(
    string InstallmentKey,
    Guid ObligationId,
    DateOnly DueDate,
    Money ExpectedAmount,
    string ScheduleOrigin,
    IReadOnlyList<string> Tags
)
{
    /// <summary>
    /// Check if this installment is overdue as of a given date.
    /// </summary>
    public bool IsOverdue(DateOnly asOf) => DueDate < asOf;

    /// <summary>
    /// Calculate days overdue as of a given date.
    /// Returns 0 if not yet due.
    /// </summary>
    public int DaysOverdue(DateOnly asOf)
    {
        if (DueDate >= asOf) return 0;
        return asOf.DayNumber - DueDate.DayNumber;
    }

    /// <summary>
    /// Check if this installment falls within a date range.
    /// </summary>
    public bool IsWithinRange(DateOnly from, DateOnly to)
    {
        return DueDate >= from && DueDate <= to;
    }
}