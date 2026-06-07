namespace DebtManager.Domain.Billing;

/// <summary>
/// Pure deterministic helpers for billing status computation and aging.
/// No side effects, no IO, no DateTime.Now.
/// </summary>
public static class BillingStatusRules
{
    /// <summary>
    /// Compute the status of a bill/invoice deterministically.
    /// </summary>
    public static string ComputeStatus(
        decimal effectiveTotal,
        decimal totalPaid,
        bool isCancelled,
        bool isDisputed,
        bool isWrittenOff,
        DateOnly asOfDate)
    {
        if (isCancelled) return "Cancelled";
        if (isWrittenOff) return "WrittenOff";
        if (isDisputed) return "Disputed";

        if (effectiveTotal <= 0) return "Paid";

        var outstanding = effectiveTotal - totalPaid;
        if (outstanding <= 0) return "Paid";
        if (totalPaid > 0) return "PartiallyPaid";
        return "Due";
    }

    /// <summary>
    /// Determine the aging bucket for a due date relative to the as-of date.
    /// Returns: "Current", "0-30", "31-60", "61-90", "90+".
    /// </summary>
    public static string AgingBucket(DateOnly dueDate, DateOnly asOfDate)
    {
        var daysOverdue = asOfDate.DayNumber - dueDate.DayNumber;
        if (daysOverdue <= 0) return "Current";
        if (daysOverdue <= 30) return "0-30";
        if (daysOverdue <= 60) return "31-60";
        if (daysOverdue <= 90) return "61-90";
        return "90+";
    }

    /// <summary>
    /// Check if a bill/invoice is overdue as of a given date.
    /// </summary>
    public static bool IsOverdue(DateOnly dueDate, DateOnly asOfDate, string status)
        => asOfDate > dueDate && status is "Due" or "PartiallyPaid";
}
