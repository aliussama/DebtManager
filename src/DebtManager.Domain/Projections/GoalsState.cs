using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

public sealed class FinancialGoalRecord
{
    public Guid GoalId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GoalType { get; set; } = string.Empty;
    public Money TargetAmount { get; set; }
    public DateOnly TargetDate { get; set; }
    public string? Notes { get; set; }
    public string[] Tags { get; set; } = [];
    public bool IsArchived { get; set; }
    public DateOnly CreatedDate { get; set; }
}

public sealed class GoalContributionRecord
{
    public Guid ContributionId { get; set; }
    public Guid GoalId { get; set; }
    public Guid AccountId { get; set; }
    public Money Amount { get; set; }
    public DateOnly EffectiveDate { get; set; }
    public string Reference { get; set; } = string.Empty;
    public bool IsReversed { get; set; }
}

public sealed class GoalsState
{
    public Dictionary<Guid, FinancialGoalRecord> Goals { get; } = new();
    public Dictionary<Guid, List<GoalContributionRecord>> ContributionsByGoal { get; } = new();

    public decimal TotalContributed(Guid goalId)
    {
        if (!ContributionsByGoal.TryGetValue(goalId, out var list))
            return 0m;
        return list.Where(c => !c.IsReversed).Sum(c => c.Amount.Amount);
    }

    public decimal RemainingAmount(Guid goalId)
    {
        if (!Goals.TryGetValue(goalId, out var goal))
            return 0m;
        return Math.Max(0m, goal.TargetAmount.Amount - TotalContributed(goalId));
    }

    public decimal ProgressPercent(Guid goalId)
    {
        if (!Goals.TryGetValue(goalId, out var goal) || goal.TargetAmount.Amount <= 0)
            return 0m;
        var pct = TotalContributed(goalId) / goal.TargetAmount.Amount * 100m;
        return Math.Min(100m, Math.Round(pct, 2, MidpointRounding.AwayFromZero));
    }

    public decimal AvgMonthlyContribution(Guid goalId, DateOnly asOfDate, int months = 6)
    {
        if (!ContributionsByGoal.TryGetValue(goalId, out var list))
            return 0m;
        var cutoff = asOfDate.AddMonths(-months);
        var recent = list.Where(c => !c.IsReversed && c.EffectiveDate >= cutoff).Sum(c => c.Amount.Amount);
        return months > 0 ? Math.Round(recent / months, 2, MidpointRounding.AwayFromZero) : 0m;
    }

    public DateOnly? EstimatedCompletionDate(Guid goalId, DateOnly asOfDate)
    {
        var remaining = RemainingAmount(goalId);
        if (remaining <= 0) return asOfDate;
        var avgMonthly = AvgMonthlyContribution(goalId, asOfDate);
        if (avgMonthly <= 0) return null;
        var monthsNeeded = (int)Math.Ceiling(remaining / avgMonthly);
        return asOfDate.AddMonths(monthsNeeded);
    }
}
