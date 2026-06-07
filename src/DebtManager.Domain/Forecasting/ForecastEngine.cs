using System.Text.Json;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Forecasting;

/// <summary>
/// Pure deterministic forecast engine. No DB, no UI, no HTTP, no DateTime.Now.
/// All dates are provided by caller.
/// </summary>
public static class ForecastEngine
{
    public static ForecastReport BuildBaselineForecast(
        ForecastHorizon horizon,
        CashLedgerState cashState,
        RecurringState recurringState,
        BudgetState budgetState,
        GoalsState goalsState,
        CategoryState categoryState,
        IReadOnlyList<DebtForecastRow> debtRows,
        string reportingCurrency,
        FxPolicyConfig fxConfig,
        FxGraph fxGraph,
        IReadOnlyList<ForecastAdjustment>? scenarioAdjustments = null)
    {
        var points = new List<ForecastPoint>();
        var warnings = new List<ForecastWarning>();
        int unknownCount = 0;

        // Build set of suppressed recurring IDs from scenario
        var suppressedRecurring = new HashSet<Guid>();
        if (scenarioAdjustments != null)
        {
            foreach (var adj in scenarioAdjustments)
            {
                if (adj.RecurringIdToSuppress.HasValue)
                    suppressedRecurring.Add(adj.RecurringIdToSuppress.Value);
            }
        }

        // 1) Expand recurring templates into forecast points
        foreach (var item in recurringState.Items.Values.Where(i => !i.IsArchived))
        {
            if (suppressedRecurring.Contains(item.RecurringId))
                continue;

            var occurrences = ExpandRecurring(item, horizon.StartDate, horizon.EndDate);
            foreach (var date in occurrences)
            {
                var kind = item.Kind == "income" ? "Income" : "Expense";
                var sign = item.Kind == "income" ? item.Amount : item.Amount;

                var fxResult = ConvertToReporting(item.Amount, item.CurrencyCode, reportingCurrency, date, fxConfig, fxGraph);
                if (!fxResult.IsKnown) unknownCount++;

                points.Add(new ForecastPoint(date, item.AccountId, item.CurrencyCode, sign, fxResult.ReportingAmount, kind));
            }
        }

        // 2) Add scenario adjustments as forecast points
        if (scenarioAdjustments != null)
        {
            foreach (var adj in scenarioAdjustments.Where(a => a.RecurringIdToSuppress == null))
            {
                if (adj.Date < horizon.StartDate || adj.Date > horizon.EndDate) continue;

                var fxResult = ConvertToReporting(adj.Amount, adj.CurrencyCode, reportingCurrency, adj.Date, fxConfig, fxGraph);
                if (!fxResult.IsKnown) unknownCount++;

                points.Add(new ForecastPoint(adj.Date, adj.AccountId, adj.CurrencyCode, adj.Amount, fxResult.ReportingAmount, adj.Kind));
            }
        }

        // 3) Build balance series per account
        var balanceSeries = BuildBalanceSeries(cashState, points, horizon, reportingCurrency, fxConfig, fxGraph, warnings);

        // 4) Build cashflow breakdown
        var cashflowRows = BuildCashflowBreakdown(points);

        // 5) Build budget forecast rows
        var budgetRows = BuildBudgetForecast(budgetState, categoryState, recurringState, horizon, suppressedRecurring, scenarioAdjustments);

        // 6) Build goal forecast rows
        var goalRows = BuildGoalForecast(goalsState, recurringState, horizon);

        // 7) Compute summary
        var totalIncome = points.Where(p => p.Kind == "Income").Sum(p => p.ReportingAmount);
        var totalExpense = points.Where(p => p.Kind == "Expense").Sum(p => p.ReportingAmount);
        var netCashflow = totalIncome - totalExpense;

        var endBalance = balanceSeries.Sum(s => s.Points.Count > 0 ? s.Points[^1].ReportingBalance : 0m);

        var summary = new ForecastSummary(
            Math.Round(netCashflow, 2, MidpointRounding.AwayFromZero),
            Math.Round(endBalance, 2, MidpointRounding.AwayFromZero),
            unknownCount,
            warnings);

        return new ForecastReport(horizon, reportingCurrency, summary, balanceSeries, cashflowRows, budgetRows, debtRows, goalRows, points);
    }

    private static IReadOnlyList<DateOnly> ExpandRecurring(RecurringItem item, DateOnly from, DateOnly to)
    {
        var dates = new List<DateOnly>();
        var candidate = item.StartDate;

        while (candidate <= to)
        {
            if (candidate >= from && !item.PostedDates.Contains(candidate))
            {
                if (!item.EndDate.HasValue || candidate <= item.EndDate.Value)
                    dates.Add(candidate);
            }

            candidate = AdvanceDate(candidate, item.Frequency, item.Interval);

            if (item.EndDate.HasValue && candidate > item.EndDate.Value)
                break;
        }

        return dates;
    }

    private static DateOnly AdvanceDate(DateOnly date, string frequency, int interval) =>
        frequency switch
        {
            "Weekly" => date.AddDays(7 * interval),
            "Monthly" => date.AddMonths(interval),
            "Quarterly" => date.AddMonths(3 * interval),
            "Yearly" => date.AddYears(interval),
            _ => date.AddMonths(interval)
        };

    private static IReadOnlyList<CashBalanceSeries> BuildBalanceSeries(
        CashLedgerState cashState,
        List<ForecastPoint> points,
        ForecastHorizon horizon,
        string reportingCurrency,
        FxPolicyConfig fxConfig,
        FxGraph fxGraph,
        List<ForecastWarning> warnings)
    {
        var result = new List<CashBalanceSeries>();

        foreach (var account in cashState.Accounts.Values.Where(a => !a.IsArchived))
        {
            var buckets = GenerateDateBuckets(horizon);
            var balance = account.Balance;
            var seriesPoints = new List<(DateOnly Date, decimal Balance, decimal ReportingBalance)>();

            foreach (var date in buckets)
            {
                var dayIncome = points
                    .Where(p => p.AccountId == account.AccountId && p.Kind == "Income" && p.Date == date)
                    .Sum(p => p.Amount);

                var dayExpense = points
                    .Where(p => p.AccountId == account.AccountId && p.Kind == "Expense" && p.Date == date)
                    .Sum(p => p.Amount);

                var dayTransferIn = points
                    .Where(p => p.AccountId == account.AccountId && p.Kind == "Transfer" && p.Amount > 0 && p.Date == date)
                    .Sum(p => p.Amount);

                var dayTransferOut = points
                    .Where(p => p.AccountId == account.AccountId && p.Kind == "Transfer" && p.Amount < 0 && p.Date == date)
                    .Sum(p => Math.Abs(p.Amount));

                balance = balance + dayIncome - dayExpense + dayTransferIn - dayTransferOut;

                var fxResult = ConvertToReporting(balance, account.CurrencyCode, reportingCurrency, date, fxConfig, fxGraph);

                seriesPoints.Add((date, Math.Round(balance, 2, MidpointRounding.AwayFromZero), fxResult.ReportingAmount));

                if (balance < 0)
                {
                    warnings.Add(new ForecastWarning("NegativeBalance",
                        $"Account '{account.Name}' projected negative ({balance:N2} {account.CurrencyCode}) on {date:yyyy-MM-dd}", date));
                }
            }

            result.Add(new CashBalanceSeries(account.AccountId, account.Name, account.CurrencyCode, seriesPoints));
        }

        return result;
    }

    private static IReadOnlyList<CashflowBreakdownRow> BuildCashflowBreakdown(List<ForecastPoint> points)
    {
        var groups = points
            .GroupBy(p => p.Kind)
            .Select(g => new CashflowBreakdownRow(
                g.Key,
                Math.Round(g.Sum(p => p.Amount), 2, MidpointRounding.AwayFromZero),
                Math.Round(g.Sum(p => p.ReportingAmount), 2, MidpointRounding.AwayFromZero)))
            .OrderBy(r => r.Category)
            .ToList();

        return groups;
    }

    private static IReadOnlyList<BudgetForecastRow> BuildBudgetForecast(
        BudgetState budgetState,
        CategoryState categoryState,
        RecurringState recurringState,
        ForecastHorizon horizon,
        HashSet<Guid> suppressedRecurring,
        IReadOnlyList<ForecastAdjustment>? scenarioAdjustments)
    {
        var rows = new List<BudgetForecastRow>();

        // Generate months in horizon
        var current = new DateOnly(horizon.StartDate.Year, horizon.StartDate.Month, 1);
        var end = new DateOnly(horizon.EndDate.Year, horizon.EndDate.Month, 1);

        while (current <= end)
        {
            var year = current.Year;
            var month = current.Month;
            var monthStart = new DateOnly(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            foreach (var budget in budgetState.Budgets.Values.Where(b => !b.IsArchived && b.PeriodYear == year && b.PeriodMonth == month))
            {
                var scopeLabel = "All Expenses";
                if (budget.ScopeType.Contains("category") && budget.CategoryId.HasValue)
                {
                    scopeLabel = categoryState.Categories.TryGetValue(budget.CategoryId.Value, out var cat)
                        ? cat.Name : budget.CategoryId.Value.ToString();
                }

                // Forecast expense for this month from recurring
                decimal forecastActual = 0m;
                foreach (var item in recurringState.Items.Values.Where(i => !i.IsArchived && i.Kind == "expense"))
                {
                    if (suppressedRecurring.Contains(item.RecurringId)) continue;

                    // Match budget scope
                    if (budget.ScopeType.Contains("category") && budget.CategoryId.HasValue && item.CategoryId != budget.CategoryId)
                        continue;
                    if (budget.ScopeType.Contains("account") && budget.AccountId.HasValue && item.AccountId != budget.AccountId)
                        continue;

                    var occurrences = ExpandRecurring(item, monthStart, monthEnd);
                    forecastActual += occurrences.Count * item.Amount;
                }

                // Add scenario one-time expenses
                if (scenarioAdjustments != null)
                {
                    foreach (var adj in scenarioAdjustments.Where(a =>
                        a.Kind == "Expense" && a.Date >= monthStart && a.Date <= monthEnd && a.RecurringIdToSuppress == null))
                    {
                        if (budget.ScopeType.Contains("category") && budget.CategoryId.HasValue)
                        {
                            if (budget.CategoryId.Value.ToString() == adj.Category || adj.Category == scopeLabel)
                                forecastActual += adj.Amount;
                        }
                        else
                        {
                            forecastActual += adj.Amount;
                        }
                    }
                }

                var remaining = budget.LimitAmount - forecastActual;
                var pct = budget.LimitAmount > 0 ? forecastActual / budget.LimitAmount * 100m : (forecastActual > 0 ? 100m : 0m);
                var status = pct >= 100m ? "Exceeded" : pct >= 80m ? "Warn" : "OK";

                rows.Add(new BudgetForecastRow(year, month, budget.CategoryId, scopeLabel,
                    budget.LimitAmount, Math.Round(forecastActual, 2, MidpointRounding.AwayFromZero),
                    Math.Round(remaining, 2, MidpointRounding.AwayFromZero), Math.Round(pct, 1), status));
            }

            current = current.AddMonths(1);
        }

        return rows;
    }

    private static IReadOnlyList<GoalForecastRow> BuildGoalForecast(
        GoalsState goalsState, RecurringState recurringState, ForecastHorizon horizon)
    {
        var rows = new List<GoalForecastRow>();

        foreach (var goal in goalsState.Goals.Values.Where(g => !g.IsArchived))
        {
            var contributed = goalsState.TotalContributed(goal.GoalId);
            var remaining = goalsState.RemainingAmount(goal.GoalId);
            var progress = goalsState.ProgressPercent(goal.GoalId);
            var estimated = goalsState.EstimatedCompletionDate(goal.GoalId, horizon.StartDate);
            var isKnown = remaining <= 0 || estimated.HasValue;

            rows.Add(new GoalForecastRow(
                goal.GoalId, goal.Name, goal.TargetAmount.Amount, contributed,
                remaining, progress, estimated, isKnown, goal.TargetAmount.Currency.Code));
        }

        return rows;
    }

    private static IReadOnlyList<DateOnly> GenerateDateBuckets(ForecastHorizon horizon)
    {
        var dates = new List<DateOnly>();
        var current = horizon.StartDate;

        while (current <= horizon.EndDate)
        {
            dates.Add(current);
            current = horizon.Granularity switch
            {
                ForecastGranularity.Daily => current.AddDays(1),
                ForecastGranularity.Weekly => current.AddDays(7),
                ForecastGranularity.Monthly => current.AddMonths(1),
                _ => current.AddDays(1)
            };
        }

        return dates;
    }

    private static (decimal ReportingAmount, bool IsKnown) ConvertToReporting(
        decimal amount, string fromCurrency, string reportingCurrency,
        DateOnly date, FxPolicyConfig config, FxGraph graph)
    {
        if (string.Equals(fromCurrency, reportingCurrency, StringComparison.OrdinalIgnoreCase))
            return (Math.Round(amount, 2, MidpointRounding.AwayFromZero), true);

        if (graph.TryGetRate(fromCurrency, reportingCurrency, date, config, out var rate, out _))
            return (Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero), true);

        return (0m, false);
    }
}
