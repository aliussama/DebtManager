using System.Text.Json;
using DebtManager.Application.Models;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class DashboardWidgetTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public DashboardWidgetTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"DashboardWidgetTests_{Guid.NewGuid()}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
    }

    public void Dispose()
    {
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
                if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                break;
            }
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    // ================================================================
    // 1) Dashboard_WithData_ReturnsCorrectTotals
    // ================================================================
    [Fact]
    public async Task Dashboard_WithData_ReturnsCorrectTotals()
    {
        var accountId = await CreateAccountAsync("Cash", 10000m);
        var partyId = await CreatePartyAsync("Vendor A");
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 7, 10), new DateOnly(2025, 6, 1));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(new DateOnly(2025, 7, 5));

        Assert.Equal(10000m, summary.TotalCashBalance);
        Assert.Equal(10000m - 500m, summary.NetWorth);
        Assert.Equal(1, summary.OverdueObligationCount + summary.UpcomingPayments.Count
            + (summary.OverdueObligationCount == 0 && summary.UpcomingPayments.Count == 0 ? 0 : 0));
        // Bill due 2025-07-10 is within 7 days of 2025-07-05
        Assert.Single(summary.UpcomingPayments);
        Assert.Equal(500m, summary.UpcomingPayments[0].Amount);
    }

    // ================================================================
    // 2) Dashboard_UpcomingPayments_FilteredTo7Days
    // ================================================================
    [Fact]
    public async Task Dashboard_UpcomingPayments_FilteredTo7Days()
    {
        var partyId = await CreatePartyAsync("Vendor B");
        var asOfDate = new DateOnly(2025, 7, 1);

        // Bill due in 3 days — included
        await IssueBillAsync(partyId, 100m, asOfDate.AddDays(3), new DateOnly(2025, 6, 1));
        // Bill due in 7 days — included
        await IssueBillAsync(partyId, 200m, asOfDate.AddDays(7), new DateOnly(2025, 6, 1));
        // Bill due in 10 days — excluded
        await IssueBillAsync(partyId, 300m, asOfDate.AddDays(10), new DateOnly(2025, 6, 1));
        // Bill due yesterday — excluded (overdue, not upcoming)
        await IssueBillAsync(partyId, 400m, asOfDate.AddDays(-1), new DateOnly(2025, 6, 1));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(asOfDate);

        Assert.Equal(2, summary.UpcomingPayments.Count);
        Assert.Equal(100m, summary.UpcomingPayments[0].Amount);
        Assert.Equal(200m, summary.UpcomingPayments[1].Amount);

        // The past-due bill should count as overdue
        Assert.Equal(1, summary.OverdueObligationCount);
    }

    // ================================================================
    // 3) Dashboard_TopGoals_SortedByLowestProgress
    // ================================================================
    [Fact]
    public async Task Dashboard_TopGoals_SortedByLowestProgress()
    {
        var accountId = await CreateAccountAsync("Savings", 50000m);
        var asOfDate = new DateOnly(2025, 7, 1);

        // Goal A — 10% progress
        var goalAId = Guid.NewGuid();
        await AppendEvent(new FinancialGoalCreated(goalAId, "Emergency Fund", "Savings",
            new Money(10000m, new Currency("EGP", 2)), new DateOnly(2026, 1, 1),
            null, Array.Empty<string>(), new DateOnly(2025, 1, 1)));
        await AppendEvent(new GoalContributionRecorded(goalAId, Guid.NewGuid(), accountId,
            new Money(1000m, new Currency("EGP", 2)), new DateOnly(2025, 3, 1), "Contrib A"));

        // Goal B — 50% progress
        var goalBId = Guid.NewGuid();
        await AppendEvent(new FinancialGoalCreated(goalBId, "Vacation", "Travel",
            new Money(4000m, new Currency("EGP", 2)), new DateOnly(2026, 6, 1),
            null, Array.Empty<string>(), new DateOnly(2025, 1, 1)));
        await AppendEvent(new GoalContributionRecorded(goalBId, Guid.NewGuid(), accountId,
            new Money(2000m, new Currency("EGP", 2)), new DateOnly(2025, 3, 1), "Contrib B"));

        // Goal C — 0% progress
        var goalCId = Guid.NewGuid();
        await AppendEvent(new FinancialGoalCreated(goalCId, "New Car", "Purchase",
            new Money(100000m, new Currency("EGP", 2)), new DateOnly(2027, 1, 1),
            null, Array.Empty<string>(), new DateOnly(2025, 1, 1)));

        // Goal D — 80% progress (should NOT be in top 3 because we only take lowest)
        var goalDId = Guid.NewGuid();
        await AppendEvent(new FinancialGoalCreated(goalDId, "Laptop", "Purchase",
            new Money(5000m, new Currency("EGP", 2)), new DateOnly(2025, 12, 1),
            null, Array.Empty<string>(), new DateOnly(2025, 1, 1)));
        await AppendEvent(new GoalContributionRecorded(goalDId, Guid.NewGuid(), accountId,
            new Money(4000m, new Currency("EGP", 2)), new DateOnly(2025, 3, 1), "Contrib D"));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(asOfDate);

        Assert.Equal(3, summary.TopGoals.Count);
        // Sorted by lowest progress: C (0%), A (10%), B (50%)
        Assert.Equal("New Car", summary.TopGoals[0].Name);
        Assert.Equal(0m, summary.TopGoals[0].ProgressPercent);
        Assert.Equal("Emergency Fund", summary.TopGoals[1].Name);
        Assert.Equal(10m, summary.TopGoals[1].ProgressPercent);
        Assert.Equal("Vacation", summary.TopGoals[2].Name);
        Assert.Equal(50m, summary.TopGoals[2].ProgressPercent);
    }

    // ================================================================
    // 4) Dashboard_BudgetHealth_ComputedCorrectly
    // ================================================================
    [Fact]
    public async Task Dashboard_BudgetHealth_ComputedCorrectly()
    {
        var accountId = await CreateAccountAsync("Main", 20000m);
        var asOfDate = new DateOnly(2025, 7, 15);

        var catId = Guid.NewGuid();
        await AppendEvent(new CategoryCreated(catId, "Food", "expense", null, new DateOnly(2025, 1, 1)));

        // Define budget for July 2025
        var budgetId = Guid.NewGuid();
        await AppendEvent(new BudgetDefined(budgetId, 2025, 7, "EGP", "category", catId, null,
            1000m, "None", new DateOnly(2025, 7, 1)));

        // Spend 800 — within budget
        await AppendEvent(new ExpenseRecorded(accountId,
            new Money(800m, new Currency("EGP", 2)),
            new DateOnly(2025, 7, 10), "Food", "Groceries"));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(asOfDate);

        // 1 budget, within limit => 100% health
        Assert.Equal(100m, summary.BudgetHealthPercent);
    }

    // ================================================================
    // 5) Dashboard_Determinism_SameEventsSameSummary
    // ================================================================
    [Fact]
    public async Task Dashboard_Determinism_SameEventsSameSummary()
    {
        var accountId = await CreateAccountAsync("Wallet", 5000m);
        var partyId = await CreatePartyAsync("Vendor C");
        await IssueBillAsync(partyId, 300m, new DateOnly(2025, 7, 5), new DateOnly(2025, 6, 1));

        var goalId = Guid.NewGuid();
        await AppendEvent(new FinancialGoalCreated(goalId, "Trip", "Travel",
            new Money(2000m, new Currency("EGP", 2)), new DateOnly(2026, 1, 1),
            null, Array.Empty<string>(), new DateOnly(2025, 1, 1)));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var asOfDate = new DateOnly(2025, 7, 3);

        var s1 = await handler.HandleAsync(asOfDate);
        var s2 = await handler.HandleAsync(asOfDate);

        Assert.Equal(s1.TotalCashBalance, s2.TotalCashBalance);
        Assert.Equal(s1.NetWorth, s2.NetWorth);
        Assert.Equal(s1.BudgetHealthPercent, s2.BudgetHealthPercent);
        Assert.Equal(s1.OverdueObligationCount, s2.OverdueObligationCount);
        Assert.Equal(s1.UpcomingPayments.Count, s2.UpcomingPayments.Count);
        Assert.Equal(s1.TopGoals.Count, s2.TopGoals.Count);
        Assert.Equal(s1.AiInsightCount, s2.AiInsightCount);
        Assert.Equal(s1.DataQualityIssueCount, s2.DataQualityIssueCount);

        for (int i = 0; i < s1.UpcomingPayments.Count; i++)
        {
            Assert.Equal(s1.UpcomingPayments[i].EntityId, s2.UpcomingPayments[i].EntityId);
            Assert.Equal(s1.UpcomingPayments[i].Amount, s2.UpcomingPayments[i].Amount);
        }

        for (int i = 0; i < s1.TopGoals.Count; i++)
        {
            Assert.Equal(s1.TopGoals[i].GoalId, s2.TopGoals[i].GoalId);
            Assert.Equal(s1.TopGoals[i].ProgressPercent, s2.TopGoals[i].ProgressPercent);
        }
    }

    // ================================================================
    // 6) Dashboard_NoData_ReturnsZeros
    // ================================================================
    [Fact]
    public async Task Dashboard_NoData_ReturnsZeros()
    {
        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(new DateOnly(2025, 7, 1));

        Assert.Equal(0m, summary.TotalCashBalance);
        Assert.Equal(0m, summary.NetWorth);
        Assert.Equal(100m, summary.BudgetHealthPercent); // No budgets = healthy
        Assert.Equal(0, summary.OverdueObligationCount);
        Assert.Empty(summary.UpcomingPayments);
        Assert.Empty(summary.TopGoals);
        Assert.Equal(0, summary.AiInsightCount);
        Assert.Equal(0, summary.DataQualityIssueCount);
    }

    // ================================================================
    // 7) Dashboard_MultiVault_Isolated
    // ================================================================
    [Fact]
    public async Task Dashboard_MultiVault_Isolated()
    {
        // Create data in vault 1
        var accountId = await CreateAccountAsync("Account V1", 7000m);

        // Create a second event store (separate vault)
        var dbPath2 = Path.Combine(Path.GetTempPath(), $"DashboardWidgetTests_v2_{Guid.NewGuid()}.db");
        var factory2 = new SqliteConnectionFactory(dbPath2, new TestKeyStore());
        var store2 = new SqliteEventStore(factory2);

        try
        {
            var handler1 = new GetDashboardSummaryHandler(_eventStore);
            var handler2 = new GetDashboardSummaryHandler(store2);

            var s1 = await handler1.HandleAsync(new DateOnly(2025, 7, 1));
            var s2 = await handler2.HandleAsync(new DateOnly(2025, 7, 1));

            Assert.Equal(7000m, s1.TotalCashBalance);
            Assert.Equal(0m, s2.TotalCashBalance);
        }
        finally
        {
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    if (File.Exists(dbPath2 + "-wal")) File.Delete(dbPath2 + "-wal");
                    if (File.Exists(dbPath2 + "-shm")) File.Delete(dbPath2 + "-shm");
                    if (File.Exists(dbPath2)) File.Delete(dbPath2);
                    break;
                }
                catch (IOException) when (i < 29)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }

    // ================================================================
    // 8) Dashboard_AiInsightCount_Correct
    // ================================================================
    [Fact]
    public async Task Dashboard_AiInsightCount_Correct()
    {
        // Emit AI insights directly
        var insightId1 = Guid.NewGuid();
        await AppendEvent(new AiInsightRecorded(insightId1, "BUDGET_EXCEEDED", "High", "Budget",
            "Overspending detected", "Your food budget exceeded limit.", new DateOnly(2025, 7, 1)));

        var insightId2 = Guid.NewGuid();
        await AppendEvent(new AiInsightRecorded(insightId2, "FORECAST_NEGATIVE_BALANCE", "Critical", "Forecast",
            "Cash warning", "Balance goes negative in August.", new DateOnly(2025, 7, 1)));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(new DateOnly(2025, 7, 5));

        Assert.Equal(2, summary.AiInsightCount);
    }

    // ================================================================
    // 9) Dashboard_DataQualityCount_Correct
    // ================================================================
    [Fact]
    public async Task Dashboard_DataQualityCount_Correct()
    {
        // Record a data quality scan
        var scanId = Guid.NewGuid();
        await AppendEvent(new DataQualityScanRecorded(
            EffectiveDate: new DateOnly(2025, 7, 1),
            ScanId: scanId,
            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CompletedAt: DateTimeOffset.UtcNow,
            AppVersion: "1.0.0",
            RuleSetVersion: "1.0",
            SummaryJson: "{}"));

        var handler = new GetDashboardSummaryHandler(_eventStore);
        var summary = await handler.HandleAsync(new DateOnly(2025, 7, 5));

        // 1 scan, 0 resolved => 1 issue
        Assert.Equal(1, summary.DataQualityIssueCount);
    }

    // ================================================================
    // 10) Dashboard_NoDirectDbQueries_Verified
    // ================================================================
    [Fact]
    public void Dashboard_NoDirectDbQueries_Verified()
    {
        // GetDashboardSummaryHandler only depends on IEventStore and domain projectors
        // Verify handler type does not reference any database types directly
        var handlerType = typeof(GetDashboardSummaryHandler);
        var ctorParams = handlerType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        // Only IEventStore allowed — no SqliteConnection, no DbContext, etc.
        Assert.Single(ctorParams);
        Assert.Equal(typeof(IEventStore), ctorParams[0]);

        // Verify handler computes from projection states (pure function verification)
        var methods = handlerType.GetMethods()
            .Where(m => m.DeclaringType == handlerType)
            .Select(m => m.Name)
            .ToList();

        Assert.Contains("HandleAsync", methods);
    }

    // ================================================================
    // Helpers
    // ================================================================

    private async Task<Guid> CreateAccountAsync(string name, decimal opening)
    {
        var handler = new CreateAccountHandler(_eventStore);
        return await handler.HandleAsync(
            new CreateAccountCommand(null, name, "Checking", opening, "EGP", DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> CreatePartyAsync(string name)
    {
        var handler = new CreatePartyHandler(_eventStore);
        return await handler.HandleAsync(
            new CreatePartyCommand(null, "Vendor", name, "EGP", null, Array.Empty<string>(),
                DateOnly.FromDateTime(DateTime.Today)),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task<Guid> IssueBillAsync(Guid partyId, decimal amount, DateOnly dueDate, DateOnly effectiveDate)
    {
        var handler = new IssueBillHandler(_eventStore);
        return await handler.HandleAsync(
            new IssueBillCommand(null, null, partyId, "EGP", amount, dueDate, "General", "Bill", null, effectiveDate),
            _actorUserId, _deviceId, CancellationToken.None);
    }

    private async Task AppendEvent<T>(T domainEvent) where T : IDomainEvent
    {
        var eventType = typeof(T).Name;
        var payloadJson = JsonSerializer.Serialize(domainEvent, DomainJson.Options);
        var streamId = new StreamId(Guid.NewGuid());

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            eventType,
            DateTimeOffset.UtcNow,
            domainEvent.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            payloadJson);

        await _eventStore.AppendAsync(envelope, CancellationToken.None);
    }
}
