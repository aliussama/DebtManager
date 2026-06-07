using System.Globalization;
using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure, deterministic generator for cash flow statement reports.
/// Input: CashLedgerState.
/// </summary>
public sealed class CashFlowStatementGenerator
{
    public GeneratedReport Generate(
        CashLedgerState cashLedger,
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

        // Operating income: In rows
        var operatingIncome = filteredRows
            .Where(r => r.Direction == "In" && r.Amount > 0)
            .ToList();

        var operatingIncomeByCategory = operatingIncome
            .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "Other Income" : r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var incomeTable = new ReportTable(
            new[] { "Category", "Total" },
            operatingIncomeByCategory.Select(x => (IReadOnlyList<string>)new[]
            {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        // Operating expenses: Out rows (excluding transfers)
        var operatingExpenses = filteredRows
            .Where(r => r.Direction == "Out" && r.Amount > 0)
            .ToList();

        var operatingExpensesByCategory = operatingExpenses
            .GroupBy(r => string.IsNullOrEmpty(r.Category) ? "Other Expense" : r.Category)
            .Select(g => new { Category = g.Key, Total = g.Sum(r => r.Amount) })
            .OrderByDescending(x => x.Total)
            .ToList();

        var expenseTable = new ReportTable(
            new[] { "Category", "Total" },
            operatingExpensesByCategory.Select(x => (IReadOnlyList<string>)new[]
            {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        // Transfers
        var transfers = filteredRows
            .Where(r => r.Direction == "Transfer")
            .ToList();

        var transferTotal = transfers.Sum(r => r.Amount);

        var transferTable = new ReportTable(
            new[] { "From", "To", "Amount", "Date" },
            transfers.Select(r => (IReadOnlyList<string>)new[]
            {
                r.AccountName,
                r.RelatedAccountName,
                r.Amount.ToString("F2", CultureInfo.InvariantCulture),
                r.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            }).ToList());

        // Net position
        var totalIn = operatingIncome.Sum(r => r.Amount);
        var totalOut = operatingExpenses.Sum(r => r.Amount);
        var netPosition = totalIn - totalOut;

        var summary = new SummaryData(new List<SummaryLine>
        {
            new("Operating Income", totalIn.ToString("F2", CultureInfo.InvariantCulture)),
            new("Operating Expenses", totalOut.ToString("F2", CultureInfo.InvariantCulture)),
            new("Net Transfers", transferTotal.ToString("F2", CultureInfo.InvariantCulture)),
            new("Net Position", netPosition.ToString("F2", CultureInfo.InvariantCulture))
        });

        var sections = new List<ReportSection>
        {
            new("Operating Income", ReportSectionKind.Table, incomeTable),
            new("Operating Expenses", ReportSectionKind.Table, expenseTable),
            new("Transfers", ReportSectionKind.Table, transferTable),
            new("Cash Flow Summary", ReportSectionKind.Summary, summary)
        };

        return new GeneratedReport(definition, sections, generatedAt);
    }
}
