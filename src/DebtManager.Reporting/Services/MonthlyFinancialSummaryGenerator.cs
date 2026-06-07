using System.Globalization;
using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure, deterministic generator for monthly financial summary reports.
/// Input: CashLedgerState, BudgetState, CategoryState.
/// </summary>
public sealed class MonthlyFinancialSummaryGenerator
{
    public GeneratedReport Generate(
        CashLedgerState cashLedger,
        BudgetState budgetState,
        CategoryState categoryState,
        ReportDefinition definition,
        DateTimeOffset generatedAt)
    {
        var from = definition.Parameters.FromDate ?? DateOnly.MinValue;
        var to = definition.Parameters.ToDate ?? DateOnly.MaxValue;
        var accountFilter = definition.Parameters.AccountIds;
        var tagFilter = definition.Parameters.Tags;

        var filteredRows = cashLedger.Rows
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            .Where(r => accountFilter == null || accountFilter.Count == 0 || accountFilter.Contains(r.AccountId))
            .ToList();

        // 1) Income by category
        var incomeByCategory = filteredRows
            .Where(r => r.Direction == "In" && r.Amount > 0)
            .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "Uncategorized" : r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var incomeTable = new ReportTable(
            new[] { "Category", "Total" },
            incomeByCategory.Select(x => (IReadOnlyList<string>)new[] {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        // 2) Expense by category
        var expenseByCategory = filteredRows
            .Where(r => r.Direction == "Out" && r.Amount > 0)
            .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "Uncategorized" : r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var expenseTable = new ReportTable(
            new[] { "Category", "Total" },
            expenseByCategory.Select(x => (IReadOnlyList<string>)new[] {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        // 3) Net cashflow summary
        var totalIncome = filteredRows.Where(r => r.Direction == "In" && r.Amount > 0).Sum(r => r.Amount);
        var totalExpense = filteredRows.Where(r => r.Direction == "Out" && r.Amount > 0).Sum(r => r.Amount);
        var netCashflow = totalIncome - totalExpense;

        var cashflowSummary = new SummaryData(new List<SummaryLine>
        {
            new("Total Income", totalIncome.ToString("F2", CultureInfo.InvariantCulture)),
            new("Total Expenses", totalExpense.ToString("F2", CultureInfo.InvariantCulture)),
            new("Net Cashflow", netCashflow.ToString("F2", CultureInfo.InvariantCulture))
        });

        // 4) Budget utilization
        var budgetYear = from.Year > 1 ? from.Year : DateTime.Today.Year;
        var budgetMonth = from.Month > 0 ? from.Month : DateTime.Today.Month;
        var utilization = BudgetProjector.ComputeUtilization(budgetState, cashLedger, categoryState, budgetYear, budgetMonth);

        var budgetTable = new ReportTable(
            new[] { "Scope", "Limit", "Actual", "Remaining", "% Used", "Status" },
            utilization.Select(u => (IReadOnlyList<string>)new[] {
                u.ScopeLabel,
                u.LimitAmount.ToString("F2", CultureInfo.InvariantCulture),
                u.ActualAmount.ToString("F2", CultureInfo.InvariantCulture),
                u.RemainingAmount.ToString("F2", CultureInfo.InvariantCulture),
                u.PercentUsed.ToString("F1", CultureInfo.InvariantCulture),
                u.Status
            }).ToList());

        // 5) Top 5 expense categories
        var top5 = expenseByCategory.Take(5).ToList();
        var top5Table = new ReportTable(
            new[] { "Category", "Total" },
            top5.Select(x => (IReadOnlyList<string>)new[] {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        // 6) Month-over-month comparison
        var monthGroups = filteredRows
            .GroupBy(r => new { r.EffectiveDate.Year, r.EffectiveDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new
            {
                Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                Income = g.Where(r => r.Direction == "In" && r.Amount > 0).Sum(r => r.Amount),
                Expenses = g.Where(r => r.Direction == "Out" && r.Amount > 0).Sum(r => r.Amount)
            })
            .ToList();

        var momTable = new ReportTable(
            new[] { "Month", "Income", "Expenses", "Net" },
            monthGroups.Select(m => (IReadOnlyList<string>)new[] {
                m.Month,
                m.Income.ToString("F2", CultureInfo.InvariantCulture),
                m.Expenses.ToString("F2", CultureInfo.InvariantCulture),
                (m.Income - m.Expenses).ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        var sections = new List<ReportSection>
        {
            new("Income by Category", ReportSectionKind.Table, incomeTable),
            new("Expense by Category", ReportSectionKind.Table, expenseTable),
            new("Net Cashflow Summary", ReportSectionKind.Summary, cashflowSummary),
            new("Budget Utilization", ReportSectionKind.Table, budgetTable),
            new("Top 5 Expense Categories", ReportSectionKind.Table, top5Table),
            new("Month-over-Month Comparison", ReportSectionKind.Table, momTable)
        };

        return new GeneratedReport(definition, sections, generatedAt);
    }
}
