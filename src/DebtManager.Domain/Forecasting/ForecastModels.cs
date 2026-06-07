namespace DebtManager.Domain.Forecasting;

public enum ForecastGranularity { Daily, Weekly, Monthly }

public sealed record ForecastHorizon(DateOnly StartDate, DateOnly EndDate, ForecastGranularity Granularity);

public sealed record ForecastPoint(
    DateOnly Date,
    Guid? AccountId,
    string CurrencyCode,
    decimal Amount,
    decimal ReportingAmount,
    string Kind // "Income", "Expense", "Transfer", "DebtPayment", "GoalContribution"
);

public sealed record CashBalanceSeries(
    Guid AccountId,
    string AccountName,
    string CurrencyCode,
    IReadOnlyList<(DateOnly Date, decimal Balance, decimal ReportingBalance)> Points
);

public sealed record CashflowBreakdownRow(
    string Category,
    decimal Amount,
    decimal ReportingAmount
);

public sealed record BudgetForecastRow(
    int Year,
    int Month,
    Guid? CategoryId,
    string ScopeLabel,
    decimal Limit,
    decimal ForecastActual,
    decimal Remaining,
    decimal Percent,
    string Status // "OK", "Warn", "Exceeded"
);

public sealed record DebtForecastRow(
    Guid ObligationId,
    string Name,
    decimal RemainingPrincipal,
    DateOnly? NextDueDate,
    decimal MonthlyPayment,
    DateOnly? PayoffDate,
    bool IsKnown,
    string CurrencyCode
);

public sealed record GoalForecastRow(
    Guid GoalId,
    string Name,
    decimal TargetAmount,
    decimal ForecastContributed,
    decimal Remaining,
    decimal ProgressPercent,
    DateOnly? EstimatedCompletionDate,
    bool IsKnown,
    string CurrencyCode
);

public sealed record ForecastWarning(string Code, string Message, DateOnly? RelevantDate);

public sealed record ForecastSummary(
    decimal KnownNetCashflow,
    decimal KnownEndBalance,
    int UnknownCount,
    IReadOnlyList<ForecastWarning> Warnings
);

public sealed record ForecastReport(
    ForecastHorizon Horizon,
    string ReportingCurrency,
    ForecastSummary Summary,
    IReadOnlyList<CashBalanceSeries> BalanceSeries,
    IReadOnlyList<CashflowBreakdownRow> CashflowRows,
    IReadOnlyList<BudgetForecastRow> BudgetRows,
    IReadOnlyList<DebtForecastRow> DebtRows,
    IReadOnlyList<GoalForecastRow> GoalRows,
    IReadOnlyList<ForecastPoint> AllPoints
);
