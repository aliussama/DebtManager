using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Reporting.Models;
using DebtManager.Reporting.Services;

namespace DebtManager.Application.UseCases;

// --- DTOs ---

public sealed record AvailableReportDto(
    string ReportId,
    string Title,
    string Category
);

// --- Handlers ---

/// <summary>
/// Returns the list of available report definitions.
/// </summary>
public sealed class GetAvailableReportsHandler
{
    public Task<IReadOnlyList<AvailableReportDto>> HandleAsync(CancellationToken ct)
    {
        var reports = ReportOrchestrator.AvailableReports
            .Select(r => new AvailableReportDto(r.ReportId, r.Title, r.Category))
            .ToList();

        return Task.FromResult<IReadOnlyList<AvailableReportDto>>(reports);
    }
}

/// <summary>
/// Generates a report by loading projection states and dispatching to the orchestrator.
/// </summary>
public sealed class GenerateReportHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner _runner;

    public GenerateReportHandler(IEventStore store, ProjectionRunner runner)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<GeneratedReport> HandleAsync(
        ReportDefinition definition,
        DateTimeOffset generatedAt,
        CancellationToken ct)
    {
        // Load all events once
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        // Build projection states deterministically
        var cashLedger = await _runner.RunAsync(
            "CashLedger",
            evts => CashLedgerProjector.Project(evts),
            ct: ct);

        var budgetState = await _runner.RunAsync(
            "Budget",
            evts => BudgetProjector.Project(evts),
            ct: ct);

        var categoryState = await _runner.RunAsync(
            "Category",
            evts => CategoryProjector.Project(evts),
            ct: ct);

        var incomeSourceState = await _runner.RunAsync(
            "IncomeSource",
            evts => IncomeSourceProjector.Project(evts),
            ct: ct);

        // NetWorthState — build a basic one from cash ledger
        var netWorthState = BuildNetWorthFromCashLedger(cashLedger);

        // FinancialState — build from financial projector
        var financialState = await _runner.RunAsync(
            "Financial",
            evts => BuildFinancialState(evts),
            ct: ct);

        var bundle = new ReportOrchestrator.ProjectionBundle
        {
            CashLedger = cashLedger,
            Budget = budgetState,
            Category = categoryState,
            Financial = financialState,
            Installments = financialState?.Installments.AsReadOnly() ?? (IReadOnlyList<Domain.Projections.Installments.InstallmentState>)Array.Empty<Domain.Projections.Installments.InstallmentState>(),
            NetWorth = netWorthState,
            IncomeSource = incomeSourceState
        };

        var orchestrator = new ReportOrchestrator();
        return orchestrator.Generate(definition, bundle, generatedAt);
    }

    private static NetWorthState BuildNetWorthFromCashLedger(CashLedgerState cashLedger)
    {
        var state = new NetWorthState
        {
            AsOfDate = DateOnly.FromDateTime(DateTime.Today),
            ReportingCurrency = "EGP"
        };

        decimal totalCash = 0m;
        foreach (var acct in cashLedger.Accounts.Values.Where(a => !a.IsArchived))
        {
            totalCash += acct.Balance;
            state.Rows.Add(new NetWorthBreakdownRow
            {
                Category = "Cash",
                SubCategory = acct.AccountType,
                Name = acct.Name,
                ReferenceId = acct.AccountId,
                NativeCurrencyCode = acct.CurrencyCode,
                NativeAmount = acct.Balance,
                ReportingCurrencyCode = acct.CurrencyCode,
                ReportingAmount = acct.Balance
            });
        }

        state.TotalCash = totalCash;
        state.TotalAssets = totalCash;
        state.KnownNetWorth = totalCash;

        return state;
    }

    private static FinancialState? BuildFinancialState(IEnumerable<EventEnvelope> envelopes)
    {
        var state = new FinancialState(Currency.EGP);
        // Minimal projection — obligations already tracked by other projectors
        return state;
    }
}
