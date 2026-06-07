using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class FinancialHealthIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public FinancialHealthIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"FinancialHealthTests_{Guid.NewGuid()}.db");
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
    // 1) HealthScore_WithBalancedData_IsHigh
    // ================================================================
    [Fact]
    public async Task HealthScore_WithBalancedData_IsHigh()
    {
        var accountId = await CreateAccountAsync("Main", 50000m);

        // High income, moderate expenses, no debt
        for (int i = 0; i < 3; i++)
        {
            await RecordIncomeAsync(20000m, accountId, new DateOnly(2025, 2 + i, 1));
            await RecordExpenseAsync(8000m, accountId, new DateOnly(2025, 2 + i, 10), "Living");
        }

        var handler = new GetFinancialHealthHandler(_eventStore);
        var healthScore = await handler.HandleAsync(new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // High income, low expenses, good savings rate => high score
        Assert.True(healthScore.Score >= 80);
        Assert.True(healthScore.Grade is "A" or "B");
        Assert.Equal(6, healthScore.Components.Count);

        // Verify savings rate component
        var savingsComponent = healthScore.Components.First(c => c.Name == "Savings Rate");
        Assert.True(savingsComponent.Value > 0.4m);
    }

    // ================================================================
    // 2) HealthScore_WithHighDebt_IsLow
    // ================================================================
    [Fact]
    public async Task HealthScore_WithHighDebt_IsLow()
    {
        var accountId = await CreateAccountAsync("Main", 10000m);

        // Low income, high debt payments
        for (int i = 0; i < 3; i++)
        {
            await RecordIncomeAsync(5000m, accountId, new DateOnly(2025, 2 + i, 1));
            await RecordExpenseAsync(4500m, accountId, new DateOnly(2025, 2 + i, 10), "Loan Payment");
        }

        var handler = new GetFinancialHealthHandler(_eventStore);
        var healthScore = await handler.HandleAsync(new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // High debt-to-income ratio, low savings => low score
        Assert.True(healthScore.Score < 60);
        Assert.True(healthScore.Grade is "D" or "F");

        var debtComponent = healthScore.Components.First(c => c.Name == "Debt-to-Income Ratio");
        Assert.True(debtComponent.Value > 0.7m);
    }

    // ================================================================
    // 3) HealthScore_WithOverduePayments_ReducesScore
    // ================================================================
    [Fact]
    public async Task HealthScore_WithOverduePayments_ReducesScore()
    {
        var accountId = await CreateAccountAsync("Main", 20000m);
        var partyId = await CreatePartyAsync("Vendor");

        // Create overdue bills
        await IssueBillAsync(partyId, 1000m, new DateOnly(2025, 3, 1), new DateOnly(2025, 2, 1));
        await IssueBillAsync(partyId, 500m, new DateOnly(2025, 3, 15), new DateOnly(2025, 2, 1));
        await IssueBillAsync(partyId, 800m, new DateOnly(2025, 5, 1), new DateOnly(2025, 2, 1));

        var handler = new GetFinancialHealthHandler(_eventStore);
        var healthScore = await handler.HandleAsync(new DateOnly(2025, 4, 1), evaluationMonths: 3);

        // 2 out of 3 bills overdue = 0.67 overdue ratio => penalty
        var overdueComponent = healthScore.Components.First(c => c.Name == "Overdue Ratio");
        Assert.True(overdueComponent.Value > 0.5m);
        Assert.Equal("Critical", overdueComponent.Status);

        // Overall score should be reduced
        Assert.True(healthScore.Score < 70);
    }

    // ================================================================
    // 4) Dashboard_HealthScore_DisplaysCorrectValue
    // ================================================================
    [Fact]
    public async Task Dashboard_HealthScore_DisplaysCorrectValue()
    {
        var accountId = await CreateAccountAsync("Main", 30000m);

        // Balanced scenario
        for (int i = 0; i < 3; i++)
        {
            await RecordIncomeAsync(12000m, accountId, new DateOnly(2025, 2 + i, 1));
            await RecordExpenseAsync(6000m, accountId, new DateOnly(2025, 2 + i, 10), "Living");
        }

        var healthHandler = new GetFinancialHealthHandler(_eventStore);
        var healthScore = await healthHandler.HandleAsync(new DateOnly(2025, 4, 30), evaluationMonths: 3);

        // Verify dashboard can display this data
        Assert.InRange(healthScore.Score, 0, 100);
        Assert.NotNull(healthScore.Grade);
        Assert.NotEmpty(healthScore.Components);

        // Components should have valid weights
        var totalWeight = healthScore.Components.Sum(c => c.Weight);
        Assert.Equal(1.0m, totalWeight);
    }

    // ================================================================
    // 5) MultiVault_Isolation_Verified
    // ================================================================
    [Fact]
    public async Task MultiVault_Isolation_Verified()
    {
        // Vault 1: balanced finances
        var accountId = await CreateAccountAsync("V1 Account", 20000m);
        await RecordIncomeAsync(10000m, accountId, new DateOnly(2025, 3, 1));
        await RecordExpenseAsync(3000m, accountId, new DateOnly(2025, 3, 10), "Living");

        var handler1 = new GetFinancialHealthHandler(_eventStore);
        var score1 = await handler1.HandleAsync(new DateOnly(2025, 4, 1), evaluationMonths: 3);

        // Vault 2: empty
        var dbPath2 = Path.Combine(Path.GetTempPath(), $"FinancialHealthTests_v2_{Guid.NewGuid()}.db");
        var factory2 = new SqliteConnectionFactory(dbPath2, new TestKeyStore());
        var store2 = new SqliteEventStore(factory2);

        try
        {
            var handler2 = new GetFinancialHealthHandler(store2);
            var score2 = await handler2.HandleAsync(new DateOnly(2025, 4, 1), evaluationMonths: 3);

            // Scores should differ
            Assert.NotEqual(score1.Score, score2.Score);

            // Vault 2 should have default empty state score
            Assert.InRange(score2.Score, 30, 70);
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
    // 6) NoEventChanges_Verified
    // ================================================================
    [Fact]
    public void NoEventChanges_Verified()
    {
        // Verify GetFinancialHealthHandler only depends on IEventStore
        var handlerType = typeof(GetFinancialHealthHandler);
        var ctorParams = handlerType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.Single(ctorParams);
        Assert.Equal(typeof(IEventStore), ctorParams[0]);

        // Verify handler does not modify any events
        var methods = handlerType.GetMethods()
            .Where(m => m.DeclaringType == handlerType)
            .ToList();

        Assert.Contains(methods, m => m.Name == "HandleAsync");
        Assert.DoesNotContain(methods, m => m.Name.Contains("Append") || m.Name.Contains("Update") || m.Name.Contains("Delete"));
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

    private async Task RecordIncomeAsync(decimal amount, Guid accountId, DateOnly date)
    {
        var ev = new IncomeRecorded(accountId, new Money(amount, Currency.EGP), date, "Income");
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(IncomeRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId, _deviceId,
            Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task RecordExpenseAsync(decimal amount, Guid accountId, DateOnly date, string category)
    {
        var ev = new ExpenseRecorded(accountId, new Money(amount, Currency.EGP), date, category, "");
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(ExpenseRecorded),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId, _deviceId,
            Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
