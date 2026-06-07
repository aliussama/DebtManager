using System.Globalization;
using DebtManager.Domain.Projections;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure, deterministic generator for income by source reports.
/// Input: IncomeSourceState, CashLedgerState.
/// </summary>
public sealed class IncomeBySourceReportGenerator
{
    public GeneratedReport Generate(
        IncomeSourceState incomeSourceState,
        CashLedgerState cashLedger,
        ReportDefinition definition,
        DateTimeOffset generatedAt)
    {
        var from = definition.Parameters.FromDate ?? DateOnly.MinValue;
        var to = definition.Parameters.ToDate ?? DateOnly.MaxValue;
        var accountFilter = definition.Parameters.AccountIds;

        var incomeRows = cashLedger.Rows
            .Where(r => r.Direction == "In" && r.Amount > 0)
            .Where(r => r.Category == "Income")
            .Where(r => r.EffectiveDate >= from && r.EffectiveDate <= to)
            .Where(r => accountFilter == null || accountFilter.Count == 0 || accountFilter.Contains(r.AccountId))
            .ToList();

        // Match rows to defined sources
        var perSource = new Dictionary<Guid, (IncomeSourceRecord Source, decimal Total, int Count)>();
        decimal unclassifiedTotal = 0m;
        int unclassifiedCount = 0;

        foreach (var row in incomeRows)
        {
            // Try to match via SourceId on the row, or by name
            IncomeSourceRecord? matched = null;
            if (row.SourceId.HasValue)
                matched = incomeSourceState.TryGet(row.SourceId.Value);

            matched ??= incomeSourceState.FindByName(row.Reference);

            if (matched != null)
            {
                if (perSource.TryGetValue(matched.SourceId, out var existing))
                    perSource[matched.SourceId] = (existing.Source, existing.Total + row.Amount, existing.Count + 1);
                else
                    perSource[matched.SourceId] = (matched, row.Amount, 1);
            }
            else
            {
                unclassifiedTotal += row.Amount;
                unclassifiedCount++;
            }
        }

        var sections = new List<ReportSection>();

        // 1) Per-source totals table
        var sourceRows = perSource.Values
            .OrderByDescending(x => x.Total)
            .Select(x => (IReadOnlyList<string>)new[]
            {
                x.Source.Name,
                x.Source.SourceType.ToString(),
                x.Total.ToString("F2", CultureInfo.InvariantCulture),
                x.Count.ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        var sourceTable = new ReportTable(
            new[] { "Source", "Type", "Total", "Transactions" },
            sourceRows);

        sections.Add(new ReportSection("Income by Source", ReportSectionKind.Table, sourceTable));

        // 2) Unclassified bucket
        var unclassifiedSummary = new SummaryData(new List<SummaryLine>
        {
            new("Unclassified Total", unclassifiedTotal.ToString("F2", CultureInfo.InvariantCulture)),
            new("Unclassified Transactions", unclassifiedCount.ToString(CultureInfo.InvariantCulture))
        });

        sections.Add(new ReportSection("Unclassified Income", ReportSectionKind.Summary, unclassifiedSummary));

        // 3) Trend summary (monthly)
        var monthlyTrend = incomeRows
            .GroupBy(r => new { r.EffectiveDate.Year, r.EffectiveDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => (IReadOnlyList<string>)new[]
            {
                $"{g.Key.Year}-{g.Key.Month:D2}",
                g.Sum(r => r.Amount).ToString("F2", CultureInfo.InvariantCulture),
                g.Count().ToString(CultureInfo.InvariantCulture)
            })
            .ToList();

        var trendTable = new ReportTable(
            new[] { "Month", "Total", "Transactions" },
            monthlyTrend);

        sections.Add(new ReportSection("Monthly Income Trend", ReportSectionKind.Table, trendTable));

        return new GeneratedReport(definition, sections, generatedAt);
    }
}
