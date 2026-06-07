using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Installments;
using DebtManager.Reporting.Models;

namespace DebtManager.Reporting.Services;

/// <summary>
/// Dispatches report generation to the correct generator based on ReportDefinition.ReportId.
/// Pure orchestration — no IO, no state.
/// </summary>
public sealed class ReportOrchestrator
{
    /// <summary>
    /// All available report definitions.
    /// </summary>
    public static IReadOnlyList<ReportDefinition> AvailableReports { get; } = new List<ReportDefinition>
    {
        new("monthly-financial-summary", "Monthly Financial Summary", "Finance", new ReportParameterSet()),
        new("obligation-payoff", "Obligation Payoff Report", "Obligations", new ReportParameterSet()),
        new("net-worth-history", "Net Worth History", "Net Worth", new ReportParameterSet()),
        new("cash-flow-statement", "Cash Flow Statement", "Finance", new ReportParameterSet()),
        new("income-by-source", "Income by Source", "Income", new ReportParameterSet())
    };

    /// <summary>
    /// All projection states needed by all reports.
    /// Supplied by the Application layer.
    /// </summary>
    public sealed class ProjectionBundle
    {
        public CashLedgerState CashLedger { get; init; } = new();
        public BudgetState Budget { get; init; } = new();
        public CategoryState Category { get; init; } = new();
        public FinancialState? Financial { get; init; }
        public IReadOnlyList<InstallmentState> Installments { get; init; } = Array.Empty<InstallmentState>();
        public NetWorthState NetWorth { get; init; } = new();
        public IncomeSourceState IncomeSource { get; init; } = new();
    }

    /// <summary>
    /// Generate a report given its definition and all projection states.
    /// </summary>
    public GeneratedReport Generate(
        ReportDefinition definition,
        ProjectionBundle projections,
        DateTimeOffset generatedAt)
    {
        return definition.ReportId switch
        {
            "monthly-financial-summary" => new MonthlyFinancialSummaryGenerator()
                .Generate(projections.CashLedger, projections.Budget, projections.Category, definition, generatedAt),

            "obligation-payoff" when projections.Financial != null =>
                new ObligationPayoffReportGenerator()
                    .Generate(projections.Financial, projections.Installments, definition, generatedAt),

            "obligation-payoff" =>
                new GeneratedReport(definition, new List<ReportSection>
                {
                    new("No Data", ReportSectionKind.Summary,
                        new SummaryData(new List<SummaryLine> { new("Status", "No financial data available") }))
                }, generatedAt),

            "net-worth-history" => new NetWorthHistoryReportGenerator()
                .Generate(projections.NetWorth, projections.CashLedger, definition, generatedAt),

            "cash-flow-statement" => new CashFlowStatementGenerator()
                .Generate(projections.CashLedger, definition, generatedAt),

            "income-by-source" => new IncomeBySourceReportGenerator()
                .Generate(projections.IncomeSource, projections.CashLedger, definition, generatedAt),

            _ => throw new InvalidOperationException($"Unknown report: {definition.ReportId}")
        };
    }
}
