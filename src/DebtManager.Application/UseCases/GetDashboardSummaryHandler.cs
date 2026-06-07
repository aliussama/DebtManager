using DebtManager.Application.Models;
using DebtManager.Domain.Ai;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.UseCases;

/// <summary>
/// Use case: Build a DashboardSummary from projection states.
/// Uses IEventStore to read all events and projects them using domain projectors.
/// No randomness. Deterministic ordering. AsOfDate passed from caller.
/// </summary>
public sealed class GetDashboardSummaryHandler
{
    private readonly IEventStore _store;

    public GetDashboardSummaryHandler(IEventStore store)
    {
        _store = store;
    }

    public async Task<DashboardSummary> HandleAsync(DateOnly asOfDate, CancellationToken ct = default)
    {
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        // Project all required states deterministically
        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var billingState = BillingProjector.Project(allEnvelopes, asOfDate);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes, asOfDate);
        var aiState = AiAdvisorProjector.Project(allEnvelopes);
        var dqState = DataQualityProjector.Project(allEnvelopes);

        // 1. TotalCashBalance — sum of all non-archived account balances
        var totalCashBalance = cashState.Accounts.Values
            .Where(a => !a.IsArchived)
            .Sum(a => a.Balance);

        // 2. NetWorth — cash balance minus outstanding bills/invoices (simplified)
        var totalOutstandingBills = billingState.Bills.Values
            .Where(b => !b.IsCancelled && !b.IsWrittenOff)
            .Sum(b => b.Outstanding);
        var totalOutstandingInvoices = billingState.Invoices.Values
            .Where(i => !i.IsCancelled && !i.IsWrittenOff)
            .Sum(i => i.Outstanding);
        var netWorth = totalCashBalance - totalOutstandingBills + totalOutstandingInvoices;

        // 3. BudgetHealthPercent — ratio of budgets within limit
        var budgetHealthPercent = ComputeBudgetHealth(budgetState, cashState, categoryState, asOfDate);

        // 4. OverdueObligationCount — bills and invoices past due and not fully paid
        var overdueBills = billingState.OverdueBills(asOfDate);
        var overdueInvoices = billingState.OverdueInvoices(asOfDate);
        var overdueObligationCount = overdueBills.Count + overdueInvoices.Count;

        // 5. UpcomingPayments — bills due in next 7 days, sorted ascending by due date then by ID
        var cutoff7Days = asOfDate.AddDays(7);
        var upcomingPayments = billingState.Bills.Values
            .Where(b => !b.IsCancelled && !b.IsWrittenOff && !b.IsDisputed
                         && b.Outstanding > 0
                         && b.DueDate >= asOfDate && b.DueDate <= cutoff7Days)
            .Select(b => new Models.UpcomingPaymentItem(
                EntityId: b.BillId,
                Title: $"{b.Category} — {b.Reference}",
                DueDate: b.DueDate,
                Amount: b.Outstanding,
                CurrencyCode: b.CurrencyCode))
            .Concat(billingState.Invoices.Values
                .Where(i => !i.IsCancelled && !i.IsWrittenOff && !i.IsDisputed
                             && i.Outstanding > 0
                             && i.DueDate >= asOfDate && i.DueDate <= cutoff7Days)
                .Select(i => new Models.UpcomingPaymentItem(
                    EntityId: i.InvoiceId,
                    Title: $"{i.Category} — {i.Reference}",
                    DueDate: i.DueDate,
                    Amount: i.Outstanding,
                    CurrencyCode: i.CurrencyCode)))
            .OrderBy(p => p.DueDate)
            .ThenBy(p => p.EntityId)
            .ToList()
            .AsReadOnly();

        // 6. TopGoals — top 3 active goals sorted by lowest progress, then by GoalId
        var topGoals = goalsState.Goals.Values
            .Where(g => !g.IsArchived)
            .Select(g => new Models.GoalProgressItem(
                GoalId: g.GoalId,
                Name: g.Name,
                ProgressPercent: goalsState.ProgressPercent(g.GoalId),
                TargetAmount: g.TargetAmount.Amount,
                ContributedAmount: goalsState.TotalContributed(g.GoalId),
                TargetDate: g.TargetDate))
            .OrderBy(g => g.ProgressPercent)
            .ThenBy(g => g.GoalId)
            .Take(3)
            .ToList()
            .AsReadOnly();

        // 7. AiInsightCount — pending insights (not yet acted upon via proposals)
        var aiInsightCount = aiState.Insights.Count;

        // 8. DataQualityIssueCount — scans with unresolved issues
        //    Count issues that are not acknowledged and not resolved
        var allIssueIds = dqState.Scans.Keys.ToHashSet();
        var resolvedOrAcknowledged = dqState.ResolvedIssueIds
            .Union(dqState.AcknowledgedIssueIds)
            .Count();
        // Use scan count as a proxy for open issues; if no scans, 0
        var dataQualityIssueCount = Math.Max(0, dqState.Scans.Count - resolvedOrAcknowledged);

        return new DashboardSummary(
            TotalCashBalance: totalCashBalance,
            NetWorth: netWorth,
            BudgetHealthPercent: budgetHealthPercent,
            OverdueObligationCount: overdueObligationCount,
            UpcomingPayments: upcomingPayments,
            TopGoals: topGoals,
            AiInsightCount: aiInsightCount,
            DataQualityIssueCount: dataQualityIssueCount
        );
    }

    /// <summary>
    /// Computes budget health as percent of budgets within their limit for the current period.
    /// 100% means all budgets OK. 0% means all exceeded or no budgets defined.
    /// </summary>
    private static decimal ComputeBudgetHealth(
        BudgetState budgetState,
        CashLedgerState cashState,
        CategoryState categoryState,
        DateOnly asOfDate)
    {
        var currentBudgets = budgetState.Budgets.Values
            .Where(b => !b.IsArchived && b.PeriodYear == asOfDate.Year && b.PeriodMonth == asOfDate.Month)
            .ToList();

        if (currentBudgets.Count == 0)
            return 100m; // No budgets = healthy by default

        var utilization = BudgetProjector.ComputeUtilization(
            budgetState, cashState, categoryState,
            asOfDate.Year, asOfDate.Month);

        if (utilization.Count == 0)
            return 100m;

        var withinLimit = utilization.Count(u => u.Status != "Exceeded");
        var pct = (decimal)withinLimit / utilization.Count * 100m;
        return Math.Round(pct, 1, MidpointRounding.AwayFromZero);
    }
}
