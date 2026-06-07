using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

/// <summary>
/// B6 Integration Tests: Balance Sheet must respect multi-vault isolation, event immutability, and determinism.
/// </summary>
public sealed class BalanceSheetIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public BalanceSheetIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"BalanceSheetTests_{Guid.NewGuid()}.db");
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

    [Fact]
    public async Task BalanceSheet_WithData_ReturnsCorrectTotals()
    {
        // Create account with opening balance via events
        var accountId = Guid.NewGuid();
        var accountEv = new AccountCreated(accountId, "Main", "Checking", "EGP", 25000m, new DateOnly(2025, 6, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            accountEv.EffectiveDate,
            _actorUserId, _deviceId,
            Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(accountEv, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);

        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var handler = new GetBalanceSheetHandler(_eventStore, obligationsHandler);

        var report = await handler.HandleAsync(new DateOnly(2025, 7, 1), "EGP");

        Assert.Equal(25000m, report.TotalAssets);
        Assert.Equal(0m, report.TotalLiabilities);
        Assert.Equal(25000m, report.Equity);
        Assert.Single(report.Assets);
        Assert.Empty(report.Liabilities);
    }

    [Fact]
    public async Task BalanceSheet_UnknownExcludedCountCorrect()
    {
        var accountId = Guid.NewGuid();
        var accountCreatedEvent = new AccountCreated(
            accountId, "USD Account", "Checking", "USD", 1000m, new DateOnly(2025, 6, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            accountCreatedEvent.EffectiveDate,
            _actorUserId, _deviceId,
            Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(accountCreatedEvent, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);

        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var handler = new GetBalanceSheetHandler(_eventStore, obligationsHandler);

        // Query for EGP without FX rate => unknown value
        var report = await handler.HandleAsync(new DateOnly(2025, 7, 1), "EGP");

        // USD account without FX rate should be excluded
        Assert.Equal(1, report.UnknownExcludedCount);
        Assert.Equal(0m, report.TotalAssets);
    }

    [Fact]
    public async Task BalanceSheet_MultiVaultIsolationVerified()
    {
        // Vault 1: Create account with balance
        var accountId = Guid.NewGuid();
        var accountEv = new AccountCreated(accountId, "V1 Account", "Checking", "EGP", 15000m, new DateOnly(2025, 6, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            accountEv.EffectiveDate,
            _actorUserId, _deviceId,
            Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(accountEv, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);

        var dbPath2 = Path.Combine(Path.GetTempPath(), $"BalanceSheetTests_v2_{Guid.NewGuid()}.db");
        var factory2 = new SqliteConnectionFactory(dbPath2, new TestKeyStore());
        var store2 = new SqliteEventStore(factory2);

        try
        {
            var dashboardHandler1 = CreateDashboardHandler();
            var obligationsHandler1 = new GetObligationsListHandler(dashboardHandler1);
            var handler1 = new GetBalanceSheetHandler(_eventStore, obligationsHandler1);

            var dashboardHandler2 = new GetPortfolioDashboardHandler(store2);
            var obligationsHandler2 = new GetObligationsListHandler(dashboardHandler2);
            var handler2 = new GetBalanceSheetHandler(store2, obligationsHandler2);

            var report1 = await handler1.HandleAsync(new DateOnly(2025, 7, 1), "EGP");
            var report2 = await handler2.HandleAsync(new DateOnly(2025, 7, 1), "EGP");

            Assert.Equal(15000m, report1.TotalAssets);
            Assert.Equal(0m, report2.TotalAssets);
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

    [Fact]
    public void BalanceSheet_NoEventChanges()
    {
        var handlerType = typeof(GetBalanceSheetHandler);
        var ctorParams = handlerType.GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.Contains(typeof(IEventStore), ctorParams);
        Assert.DoesNotContain(ctorParams, t => t.Name.Contains("DbContext") || t.Name.Contains("SqlCommand"));

        var methods = handlerType.GetMethods()
            .Where(m => m.DeclaringType == handlerType)
            .ToList();

        Assert.DoesNotContain(methods, m => m.Name.Contains("Append") || m.Name.Contains("Update") || m.Name.Contains("Delete"));
    }

    [Fact]
    public async Task DashboardAndBalanceSheetConsistency()
    {
        var accountId = await CreateAccountAsync("Main", 30000m);

        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);
        var balanceSheetHandler = new GetBalanceSheetHandler(_eventStore, obligationsHandler);

        var netWorth = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 7, 1), "EGP"),
            CancellationToken.None);

        var balanceSheet = await balanceSheetHandler.HandleAsync(new DateOnly(2025, 7, 1), "EGP");

        Assert.Equal(netWorth.TotalAssets, balanceSheet.TotalAssets);
        Assert.Equal(netWorth.TotalLiabilities, balanceSheet.TotalLiabilities);
        Assert.Equal(netWorth.KnownNetWorth, balanceSheet.Equity);
    }

    [Fact]
    public async Task ToggleMode_DoesNotBreakNetWorthView()
    {
        var accountId = await CreateAccountAsync("Main", 10000m);

        var dashboardHandler = CreateDashboardHandler();
        var obligationsHandler = new GetObligationsListHandler(dashboardHandler);
        var netWorthHandler = new GetNetWorthReportHandler(_eventStore, obligationsHandler);
        var balanceSheetHandler = new GetBalanceSheetHandler(_eventStore, obligationsHandler);

        var netWorth = await netWorthHandler.HandleAsync(
            new GetNetWorthReportQuery(new DateOnly(2025, 7, 1), "EGP"),
            CancellationToken.None);

        var balanceSheet = await balanceSheetHandler.HandleAsync(new DateOnly(2025, 7, 1), "EGP");

        Assert.NotNull(netWorth);
        Assert.NotNull(balanceSheet);
        Assert.Equal(netWorth.Rows.Count, balanceSheet.Assets.Count + balanceSheet.Liabilities.Count);
    }

    private GetPortfolioDashboardHandler CreateDashboardHandler()
    {
        return new GetPortfolioDashboardHandler(_eventStore);
    }

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
}
