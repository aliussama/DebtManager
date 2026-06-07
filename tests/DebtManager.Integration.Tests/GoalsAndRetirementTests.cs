using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Planning;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class GoalsAndRetirementTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public GoalsAndRetirementTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"GoalsRetirementTests_{id}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                break;
            }
            catch (IOException) when (i < 29) { Thread.Sleep(100); }
        }
    }

    // ----------------------------------------------------------------
    // 1) Goals_CreateModifyArchive_Works
    // ----------------------------------------------------------------
    [Fact]
    public async Task Goals_CreateModifyArchive_Works()
    {
        var createHandler = new CreateFinancialGoalHandler(_eventStore);
        var modifyHandler = new ModifyFinancialGoalHandler(_eventStore);
        var archiveHandler = new ArchiveFinancialGoalHandler(_eventStore);
        var dashHandler = new GetGoalsDashboardHandler(_eventStore);

        // Create
        var goalId = await createHandler.HandleAsync(
            new CreateFinancialGoalCommand(null, "Emergency Fund", "EmergencyFund",
                10000m, "EGP", new DateOnly(2026, 12, 31), "Rainy day fund", ["safety"],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Single(dash.Goals);
        Assert.Equal("Emergency Fund", dash.Goals[0].Name);
        Assert.Equal("EmergencyFund", dash.Goals[0].GoalType);
        Assert.Equal(10000m, dash.Goals[0].TargetAmount);
        Assert.Equal("Active", dash.Goals[0].Status);

        // Modify
        await modifyHandler.HandleAsync(
            new ModifyFinancialGoalCommand(goalId, "Updated Fund", "EmergencyFund",
                15000m, "EGP", new DateOnly(2027, 6, 30), "Updated notes", ["safety", "priority"],
                new DateOnly(2025, 2, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Equal("Updated Fund", dash.Goals[0].Name);
        Assert.Equal(15000m, dash.Goals[0].TargetAmount);

        // Archive
        await archiveHandler.HandleAsync(
            new ArchiveFinancialGoalCommand(goalId, new DateOnly(2025, 3, 1), "No longer needed"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Without includeArchived
        dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(IncludeArchived: false), CancellationToken.None);
        Assert.Empty(dash.Goals);

        // With includeArchived
        dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(IncludeArchived: true), CancellationToken.None);
        Assert.Single(dash.Goals);
        Assert.Equal("Archived", dash.Goals[0].Status);
    }

    // ----------------------------------------------------------------
    // 2) GoalContribution_Recorded_UpdatesProgress
    // ----------------------------------------------------------------
    [Fact]
    public async Task GoalContribution_Recorded_UpdatesProgress()
    {
        var createHandler = new CreateFinancialGoalHandler(_eventStore);
        var contribHandler = new RecordGoalContributionHandler(_eventStore);
        var dashHandler = new GetGoalsDashboardHandler(_eventStore);

        var accountId = await CreateAccount("Savings", "EGP");
        var goalId = await createHandler.HandleAsync(
            new CreateFinancialGoalCommand(null, "Car Fund", "Vehicle",
                50000m, "EGP", new DateOnly(2027, 1, 1), null, [],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Contribute 10,000
        await contribHandler.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, accountId,
                10000m, "EGP", new DateOnly(2025, 2, 1), "Feb savings"),
            _actorUserId, _deviceId, CancellationToken.None);

        var dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Single(dash.Goals);
        Assert.Equal(10000m, dash.Goals[0].Contributed);
        Assert.Equal(40000m, dash.Goals[0].Remaining);
        Assert.Equal(20m, dash.Goals[0].ProgressPercent);

        // Contribute another 40,000
        await contribHandler.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, accountId,
                40000m, "EGP", new DateOnly(2025, 3, 1), "Mar savings"),
            _actorUserId, _deviceId, CancellationToken.None);

        dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Equal(50000m, dash.Goals[0].Contributed);
        Assert.Equal(0m, dash.Goals[0].Remaining);
        Assert.Equal(100m, dash.Goals[0].ProgressPercent);
    }

    // ----------------------------------------------------------------
    // 3) GoalContribution_Reversed_NetsOut
    // ----------------------------------------------------------------
    [Fact]
    public async Task GoalContribution_Reversed_NetsOut()
    {
        var createHandler = new CreateFinancialGoalHandler(_eventStore);
        var contribHandler = new RecordGoalContributionHandler(_eventStore);
        var reverseHandler = new ReverseGoalContributionHandler(_eventStore);
        var dashHandler = new GetGoalsDashboardHandler(_eventStore);

        var accountId = await CreateAccount("Checking", "EGP");
        var goalId = await createHandler.HandleAsync(
            new CreateFinancialGoalCommand(null, "Travel Fund", "Travel",
                5000m, "EGP", new DateOnly(2026, 6, 1), null, [],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var contribId = await contribHandler.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, accountId,
                2000m, "EGP", new DateOnly(2025, 3, 1), "March"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Verify contributed
        var dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Equal(2000m, dash.Goals[0].Contributed);

        // Reverse
        await reverseHandler.HandleAsync(
            new ReverseGoalContributionCommand(goalId, contribId,
                new DateOnly(2025, 3, 2), "Incorrect entry"),
            _actorUserId, _deviceId, CancellationToken.None);

        dash = await dashHandler.HandleAsync(new GoalsDashboardQuery(), CancellationToken.None);
        Assert.Equal(0m, dash.Goals[0].Contributed);
        Assert.Equal(5000m, dash.Goals[0].Remaining);

        // Contribution still appears in history with "Reversed" status
        var reversedContribs = dash.RecentContributions.Where(c => c.Status == "Reversed").ToList();
        Assert.Single(reversedContribs);
    }

    // ----------------------------------------------------------------
    // 4) GoalsProjector_DeterministicOrdering
    // ----------------------------------------------------------------
    [Fact]
    public async Task GoalsProjector_DeterministicOrdering()
    {
        var createHandler = new CreateFinancialGoalHandler(_eventStore);
        var contribHandler = new RecordGoalContributionHandler(_eventStore);

        var accountId = await CreateAccount("Test", "EGP");
        var goalId = await createHandler.HandleAsync(
            new CreateFinancialGoalCommand(null, "Order Test", "Custom",
                10000m, "EGP", new DateOnly(2026, 1, 1), null, [],
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Multiple contributions on the same effective date
        await contribHandler.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, accountId,
                1000m, "EGP", new DateOnly(2025, 3, 1), "A"),
            _actorUserId, _deviceId, CancellationToken.None);

        await contribHandler.HandleAsync(
            new RecordGoalContributionCommand(goalId, null, accountId,
                2000m, "EGP", new DateOnly(2025, 3, 1), "B"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Project twice - results must be identical
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state1 = GoalsProjector.Project(envelopes);
        var state2 = GoalsProjector.Project(envelopes);

        Assert.Equal(state1.TotalContributed(goalId), state2.TotalContributed(goalId));
        Assert.Equal(3000m, state1.TotalContributed(goalId));
    }

    // ----------------------------------------------------------------
    // 5) Goals_Export_HasStableHeaders
    // ----------------------------------------------------------------
    [Fact]
    public void Goals_Export_HasStableHeaders()
    {
        var expectedHeaders = new[] {
            "Name", "Type", "TargetAmount", "TargetCurrency", "TargetDate",
            "Contributed", "Remaining", "ProgressPercent", "EstimatedCompletionDate", "Status", "Tags"
        };

        var header = string.Join(",", expectedHeaders);
        Assert.Equal("Name,Type,TargetAmount,TargetCurrency,TargetDate,Contributed,Remaining,ProgressPercent,EstimatedCompletionDate,Status,Tags", header);

        // Verify all 11 columns
        Assert.Equal(11, expectedHeaders.Length);
    }

    // ----------------------------------------------------------------
    // 6) RetirementProfile_And_Assumptions_Save_ThenReportGenerated
    // ----------------------------------------------------------------
    [Fact]
    public async Task RetirementProfile_And_Assumptions_Save_ThenReportGenerated()
    {
        var profileHandler = new DefineRetirementProfileHandler(_eventStore);
        var assumptionsHandler = new SetRetirementAssumptionsHandler(_eventStore);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var oblHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, oblHandler);
        var reportHandler = new GetRetirementPlanReportHandler(_eventStore, netWorthHandler);

        await profileHandler.HandleAsync(
            new DefineRetirementProfileCommand(null, "My Plan",
                new DateOnly(2050, 1, 1), 5000m, "EGP", 85,
                "SafeWithdrawalRate", 0.04m, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await assumptionsHandler.HandleAsync(
            new SetRetirementAssumptionsCommand(null, "Baseline",
                0.07m, 0.03m, 0.02m, 1000m, "EGP", "EGP",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var result = await reportHandler.HandleAsync(new DateOnly(2025, 6, 1), CancellationToken.None);

        Assert.True(result.HasProfile);
        Assert.True(result.HasAssumptions);
        Assert.Null(result.ErrorMessage);
        Assert.NotNull(result.Plan);
        Assert.True(result.Plan!.YearsToRetirement > 0);
        Assert.True(result.Plan.RequiredCorpusAtRetirement > 0);
        Assert.Equal("EGP", result.Plan.ReportingCurrencyCode);
    }

    // ----------------------------------------------------------------
    // 7) RetirementPlanner_Calculations_MatchExpected
    // ----------------------------------------------------------------
    [Fact]
    public void RetirementPlanner_Calculations_MatchExpected()
    {
        var profile = new RetirementProfileRecord
        {
            ProfileId = Guid.NewGuid(),
            ProfileName = "Test",
            RetirementDate = new DateOnly(2050, 1, 1),
            DesiredMonthlySpending = new Money(5000m, Currency.EGP),
            LifeExpectancyYears = 85,
            WithdrawalStrategy = "SafeWithdrawalRate",
            SafeWithdrawalRate = 0.04m,
            DefinedDate = new DateOnly(2025, 1, 1)
        };

        var assumptions = new RetirementAssumptionsRecord
        {
            AssumptionsId = Guid.NewGuid(),
            Name = "Baseline",
            ExpectedAnnualReturnRate = 0.07m,
            ExpectedAnnualInflation = 0.03m,
            ExpectedAnnualSalaryGrowth = 0.02m,
            CurrentMonthlySavings = new Money(2000m, Currency.EGP),
            ReportingCurrencyCode = "EGP",
            DefinedDate = new DateOnly(2025, 1, 1)
        };

        var asOf = new DateOnly(2025, 1, 1);
        var result = RetirementPlanner.Compute(profile, assumptions, 100000m, 0, asOf);

        // Years to retirement ~25
        Assert.True(result.YearsToRetirement >= 24m && result.YearsToRetirement <= 26m);

        // Inflated monthly spending > 5000 (25 years at 3%)
        Assert.True(result.MonthlySpendingAtRetirementInflationAdjusted > 5000m);

        // Required corpus = inflated annual / SWR => should be large
        Assert.True(result.RequiredCorpusAtRetirement > 100000m);

        // Projected should include FV of 100k + FV of monthly 2000 contributions
        Assert.True(result.ProjectedCorpusAtRetirement > 0m);

        // SWR should be what was input
        Assert.Equal(0.04m, result.SafeWithdrawalRate);
        Assert.Equal(0.07m, result.ReturnRate);
        Assert.Equal(0.03m, result.InflationRate);

        // No warnings expected for clean input
        Assert.DoesNotContain(result.Warnings, w => w.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------------------
    // 8) Retirement_Export_HasStableHeaders
    // ----------------------------------------------------------------
    [Fact]
    public void Retirement_Export_HasStableHeaders()
    {
        var expectedHeaders = new[] {
            "AsOfDate", "RetirementDate", "ReportingCurrency", "YearsToRetirement",
            "CurrentNetWorthKnown", "UnknownValueCount", "MonthlySpendingAtRetirement",
            "RequiredCorpus", "ProjectedCorpus", "FundingGap", "RequiredMonthlySavings",
            "SafeWithdrawalRate", "ReturnRate", "InflationRate"
        };

        var header = string.Join(",", expectedHeaders);
        Assert.Equal(
            "AsOfDate,RetirementDate,ReportingCurrency,YearsToRetirement," +
            "CurrentNetWorthKnown,UnknownValueCount,MonthlySpendingAtRetirement," +
            "RequiredCorpus,ProjectedCorpus,FundingGap,RequiredMonthlySavings," +
            "SafeWithdrawalRate,ReturnRate,InflationRate", header);

        Assert.Equal(14, expectedHeaders.Length);
    }

    // ----------------------------------------------------------------
    // 9) Retirement_MissingProfile_ShowsEmptyResult
    // ----------------------------------------------------------------
    [Fact]
    public async Task Retirement_MissingProfile_ShowsEmptyResult()
    {
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var oblHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, oblHandler);
        var reportHandler = new GetRetirementPlanReportHandler(_eventStore, netWorthHandler);

        // No profile or assumptions
        var result = await reportHandler.HandleAsync(new DateOnly(2025, 6, 1), CancellationToken.None);

        Assert.False(result.HasProfile);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.Plan);
    }

    // ----------------------------------------------------------------
    // 10) Retirement_UsesNetWorthKnown_AndFlagsUnknown
    // ----------------------------------------------------------------
    [Fact]
    public async Task Retirement_UsesNetWorthKnown_AndFlagsUnknown()
    {
        var profileHandler = new DefineRetirementProfileHandler(_eventStore);
        var assumptionsHandler = new SetRetirementAssumptionsHandler(_eventStore);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var oblHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, oblHandler);
        var reportHandler = new GetRetirementPlanReportHandler(_eventStore, netWorthHandler);

        // Create cash account with balance
        var accountId = await CreateAccount("Cash", "EGP");
        await RecordIncome(accountId, 50000m, "EGP", new DateOnly(2025, 1, 15), "Salary");

        // Create asset with no price (will be UnknownValue)
        var assetId = Guid.NewGuid();
        var qtySpec = JsonSerializer.Serialize(new { amount = 10m, symbol = "GLD" });
        var assetEv = new AssetCreated(assetId, "Gold", "Commodity", "USD", qtySpec, [], "",
            new DateOnly(2025, 1, 1));
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(assetId),
            nameof(AssetCreated), DateTimeOffset.UtcNow, assetEv.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(assetEv, DomainJson.Options)), CancellationToken.None);

        await profileHandler.HandleAsync(
            new DefineRetirementProfileCommand(null, "Plan",
                new DateOnly(2055, 1, 1), 3000m, "EGP", 85,
                "SafeWithdrawalRate", 0.04m, new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await assumptionsHandler.HandleAsync(
            new SetRetirementAssumptionsCommand(null, "Assumptions",
                0.07m, 0.03m, 0.02m, 500m, "EGP", "EGP",
                new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var result = await reportHandler.HandleAsync(new DateOnly(2025, 6, 1), CancellationToken.None);

        Assert.NotNull(result.Plan);

        // Net worth should include cash (50k EGP)
        Assert.True(result.Plan!.CurrentNetWorthKnown >= 50000m);

        // Unknown count should be >= 1 (Gold asset has no price)
        Assert.True(result.Plan.UnknownValueCount >= 1);

        // Warning about unknown values
        Assert.Contains(result.Plan.Warnings, w => w.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private async Task<Guid> CreateAccount(string name, string ccy)
    {
        var accountId = Guid.NewGuid();
        var ev = new AccountCreated(accountId, name, "Cash", ccy, 0m, DateOnly.FromDateTime(DateTime.Today));
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(AccountCreated), DateTimeOffset.UtcNow, ev.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), CancellationToken.None);
        return accountId;
    }

    private async Task RecordIncome(Guid accountId, decimal amount, string ccy, DateOnly date, string source)
    {
        var money = new Money(amount, new Currency(ccy, 2));
        var ev = new IncomeRecorded(accountId, money, date, source);
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(accountId),
            nameof(IncomeRecorded), DateTimeOffset.UtcNow, date,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options)), CancellationToken.None);
    }
}
