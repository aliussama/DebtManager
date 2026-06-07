using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Domain.Projections;

/// <summary>
/// State of a single budget definition.
/// </summary>
public sealed class BudgetItem
{
    public Guid BudgetId { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string ScopeType { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public Guid? AccountId { get; set; }
    public decimal LimitAmount { get; set; }
    public string CarryPolicy { get; set; } = "None";
    public bool IsArchived { get; set; }
}

/// <summary>
/// A single row in the budget utilization report.
/// </summary>
public sealed record BudgetUtilizationRow(
    Guid BudgetId,
    string ScopeLabel,
    string CurrencyCode,
    decimal LimitAmount,
    decimal ActualAmount,
    decimal RemainingAmount,
    decimal PercentUsed,
    string Status // "OK", "Warn", "Exceeded"
);

/// <summary>
/// Full budget state derived from events.
/// </summary>
public sealed class BudgetState
{
    public Dictionary<Guid, BudgetItem> Budgets { get; } = new();
}

/// <summary>
/// Projects budget events and computes utilization against cash ledger data.
/// </summary>
public static class BudgetProjector
{
    public static BudgetState Project(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new BudgetState();
        var opt = DomainJson.Options;

        var ordered = envelopes
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value);

        foreach (var env in ordered)
        {
            switch (env.EventType)
            {
                case nameof(BudgetDefined):
                {
                    var ev = JsonSerializer.Deserialize<BudgetDefined>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    state.Budgets[ev.BudgetId] = new BudgetItem
                    {
                        BudgetId = ev.BudgetId,
                        PeriodYear = ev.PeriodYear,
                        PeriodMonth = ev.PeriodMonth,
                        CurrencyCode = ev.CurrencyCode,
                        ScopeType = ev.ScopeType,
                        CategoryId = ev.CategoryId,
                        AccountId = ev.AccountId,
                        LimitAmount = ev.LimitAmount,
                        CarryPolicy = ev.CarryPolicy
                    };
                    break;
                }
                case nameof(BudgetAdjusted):
                {
                    var ev = JsonSerializer.Deserialize<BudgetAdjusted>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Budgets.TryGetValue(ev.BudgetId, out var b))
                        b.LimitAmount = ev.NewLimitAmount;
                    break;
                }
                case nameof(BudgetArchived):
                {
                    var ev = JsonSerializer.Deserialize<BudgetArchived>(env.PayloadJson, opt);
                    if (ev == null) continue;
                    if (state.Budgets.TryGetValue(ev.BudgetId, out var b))
                        b.IsArchived = true;
                    break;
                }
            }
        }

        return state;
    }

    /// <summary>
    /// Computes budget utilization for a given period using cash ledger rows.
    /// </summary>
    public static IReadOnlyList<BudgetUtilizationRow> ComputeUtilization(
        BudgetState budgetState,
        CashLedgerState ledgerState,
        CategoryState categoryState,
        int year, int month,
        BudgetState? priorBudgetState = null,
        CashLedgerState? priorLedgerState = null)
    {
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);

        var rows = ledgerState.Rows
            .Where(r => r.EffectiveDate >= periodStart && r.EffectiveDate <= periodEnd)
            .ToList();

        var result = new List<BudgetUtilizationRow>();

        foreach (var budget in budgetState.Budgets.Values.Where(b =>
            !b.IsArchived && b.PeriodYear == year && b.PeriodMonth == month))
        {
            var matchingRows = rows.Where(r => r.Direction == "Out").AsEnumerable();

            string scopeLabel = "All Expenses";

            if (budget.ScopeType.Contains("category") && budget.CategoryId.HasValue)
            {
                var catName = categoryState.Categories.TryGetValue(budget.CategoryId.Value, out var cat)
                    ? cat.Name : budget.CategoryId.Value.ToString();

                matchingRows = matchingRows.Where(r =>
                    r.Category.Equals(catName, StringComparison.OrdinalIgnoreCase));
                scopeLabel = catName;
            }

            if (budget.ScopeType.Contains("account") && budget.AccountId.HasValue)
            {
                matchingRows = matchingRows.Where(r => r.AccountId == budget.AccountId.Value);
                var acctName = ledgerState.Accounts.TryGetValue(budget.AccountId.Value, out var acct)
                    ? acct.Name : "Account";
                scopeLabel = budget.ScopeType.Contains("category") ? $"{scopeLabel} ({acctName})" : acctName;
            }

            var actual = matchingRows.Sum(r => r.Amount);

            // Apply carry policy from prior period
            var effectiveLimit = budget.LimitAmount;
            if (priorBudgetState != null && priorLedgerState != null)
            {
                effectiveLimit = ApplyCarryPolicy(budget, priorBudgetState, priorLedgerState, categoryState, effectiveLimit);
            }

            var remaining = effectiveLimit - actual;
            var pct = effectiveLimit > 0 ? (actual / effectiveLimit) * 100m : (actual > 0 ? 100m : 0m);
            var status = pct >= 100m ? "Exceeded" : pct >= 80m ? "Warn" : "OK";

            result.Add(new BudgetUtilizationRow(
                budget.BudgetId, scopeLabel, budget.CurrencyCode,
                effectiveLimit, actual, remaining, Math.Round(pct, 1), status));
        }

        return result;
    }

    private static decimal ApplyCarryPolicy(
        BudgetItem currentBudget,
        BudgetState priorBudgetState,
        CashLedgerState priorLedgerState,
        CategoryState categoryState,
        decimal currentLimit)
    {
        // Find the prior period budget with same scope
        var priorYear = currentBudget.PeriodMonth == 1 ? currentBudget.PeriodYear - 1 : currentBudget.PeriodYear;
        var priorMonth = currentBudget.PeriodMonth == 1 ? 12 : currentBudget.PeriodMonth - 1;

        var priorBudget = priorBudgetState.Budgets.Values.FirstOrDefault(b =>
            !b.IsArchived &&
            b.PeriodYear == priorYear && b.PeriodMonth == priorMonth &&
            b.ScopeType == currentBudget.ScopeType &&
            b.CategoryId == currentBudget.CategoryId &&
            b.AccountId == currentBudget.AccountId);

        if (priorBudget == null) return currentLimit;

        // Compute prior actual
        var priorStart = new DateOnly(priorYear, priorMonth, 1);
        var priorEnd = priorStart.AddMonths(1).AddDays(-1);
        var priorRows = priorLedgerState.Rows
            .Where(r => r.EffectiveDate >= priorStart && r.EffectiveDate <= priorEnd && r.Direction == "Out")
            .AsEnumerable();

        if (priorBudget.ScopeType.Contains("category") && priorBudget.CategoryId.HasValue)
        {
            var catName = categoryState.Categories.TryGetValue(priorBudget.CategoryId.Value, out var cat)
                ? cat.Name : priorBudget.CategoryId.Value.ToString();
            priorRows = priorRows.Where(r => r.Category.Equals(catName, StringComparison.OrdinalIgnoreCase));
        }
        if (priorBudget.ScopeType.Contains("account") && priorBudget.AccountId.HasValue)
            priorRows = priorRows.Where(r => r.AccountId == priorBudget.AccountId.Value);

        var priorActual = priorRows.Sum(r => r.Amount);
        var priorRemaining = priorBudget.LimitAmount - priorActual;

        return currentBudget.CarryPolicy switch
        {
            "CarryUnused" when priorRemaining > 0 => currentLimit + priorRemaining,
            "CarryOverspend" when priorRemaining < 0 => currentLimit + priorRemaining, // reduces limit
            _ => currentLimit
        };
    }
}
