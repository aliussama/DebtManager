using System.Globalization;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Pure, deterministic generator for obligation payoff reports.
/// Input: FinancialState, InstallmentState projections.
/// </summary>
public sealed class ObligationPayoffReportGenerator
{
    public GeneratedReport Generate(
        FinancialState financialState,
        IReadOnlyList<InstallmentState> installments,
        ReportDefinition definition,
        DateTimeOffset generatedAt)
    {
        var from = definition.Parameters.FromDate ?? DateOnly.MinValue;
        var to = definition.Parameters.ToDate ?? DateOnly.MaxValue;
        var accountFilter = definition.Parameters.AccountIds;

        var sections = new List<ReportSection>();

        var obligations = financialState.Obligations.Values.ToList();
        if (accountFilter != null && accountFilter.Count > 0)
            obligations = obligations.Where(o => accountFilter.Contains(o.ObligationId)).ToList();

        var tableRows = new List<IReadOnlyList<string>>();

        foreach (var ob in obligations.OrderBy(o => o.Name))
        {
            var obInstallments = installments
                .Where(i => i.ObligationId == ob.ObligationId)
                .ToList();

            var principal = ob.Principal.Amount;
            var totalPaid = ob.TotalPaid.Amount;
            var remaining = principal - totalPaid;
            if (remaining < 0) remaining = 0;

            var nextDue = obInstallments
                .Where(i => i.Status != InstallmentStatus.Paid && i.DueDate >= from)
                .OrderBy(i => i.DueDate)
                .FirstOrDefault();

            var nextDueDate = nextDue?.DueDate;

            var unpaidInstallments = obInstallments
                .Where(i => i.Status != InstallmentStatus.Paid)
                .OrderBy(i => i.DueDate)
                .ToList();

            var projectedPayoffDate = unpaidInstallments.LastOrDefault()?.DueDate;

            var totalInterest = financialState.Charges
                .Where(c => c.Type == Domain.Projections.Charges.ChargeType.Interest)
                .Sum(c => c.Amount.Amount);

            tableRows.Add(new[]
            {
                ob.Name,
                principal.ToString("F2", CultureInfo.InvariantCulture),
                totalPaid.ToString("F2", CultureInfo.InvariantCulture),
                remaining.ToString("F2", CultureInfo.InvariantCulture),
                nextDueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "N/A",
                projectedPayoffDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "N/A",
                totalInterest.ToString("F2", CultureInfo.InvariantCulture)
            });
        }

        var table = new ReportTable(
            new[] { "Obligation", "Principal", "Total Paid", "Remaining", "Next Due", "Projected Payoff", "Total Interest" },
            tableRows);

        sections.Add(new ReportSection("Obligation Payoff Details", ReportSectionKind.Table, table));

        // Summary
        var grandPrincipal = obligations.Sum(o => o.Principal.Amount);
        var grandPaid = obligations.Sum(o => o.TotalPaid.Amount);
        var grandRemaining = grandPrincipal - grandPaid;
        if (grandRemaining < 0) grandRemaining = 0;

        var summary = new SummaryData(new List<SummaryLine>
        {
            new("Total Principal", grandPrincipal.ToString("F2", CultureInfo.InvariantCulture)),
            new("Total Paid", grandPaid.ToString("F2", CultureInfo.InvariantCulture)),
            new("Total Remaining", grandRemaining.ToString("F2", CultureInfo.InvariantCulture)),
            new("Obligation Count", obligations.Count.ToString(CultureInfo.InvariantCulture))
        });

        sections.Add(new ReportSection("Payoff Summary", ReportSectionKind.Summary, summary));

        return new GeneratedReport(definition, sections, generatedAt);
    }
}
