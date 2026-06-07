using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Services.Rules;

/// <summary>
/// Rule for grace period logic.
/// "No penalty for first X days after due date."
/// Grace periods are time-bound and can vary by institution/product.
/// </summary>
public sealed class GracePeriodRule
{
    public string RuleKey { get; }
    public int GraceDays { get; }
    public GracePeriodType Type { get; }
    public bool AppliesToPenalties { get; }
    public bool AppliesToInterest { get; }
    public DateOnly? EffectiveFrom { get; }
    public DateOnly? EffectiveTo { get; }

    public GracePeriodRule(
        string ruleKey,
        int graceDays,
        GracePeriodType type = GracePeriodType.CalendarDays,
        bool appliesToPenalties = true,
        bool appliesToInterest = false,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
    {
        if (graceDays < 0)
            throw new ArgumentOutOfRangeException(nameof(graceDays), "Grace days cannot be negative.");

        RuleKey = ruleKey;
        GraceDays = graceDays;
        Type = type;
        AppliesToPenalties = appliesToPenalties;
        AppliesToInterest = appliesToInterest;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    /// <summary>
    /// Check if a given date is within the grace period after a due date.
    /// </summary>
    public GracePeriodEvaluation Evaluate(DateOnly dueDate, DateOnly evaluationDate)
    {
        if (evaluationDate <= dueDate)
        {
            return new GracePeriodEvaluation(
                IsWithinGrace: true,
                DaysOverdue: 0,
                DaysIntoGrace: 0,
                GraceExpiryDate: CalculateGraceExpiry(dueDate),
                RuleKey: RuleKey
            );
        }

        var daysOverdue = evaluationDate.DayNumber - dueDate.DayNumber;
        var graceExpiry = CalculateGraceExpiry(dueDate);
        var isWithinGrace = evaluationDate <= graceExpiry;
        var daysIntoGrace = isWithinGrace ? daysOverdue : GraceDays;

        return new GracePeriodEvaluation(
            IsWithinGrace: isWithinGrace,
            DaysOverdue: daysOverdue,
            DaysIntoGrace: daysIntoGrace,
            GraceExpiryDate: graceExpiry,
            RuleKey: RuleKey
        );
    }

    /// <summary>
    /// Calculate the effective days overdue after applying grace period.
    /// </summary>
    public int CalculateEffectiveDaysOverdue(DateOnly dueDate, DateOnly evaluationDate)
    {
        var eval = Evaluate(dueDate, evaluationDate);

        if (eval.IsWithinGrace)
            return 0;

        // Days overdue beyond grace period
        return eval.DaysOverdue - GraceDays;
    }

    private DateOnly CalculateGraceExpiry(DateOnly dueDate)
    {
        return Type switch
        {
            GracePeriodType.CalendarDays => dueDate.AddDays(GraceDays),
            GracePeriodType.BusinessDays => AddBusinessDays(dueDate, GraceDays),
            _ => dueDate.AddDays(GraceDays)
        };
    }

    private static DateOnly AddBusinessDays(DateOnly start, int days)
    {
        var current = start;
        var added = 0;

        while (added < days)
        {
            current = current.AddDays(1);
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                added++;
            }
        }

        return current;
    }
}

/// <summary>
/// Type of grace period calculation.
/// </summary>
public enum GracePeriodType
{
    /// <summary>Calendar days including weekends.</summary>
    CalendarDays,

    /// <summary>Business days excluding weekends.</summary>
    BusinessDays
}

/// <summary>
/// Result of grace period evaluation.
/// </summary>
public sealed record GracePeriodEvaluation(
    bool IsWithinGrace,
    int DaysOverdue,
    int DaysIntoGrace,
    DateOnly GraceExpiryDate,
    string RuleKey
);