namespace DebtManager.Application.Models;

/// <summary>
/// Immutable dashboard summary aggregated from projection states.
/// No UI types allowed. No financial computation here — only data transfer.
/// </summary>
public sealed record DashboardSummary(
    decimal TotalCashBalance,
    decimal NetWorth,
    decimal BudgetHealthPercent,
    int OverdueObligationCount,
    IReadOnlyList<UpcomingPaymentItem> UpcomingPayments,
    IReadOnlyList<GoalProgressItem> TopGoals,
    int AiInsightCount,
    int DataQualityIssueCount
);

/// <summary>
/// A single upcoming payment within the next 7 days.
/// </summary>
public sealed record UpcomingPaymentItem(
    Guid EntityId,
    string Title,
    DateOnly DueDate,
    decimal Amount,
    string CurrencyCode
);

/// <summary>
/// Progress snapshot for a single financial goal.
/// </summary>
public sealed record GoalProgressItem(
    Guid GoalId,
    string Name,
    decimal ProgressPercent,
    decimal TargetAmount,
    decimal ContributedAmount,
    DateOnly? TargetDate
);
