using System.Globalization;
using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure, deterministic generator for net worth history reports.
/// Input: NetWorthState.
/// </summary>
public sealed class NetWorthHistoryReportGenerator
{
    public GeneratedReport Generate(
        NetWorthState netWorthState,
        CashLedgerState cashLedger,
        ReportDefinition definition,
        DateTimeOffset generatedAt)
    {
        var from = definition.Parameters.FromDate ?? DateOnly.MinValue;
        var to = definition.Parameters.ToDate ?? DateOnly.MaxValue;

        var sections = new List<ReportSection>();

        // 1) Monthly net worth trend from cash ledger snapshots
        var filteredRows = cashLedger.Rows
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            .ToList();

        var monthlySnapshots = filteredRows
            .GroupBy(r => new { r.EffectiveDate.Year, r.EffectiveDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g =>
            {
                var income = g.Where(r => r.Direction == "In" && r.Amount > 0).Sum(r => r.Amount);
                var expense = g.Where(r => r.Direction == "Out" && r.Amount > 0).Sum(r => r.Amount);
                return new
                {
                    Month = $"{g.Key.Year}-{g.Key.Month:D2}",
                    CumulativeNet = income - expense
                };
            })
            .ToList();

        // Build running total for trend
        decimal running = 0m;
        var trendRows = new List<IReadOnlyList<string>>();
        foreach (var snap in monthlySnapshots)
        {
            running += snap.CumulativeNet;
            trendRows.Add(new[]
            {
                snap.Month,
                running.ToString("F2", CultureInfo.InvariantCulture),
                snap.CumulativeNet.ToString("F2", CultureInfo.InvariantCulture)
            });
        }

        var trendTable = new ReportTable(
            new[] { "Month", "Net Worth", "Monthly Delta" },
            trendRows);

        sections.Add(new ReportSection("Monthly Net Worth Trend", ReportSectionKind.Table, trendTable));

        // 2) Breakdown by asset/liability category from NetWorthState
        var breakdownByCategory = netWorthState.Rows
            .GroupBy(r => r.Category)
            .Select(g => new
            {
                Category = g.Key,
                Total = g.Sum(r => r.ReportingAmount)
            })
            .OrderByDescending(x => x.Total)
            .ToList();

        var breakdownTable = new ReportTable(
            new[] { "Category", "Total" },
            breakdownByCategory.Select(x => (IReadOnlyList<string>)new[]
            {
                x.Category,
                x.Total.ToString("F2", CultureInfo.InvariantCulture)
            }).ToList());

        sections.Add(new ReportSection("Breakdown by Category", ReportSectionKind.Table, breakdownTable));

        // 3) Summary
        var summary = new SummaryData(new List<SummaryLine>
        {
            new("Total Assets", netWorthState.TotalAssets.ToString("F2", CultureInfo.InvariantCulture)),
            new("Total Liabilities", netWorthState.TotalLiabilities.ToString("F2", CultureInfo.InvariantCulture)),
            new("Known Net Worth", netWorthState.KnownNetWorth.ToString("F2", CultureInfo.InvariantCulture)),
            new("Reporting Currency", netWorthState.ReportingCurrency),
            new("As Of Date", netWorthState.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        });

        sections.Add(new ReportSection("Net Worth Summary", ReportSectionKind.Summary, summary));

        return new GeneratedReport(definition, sections, generatedAt);
    }
}
