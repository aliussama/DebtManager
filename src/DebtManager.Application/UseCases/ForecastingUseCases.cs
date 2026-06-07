using DebtManager.Application.Projections;
using DebtManager.Domain.Events;
using DebtManager.Domain.Forecasting;
using DebtManager.Domain.Fx;
using DebtManager.Domain.Projections;

namespace DebtManager.Application.UseCases;

// --- DTOs ---

public sealed record ForecastReportDto(
    ForecastHorizon Horizon,
    string ReportingCurrency,
    ForecastSummary Summary,
    IReadOnlyList<CashBalanceSeries> BalanceSeries,
    IReadOnlyList<CashflowBreakdownRow> CashflowRows,
    IReadOnlyList<BudgetForecastRow> BudgetRows,
    IReadOnlyList<DebtForecastRow> DebtRows,
    IReadOnlyList<GoalForecastRow> GoalRows
);

public sealed record ScenarioForecastComparisonDto(
    ForecastReportDto Baseline,
    ForecastReportDto Scenario,
    decimal BaselineEndBalance,
    decimal ScenarioEndBalance,
    decimal DeltaEndBalance,
    decimal BaselineNetCashflow,
    decimal ScenarioNetCashflow,
    decimal DeltaNetCashflow
);

public sealed record ForecastDashboardDto(
    decimal CashRunwayDays,
    decimal NextMonthNetCashflow,
    int UpcomingRecurringCount,
    IReadOnlyList<ForecastWarning> TopWarnings
);

// --- Commands ---

public sealed record GetBaselineForecastQuery(
    DateOnly StartDate,
    DateOnly EndDate,
    ForecastGranularity Granularity = ForecastGranularity.Monthly
);

public sealed record GetScenarioForecastQuery(Guid ScenarioId);

// --- Handlers ---

public sealed class GetBaselineForecastHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;

    public GetBaselineForecastHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<ForecastReportDto> HandleAsync(GetBaselineForecastQuery query, CancellationToken ct)
    {
        var horizon = new ForecastHorizon(query.StartDate, query.EndDate, query.Granularity);

        CashLedgerState cashState;
        RecurringState recurringState;
        BudgetState budgetState;
        GoalsState goalsState;
        CategoryState categoryState;
        CurrencySettingsState settingsState;
        AssetsState assetsState;

        if (_runner != null)
        {
            cashState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e, query.StartDate),
                asOfDate: query.StartDate, ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, query.StartDate),
                asOfDate: query.StartDate, ct: ct);

            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            recurringState = RecurringProjector.Project(envelopes);
            budgetState = BudgetProjector.Project(envelopes);
            goalsState = GoalsProjector.Project(envelopes);
            categoryState = CategoryProjector.Project(envelopes);
            settingsState = CurrencySettingsProjector.Project(envelopes);
        }
        else
        {
            var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
            cashState = CashLedgerProjector.Project(envelopes, query.StartDate);
            recurringState = RecurringProjector.Project(envelopes);
            budgetState = BudgetProjector.Project(envelopes);
            goalsState = GoalsProjector.Project(envelopes);
            categoryState = CategoryProjector.Project(envelopes);
            settingsState = CurrencySettingsProjector.Project(envelopes);
            assetsState = AssetsProjector.Project(envelopes, query.StartDate);
        }

        var reportCcy = settingsState.ReportingCurrencyCode;
        var fxConfig = new FxPolicyConfig(settingsState.Policy, settingsState.MaxAgeDays);
        var fxGraph = FxGraph.Build(assetsState.FxRates);

        var debtRows = new List<DebtForecastRow>(); // Debt data from obligations — simplified for baseline

        var report = ForecastEngine.BuildBaselineForecast(
            horizon, cashState, recurringState, budgetState, goalsState, categoryState,
            debtRows, reportCcy, fxConfig, fxGraph);

        return new ForecastReportDto(report.Horizon, report.ReportingCurrency, report.Summary,
            report.BalanceSeries, report.CashflowRows, report.BudgetRows, report.DebtRows, report.GoalRows);
    }
}

public sealed class GetScenarioForecastHandler
{
    private readonly IEventStore _store;
    private readonly ProjectionRunner? _runner;

    public GetScenarioForecastHandler(IEventStore store, ProjectionRunner? runner = null)
    {
        _store = store;
        _runner = runner;
    }

    public async Task<ScenarioForecastComparisonDto> HandleAsync(GetScenarioForecastQuery query, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var scenarioState = ScenarioProjector.Project(envelopes);

        if (!scenarioState.Scenarios.TryGetValue(query.ScenarioId, out var scenario))
            throw new InvalidOperationException($"Scenario {query.ScenarioId} not found.");

        var horizon = new ForecastHorizon(scenario.HorizonStart, scenario.HorizonEnd, scenario.Granularity);

        CashLedgerState cashState;
        AssetsState assetsState;

        if (_runner != null)
        {
            cashState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                e => CashLedgerProjector.Project(e, scenario.HorizonStart),
                asOfDate: scenario.HorizonStart, ct: ct);
            assetsState = await _runner.RunAsync(
                nameof(ProjectionCachePolicies.SchemaVersions.AssetsState),
                e => AssetsProjector.Project(e, scenario.HorizonStart),
                asOfDate: scenario.HorizonStart, ct: ct);
        }
        else
        {
            cashState = CashLedgerProjector.Project(envelopes, scenario.HorizonStart);
            assetsState = AssetsProjector.Project(envelopes, scenario.HorizonStart);
        }

        var recurringState = RecurringProjector.Project(envelopes);
        var budgetState = BudgetProjector.Project(envelopes);
        var goalsState = GoalsProjector.Project(envelopes);
        var categoryState = CategoryProjector.Project(envelopes);
        var settingsState = CurrencySettingsProjector.Project(envelopes);

        var reportCcy = settingsState.ReportingCurrencyCode;
        var fxConfig = new FxPolicyConfig(settingsState.Policy, settingsState.MaxAgeDays);
        var fxGraph = FxGraph.Build(assetsState.FxRates);
        var debtRows = new List<DebtForecastRow>();

        // Baseline
        var baseline = ForecastEngine.BuildBaselineForecast(
            horizon, cashState, recurringState, budgetState, goalsState, categoryState,
            debtRows, reportCcy, fxConfig, fxGraph);

        // Build scenario definition from projected state
        var activeChanges = scenario.Changes.Values
            .Where(c => !c.IsRemoved)
            .Select(c => new ScenarioChange(c.ChangeId, c.Kind, c.PayloadJson))
            .ToList();

        var scenarioDef = new ScenarioDefinition(
            scenario.ScenarioId, scenario.Name, scenario.Notes, horizon, activeChanges);

        // Apply scenario overrides
        var fxOverride = ScenarioApplier.ExtractFxPolicyOverride(scenarioDef);
        var ccyOverride = ScenarioApplier.ExtractReportingCurrencyOverride(scenarioDef);
        var adjustments = ScenarioApplier.Apply(scenarioDef);

        var scenarioReportCcy = ccyOverride ?? reportCcy;
        var scenarioFxConfig = fxOverride ?? fxConfig;

        var scenarioReport = ForecastEngine.BuildBaselineForecast(
            horizon, cashState, recurringState, budgetState, goalsState, categoryState,
            debtRows, scenarioReportCcy, scenarioFxConfig, fxGraph, adjustments);

        var baselineDto = new ForecastReportDto(baseline.Horizon, baseline.ReportingCurrency, baseline.Summary,
            baseline.BalanceSeries, baseline.CashflowRows, baseline.BudgetRows, baseline.DebtRows, baseline.GoalRows);
        var scenarioDto = new ForecastReportDto(scenarioReport.Horizon, scenarioReport.ReportingCurrency, scenarioReport.Summary,
            scenarioReport.BalanceSeries, scenarioReport.CashflowRows, scenarioReport.BudgetRows, scenarioReport.DebtRows, scenarioReport.GoalRows);

        return new ScenarioForecastComparisonDto(
            baselineDto, scenarioDto,
            baseline.Summary.KnownEndBalance, scenarioReport.Summary.KnownEndBalance,
            scenarioReport.Summary.KnownEndBalance - baseline.Summary.KnownEndBalance,
            baseline.Summary.KnownNetCashflow, scenarioReport.Summary.KnownNetCashflow,
            scenarioReport.Summary.KnownNetCashflow - baseline.Summary.KnownNetCashflow);
    }
}

public sealed class GetForecastDashboardHandler
{
    private readonly IEventStore _store;

    public GetForecastDashboardHandler(IEventStore store) => _store = store;

    public async Task<ForecastDashboardDto> HandleAsync(DateOnly asOfDate, CancellationToken ct)
    {
        var envelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);
        var cashState = CashLedgerProjector.Project(envelopes, asOfDate);
        var recurringState = RecurringProjector.Project(envelopes);

        var totalBalance = cashState.Accounts.Values.Where(a => !a.IsArchived).Sum(a => a.Balance);

        // Estimate monthly expense from recurring
        var monthlyExpense = recurringState.Items.Values
            .Where(i => !i.IsArchived && i.Kind == "expense")
            .Sum(i => EstimateMonthlyAmount(i));

        var runwayDays = monthlyExpense > 0 ? totalBalance / (monthlyExpense / 30m) : 999m;

        // Count upcoming recurring in next 30 days
        var upcoming = recurringState.Items.Values
            .Where(i => !i.IsArchived)
            .Select(i => RecurringProjector.ComputeNextDueDate(i, asOfDate))
            .Count(d => d.HasValue && d.Value <= asOfDate.AddDays(30));

        // Net cashflow next month
        var monthlyIncome = recurringState.Items.Values
            .Where(i => !i.IsArchived && i.Kind == "income")
            .Sum(i => EstimateMonthlyAmount(i));

        var warnings = new List<ForecastWarning>();
        if (runwayDays < 90)
            warnings.Add(new ForecastWarning("LowRunway", $"Cash runway is only {runwayDays:N0} days", asOfDate));

        return new ForecastDashboardDto(
            Math.Round(runwayDays, 0), Math.Round(monthlyIncome - monthlyExpense, 2), upcoming, warnings);
    }

    private static decimal EstimateMonthlyAmount(RecurringItem item) =>
        item.Frequency switch
        {
            "Weekly" => item.Amount * (4.33m / item.Interval),
            "Monthly" => item.Amount / item.Interval,
            "Quarterly" => item.Amount / (3m * item.Interval),
            "Yearly" => item.Amount / (12m * item.Interval),
            _ => item.Amount
        };
}
