using System.Text.Json;
using DebtManager.Application.Ai;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Ai;
using DebtManager.Domain.Events;
using DebtManager.Domain.Forecasting;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class AiAdvisorTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public AiAdvisorTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"AiAdvisorTests_{id}.db");
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

    // Helpers

    private async Task EnableAiAsync()
    {
        var handler = new UpdateAiSettingsHandler(_eventStore);
        await handler.HandleAsync(
            new UpdateAiSettingsCommand(true, false, false, DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreateAccountAsync(string name = "Cash", decimal opening = 5000m)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Bank", opening, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreatePartyAsync(string name)
    {
        var handler = new CreatePartyHandler(_eventStore);
        return await handler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", name, "EGP", null, [], DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueBillAsync(Guid partyId, decimal amount, DateOnly dueDate, DateOnly effectiveDate)
    {
        var handler = new IssueBillHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", amount, dueDate, "General",
                $"BILL-{Guid.NewGuid().ToString("N")[..6]}", null, effectiveDate),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task AppendEvent<T>(T domainEvent) where T : IDomainEvent
    {
        var typeName = typeof(T).Name;
        await _eventStore.AppendAsync(new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(Guid.NewGuid()),
            typeName, DateTimeOffset.UtcNow, domainEvent.EffectiveDate,
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(domainEvent, DomainJson.Options)), CancellationToken.None);
    }

    // 1) RunAiAnalysis_WritesInsightsAndProposals
    [Fact]
    public async Task RunAiAnalysis_WritesInsightsAndProposals()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);

        var partyId = await CreatePartyAsync("Vendor");
        // Issue an overdue bill
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var handler = new RunAiAnalysisHandler(_eventStore);
        var (insightCount, proposalCount) = await handler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.True(insightCount > 0, "Should produce at least one insight");

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(all, e => e.EventType == nameof(AiInsightRecorded));
    }

    // 2) Determinism_SameStateSameInsights
    [Fact]
    public async Task Determinism_SameStateSameInsights()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var asOfDate = new DateOnly(2025, 6, 15);

        // Run engine twice on same data
        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var ledger = CashLedgerProjector.Project(all);
        var budget = BudgetProjector.Project(all);
        var cats = CategoryProjector.Project(all);
        var billing = BillingProjector.Project(all, asOfDate);
        var goals = GoalsProjector.Project(all);
        var retirement = RetirementProjector.Project(all);
        var portfolio = PortfolioProjector.Project(all);
        var assets = AssetsProjector.Project(all);
        var dq = DataQualityProjector.Project(all);

        var input = new AiAdvisorEngine.AnalysisInput(
            ledger, budget, cats, billing, goals, retirement, portfolio, assets, dq, null, asOfDate);

        var output1 = AiAdvisorEngine.Analyze(input);
        var output2 = AiAdvisorEngine.Analyze(input);

        Assert.Equal(output1.Insights.Count, output2.Insights.Count);
        Assert.Equal(output1.Proposals.Count, output2.Proposals.Count);

        for (int i = 0; i < output1.Insights.Count; i++)
        {
            Assert.Equal(output1.Insights[i].InsightId, output2.Insights[i].InsightId);
            Assert.Equal(output1.Insights[i].InsightCode, output2.Insights[i].InsightCode);
        }

        for (int i = 0; i < output1.Proposals.Count; i++)
        {
            Assert.Equal(output1.Proposals[i].ProposalId, output2.Proposals[i].ProposalId);
            Assert.Equal(output1.Proposals[i].ProposalKind, output2.Proposals[i].ProposalKind);
        }
    }

    // 3) ProposalApproval_WritesApprovalEvent
    [Fact]
    public async Task ProposalApproval_WritesApprovalEvent()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var analysisHandler = new RunAiAnalysisHandler(_eventStore);
        await analysisHandler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        var pendingProposal = dashboard.Proposals.FirstOrDefault(p => p.Status == "Pending");

        if (pendingProposal != null)
        {
            var approveHandler = new ApproveAiProposalHandler(_eventStore);
            await approveHandler.HandleAsync(
                new ApproveAiProposalCommand(pendingProposal.ProposalId, new DateOnly(2025, 6, 15)),
                _actorUserId, _deviceId, CancellationToken.None);

            var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
            Assert.Contains(all, e => e.EventType == nameof(AiProposalApproved));
        }
    }

    // 4) ProposalApproval_TriggersCorrectExistingHandler (via approval event only — foundation)
    [Fact]
    public async Task ProposalApproval_TriggersCorrectExistingHandler()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var analysisHandler = new RunAiAnalysisHandler(_eventStore);
        await analysisHandler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        var proposal = dashboard.Proposals.FirstOrDefault(p => p.Status == "Pending");

        if (proposal != null)
        {
            await new ApproveAiProposalHandler(_eventStore).HandleAsync(
                new ApproveAiProposalCommand(proposal.ProposalId, new DateOnly(2025, 6, 15)),
                _actorUserId, _deviceId, CancellationToken.None);

            // Verify the proposal is now Approved in state
            var dashboard2 = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
            var approved = dashboard2.Proposals.FirstOrDefault(p => p.ProposalId == proposal.ProposalId);
            Assert.NotNull(approved);
            Assert.Equal("Approved", approved!.Status);
        }
    }

    // 5) ProposalRejection_WritesEvent
    [Fact]
    public async Task ProposalRejection_WritesEvent()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        var analysisHandler = new RunAiAnalysisHandler(_eventStore);
        await analysisHandler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        var proposal = dashboard.Proposals.FirstOrDefault(p => p.Status == "Pending");

        if (proposal != null)
        {
            await new RejectAiProposalHandler(_eventStore).HandleAsync(
                new RejectAiProposalCommand(proposal.ProposalId, "Not now", new DateOnly(2025, 6, 15)),
                _actorUserId, _deviceId, CancellationToken.None);

            var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
            Assert.Contains(all, e => e.EventType == nameof(AiProposalRejected));

            var dashboard2 = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
            var rejected = dashboard2.Proposals.FirstOrDefault(p => p.ProposalId == proposal.ProposalId);
            Assert.NotNull(rejected);
            Assert.Equal("Rejected", rejected!.Status);
            Assert.Equal("Not now", rejected.RejectionReason);
        }
    }

    // 6) AiDisabled_NoAnalysisRuns
    [Fact]
    public async Task AiDisabled_NoAnalysisRuns()
    {
        // AI is disabled by default — no EnableAiAsync call
        await CreateAccountAsync("Cash", 10000m);

        var handler = new RunAiAnalysisHandler(_eventStore);
        var (insightCount, proposalCount) = await handler.HandleAsync(
            new RunAiAnalysisCommand(DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.Equal(0, insightCount);
        Assert.Equal(0, proposalCount);

        var all = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.DoesNotContain(all, e => e.EventType == nameof(AiInsightRecorded));
        Assert.DoesNotContain(all, e => e.EventType == nameof(AiProposalCreated));
    }

    // 7) HighInterestDebt_IdleCash_GeneratesReallocateProposal
    [Fact]
    public async Task HighInterestDebt_IdleCash_GeneratesReallocateProposal()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Main Account", 50000m);

        var partyId = await CreatePartyAsync("Bank");
        // Issue outstanding bill — simulates debt
        await IssueBillAsync(partyId, 10000m, new DateOnly(2025, 7, 1), new DateOnly(2025, 6, 1));

        var handler = new RunAiAnalysisHandler(_eventStore);
        await handler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        Assert.Contains(dashboard.Proposals, p => p.ProposalKind == "ReallocateCash");
    }

    // 8) ForecastNegativeBalance_GeneratesWarning
    [Fact]
    public async Task ForecastNegativeBalance_GeneratesWarning()
    {
        var asOfDate = new DateOnly(2025, 6, 15);

        // Build engine input with forecast warning
        var forecast = new ForecastReport(
            new ForecastHorizon(asOfDate, asOfDate.AddMonths(3), ForecastGranularity.Monthly),
            "EGP",
            new ForecastSummary(-5000m, -5000m, 0, new[] {
                new ForecastWarning("NEGATIVE_BALANCE", "Balance goes negative in August", new DateOnly(2025, 8, 1))
            }),
            Array.Empty<CashBalanceSeries>(),
            Array.Empty<CashflowBreakdownRow>(),
            Array.Empty<BudgetForecastRow>(),
            Array.Empty<DebtForecastRow>(),
            Array.Empty<GoalForecastRow>(),
            Array.Empty<ForecastPoint>());

        var input = new AiAdvisorEngine.AnalysisInput(
            new CashLedgerState(), new BudgetState(), new CategoryState(),
            new BillingState(), new GoalsState(), new RetirementState(),
            new PortfolioState(), new AssetsState(), new DataQualityState(),
            forecast, asOfDate);

        var output = AiAdvisorEngine.Analyze(input);

        Assert.Contains(output.Insights, i => i.InsightCode == "FORECAST_NEGATIVE_BALANCE");
        Assert.Contains(output.Proposals, p => p.ProposalKind == "AdjustRecurring");
    }

    // 9) BudgetOverrun_GeneratesBudgetProposal
    [Fact]
    public async Task BudgetOverrun_GeneratesBudgetProposal()
    {
        await EnableAiAsync();

        var categoryId = Guid.NewGuid();
        await AppendEvent(new CategoryCreated(categoryId, "Food", "expense", null, new DateOnly(2025, 6, 1)));

        var budgetId = Guid.NewGuid();
        await AppendEvent(new BudgetDefined(budgetId, 2025, 6, "EGP", "category", categoryId, null, 500m,
            "None", new DateOnly(2025, 6, 1)));

        var accountId = await CreateAccountAsync("Cash", 10000m);
        await AppendEvent(new ExpenseRecorded(accountId, new Money(700m, new Currency("EGP", 2)),
            new DateOnly(2025, 6, 15), "Food", "Groceries"));

        var handler = new RunAiAnalysisHandler(_eventStore);
        await handler.HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 20)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        Assert.Contains(dashboard.Insights, i => i.InsightCode == "BUDGET_EXCEEDED");
        Assert.Contains(dashboard.Proposals, p => p.ProposalKind == "CreateBudget");
    }

    // 10) PortfolioConcentration_GeneratesInsight
    [Fact]
    public void PortfolioConcentration_GeneratesInsight()
    {
        var asOfDate = new DateOnly(2025, 6, 15);
        var portfolioState = new PortfolioState();
        var acctId = Guid.NewGuid();
        var assetId1 = Guid.NewGuid();
        var assetId2 = Guid.NewGuid();

        // Add positions where one is 80% of total
        portfolioState.Positions[(acctId, assetId1)] = new InvestmentPosition
        {
            AccountId = acctId,
            AssetId = assetId1,
            Symbol = "BIGSTOCK",
            Quantity = 100,
            TotalCost = 80000m
        };
        portfolioState.Positions[(acctId, assetId2)] = new InvestmentPosition
        {
            AccountId = acctId,
            AssetId = assetId2,
            Symbol = "SMALLSTOCK",
            Quantity = 50,
            TotalCost = 20000m
        };

        var input = new AiAdvisorEngine.AnalysisInput(
            new CashLedgerState(), new BudgetState(), new CategoryState(),
            new BillingState(), new GoalsState(), new RetirementState(),
            portfolioState, new AssetsState(), new DataQualityState(),
            null, asOfDate);

        var output = AiAdvisorEngine.Analyze(input);

        Assert.Contains(output.Insights, i => i.InsightCode == "PORTFOLIO_CONCENTRATION");
        Assert.Contains(output.Insights, i => i.Title.Contains("BIGSTOCK"));
    }

    // 11) RetirementUnderfunded_GeneratesProposal
    [Fact]
    public void RetirementUnderfunded_GeneratesProposal()
    {
        var asOfDate = new DateOnly(2025, 6, 15);
        var retirementState = new RetirementState();
        retirementState.Profiles.Add(new RetirementProfileRecord
        {
            ProfileId = Guid.NewGuid(),
            ProfileName = "Main",
            RetirementDate = new DateOnly(2055, 1, 1),
            DesiredMonthlySpending = new Money(5000m, new Currency("EGP", 2)),
            LifeExpectancyYears = 85,
            SafeWithdrawalRate = 0.04m,
            DefinedDate = new DateOnly(2025, 1, 1)
        });
        retirementState.AllAssumptions.Add(new RetirementAssumptionsRecord
        {
            AssumptionsId = Guid.NewGuid(),
            Name = "Default",
            ExpectedAnnualReturnRate = 0.07m,
            ExpectedAnnualInflation = 0.03m,
            ExpectedAnnualSalaryGrowth = 0.02m,
            CurrentMonthlySavings = new Money(200m, new Currency("EGP", 2)),
            ReportingCurrencyCode = "EGP",
            DefinedDate = new DateOnly(2025, 1, 1)
        });

        var input = new AiAdvisorEngine.AnalysisInput(
            new CashLedgerState(), new BudgetState(), new CategoryState(),
            new BillingState(), new GoalsState(), retirementState,
            new PortfolioState(), new AssetsState(), new DataQualityState(),
            null, asOfDate);

        var output = AiAdvisorEngine.Analyze(input);

        // 200/mo * 12 * 30 = 72000 vs 5000 * 12 * 55 = 3,300,000 => ~2% funding = proposal
        Assert.Contains(output.Proposals, p => p.ProposalKind == "AdjustRecurring");
    }

    // 12) Approval_IsIdempotent
    [Fact]
    public async Task Approval_IsIdempotent()
    {
        await EnableAiAsync();
        await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 5, 1), new DateOnly(2025, 4, 1));

        await new RunAiAnalysisHandler(_eventStore).HandleAsync(
            new RunAiAnalysisCommand(new DateOnly(2025, 6, 15)),
            _actorUserId, _deviceId, CancellationToken.None);

        var dashboard = await new GetAiDashboardHandler(_eventStore).HandleAsync(CancellationToken.None);
        var proposal = dashboard.Proposals.FirstOrDefault(p => p.Status == "Pending");

        if (proposal != null)
        {
            var approveHandler = new ApproveAiProposalHandler(_eventStore);

            // Approve once
            await approveHandler.HandleAsync(
                new ApproveAiProposalCommand(proposal.ProposalId, new DateOnly(2025, 6, 15)),
                _actorUserId, _deviceId, CancellationToken.None);

            var countBefore = (await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None))
                .Count(e => e.EventType == nameof(AiProposalApproved));

            // Approve again - should be idempotent (no additional event)
            await approveHandler.HandleAsync(
                new ApproveAiProposalCommand(proposal.ProposalId, new DateOnly(2025, 6, 15)),
                _actorUserId, _deviceId, CancellationToken.None);

            var countAfter = (await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None))
                .Count(e => e.EventType == nameof(AiProposalApproved));

            Assert.Equal(countBefore, countAfter);
        }
    }
}
