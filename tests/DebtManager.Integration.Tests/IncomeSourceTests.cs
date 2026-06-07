using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class IncomeSourceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public IncomeSourceTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"IncomeSourceTests_{id}.db");
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

    // ================================================================
    // 1) DefineSource_List_Works
    // ================================================================
    [Fact]
    public async Task DefineSource_List_Works()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var listHandler = new GetIncomeSourcesHandler(_eventStore);

        var id = await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Monthly Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), "Primary job"),
            _actorUserId, _deviceId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);

        var list = await listHandler.HandleAsync(CancellationToken.None);
        Assert.Single(list);

        var source = list[0];
        Assert.Equal("Monthly Salary", source.Name);
        Assert.Equal("Salary", source.SourceType);
        Assert.Equal("EGP", source.CurrencyCode);
        Assert.True(source.IsRecurring);
        Assert.False(source.IsArchived);
        Assert.Equal("Primary job", source.Notes);
    }

    // ================================================================
    // 2) ArchiveSource_HidesFromActive
    // ================================================================
    [Fact]
    public async Task ArchiveSource_HidesFromActive()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var archiveHandler = new ArchiveIncomeSourceHandler(_eventStore);
        var listHandler = new GetIncomeSourcesHandler(_eventStore);

        var id = await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Freelance Work", IncomeSourceType.Freelance, "USD", false, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        await archiveHandler.HandleAsync(
            new ArchiveIncomeSourceCommand(id, "No longer freelancing", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var list = await listHandler.HandleAsync(CancellationToken.None);
        Assert.Single(list);
        Assert.True(list[0].IsArchived);

        // Projection active sources should be empty
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = IncomeSourceProjector.Project(envelopes);
        Assert.Empty(state.GetActiveSources());
    }

    // ================================================================
    // 3) RecordIncome_WithSourceId_SetsSourceStringAndLinks
    // ================================================================
    [Fact]
    public async Task RecordIncome_WithSourceId_SetsSourceStringAndLinks()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);

        var sourceId = await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record income WITH SourceId
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 2, 1), "any-text-ignored", sourceId),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        // Verify event has Source string set to source.Name
        var incomeEnvelope = envelopes.First(e => e.EventType == nameof(IncomeRecorded));
        var ev = JsonSerializer.Deserialize<IncomeRecorded>(incomeEnvelope.PayloadJson, DomainJson.Options);
        Assert.NotNull(ev);
        Assert.Equal("Salary", ev!.Source); // Source string set to name for backward compat

        // Verify projection totals
        var sourceState = IncomeSourceProjector.Project(envelopes);
        var src = sourceState.TryGet(sourceId);
        Assert.NotNull(src);
        Assert.Equal(15000m, src!.TotalReceived);
        Assert.Equal(new DateOnly(2025, 2, 1), src.LastReceivedDate);
    }

    // ================================================================
    // 4) RecordIncome_WithoutSourceId_IsUnclassified
    // ================================================================
    [Fact]
    public async Task RecordIncome_WithoutSourceId_IsUnclassified()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);
        var reportHandler = new GetIncomeBySourceReportHandler(_eventStore);

        // Define a source "Salary" but record income with a different string
        await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(5000m, "EGP", new DateOnly(2025, 2, 1), "Random Side Gig"),
            _actorUserId, _deviceId, CancellationToken.None);

        var report = await reportHandler.HandleAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), null, CancellationToken.None);

        Assert.Empty(report.PerSourceTotals);
        Assert.Equal(5000m, report.Unclassified.Total);
        Assert.Equal(1, report.Unclassified.TransactionCount);
    }

    // ================================================================
    // 5) BackwardCompat_StringOnlyIncome_MatchesDefinedSourceByName
    // ================================================================
    [Fact]
    public async Task BackwardCompat_StringOnlyIncome_MatchesDefinedSourceByName()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);
        var reportHandler = new GetIncomeBySourceReportHandler(_eventStore);

        // Define a source
        await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record income using string "Salary" WITHOUT SourceId (backward compat path)
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 2, 1), "Salary"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Case-insensitive match
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 3, 1), "salary"),
            _actorUserId, _deviceId, CancellationToken.None);

        var report = await reportHandler.HandleAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), null, CancellationToken.None);

        Assert.Single(report.PerSourceTotals);
        Assert.Equal(30000m, report.PerSourceTotals[0].Total);
        Assert.Equal(2, report.PerSourceTotals[0].TransactionCount);
        Assert.Equal(0m, report.Unclassified.Total);
    }

    // ================================================================
    // 6) IncomeBySourceReport_TotalsCorrect_IncludingUnclassified
    // ================================================================
    [Fact]
    public async Task IncomeBySourceReport_TotalsCorrect_IncludingUnclassified()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);
        var reportHandler = new GetIncomeBySourceReportHandler(_eventStore);

        // Create an account first
        await CreateAccountAsync("Wallet", 0m);

        // Define two sources
        await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);
        await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Freelance", IncomeSourceType.Freelance, "EGP", false, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Record incomes
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 2, 1), "Salary"),
            _actorUserId, _deviceId, CancellationToken.None);
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(5000m, "EGP", new DateOnly(2025, 2, 15), "Freelance"),
            _actorUserId, _deviceId, CancellationToken.None);
        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(1000m, "EGP", new DateOnly(2025, 2, 20), "Gift from uncle"),
            _actorUserId, _deviceId, CancellationToken.None);

        var report = await reportHandler.HandleAsync(
            new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31), null, CancellationToken.None);

        Assert.Equal(2, report.PerSourceTotals.Count);
        Assert.Equal(1000m, report.Unclassified.Total);
        Assert.Equal(21000m, report.GrandTotal);

        // Verify per-source (ordered by total descending)
        Assert.Equal("Salary", report.PerSourceTotals[0].SourceName);
        Assert.Equal(15000m, report.PerSourceTotals[0].Total);
        Assert.Equal("Freelance", report.PerSourceTotals[1].SourceName);
        Assert.Equal(5000m, report.PerSourceTotals[1].Total);
    }

    // ================================================================
    // 7) Determinism_SameEvents_SameProjectionState
    // ================================================================
    [Fact]
    public async Task Determinism_SameEvents_SameProjectionState()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);

        await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 2, 1), "Salary"),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var envList = envelopes.ToList();

        // Project twice from the same events
        var state1 = IncomeSourceProjector.Project(envList);
        var state2 = IncomeSourceProjector.Project(envList);

        Assert.Equal(state1.Sources.Count, state2.Sources.Count);

        foreach (var key in state1.Sources.Keys)
        {
            Assert.True(state2.Sources.ContainsKey(key));
            Assert.Equal(state1.Sources[key].TotalReceived, state2.Sources[key].TotalReceived);
            Assert.Equal(state1.Sources[key].Name, state2.Sources[key].Name);
            Assert.Equal(state1.Sources[key].IsArchived, state2.Sources[key].IsArchived);
            Assert.Equal(state1.Sources[key].LastReceivedDate, state2.Sources[key].LastReceivedDate);
        }

        // Also verify summary
        var summary1 = state1.GetSummary();
        var summary2 = state2.GetSummary();
        Assert.Equal(summary1.ActiveCount, summary2.ActiveCount);
        Assert.Equal(summary1.ArchivedCount, summary2.ArchivedCount);
        Assert.Equal(summary1.TotalReceivedAllSources, summary2.TotalReceivedAllSources);
    }

    // ================================================================
    // 8) Validation_RejectsInvalidCurrencyOrEmptyName
    // ================================================================
    [Fact]
    public async Task Validation_RejectsInvalidCurrencyOrEmptyName()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);

        // Empty name
        var ex1 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            defineHandler.HandleAsync(
                new DefineIncomeSourceCommand("", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("name", ex1.Message, StringComparison.OrdinalIgnoreCase);

        // Invalid currency (wrong length)
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            defineHandler.HandleAsync(
                new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EG", true, new DateOnly(2025, 1, 1), null),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("CurrencyCode", ex2.Message);

        // Invalid currency (has digits)
        var ex3 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            defineHandler.HandleAsync(
                new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "E12", true, new DateOnly(2025, 1, 1), null),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("CurrencyCode", ex3.Message);

        // Name too long (101 chars)
        var longName = new string('A', 101);
        var ex4 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            defineHandler.HandleAsync(
                new DefineIncomeSourceCommand(longName, IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("name", ex4.Message, StringComparison.OrdinalIgnoreCase);

        // Notes too long
        var longNotes = new string('N', 501);
        var ex5 = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            defineHandler.HandleAsync(
                new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), longNotes),
                _actorUserId, _deviceId, CancellationToken.None));
        Assert.Contains("Notes", ex5.Message);

        // Verify nothing was persisted
        var envelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Empty(envelopes);
    }

    // ================================================================
    // 9) RecordIncome_WithArchivedSourceId_Throws
    // ================================================================
    [Fact]
    public async Task RecordIncome_WithArchivedSourceId_Throws()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var archiveHandler = new ArchiveIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);

        var sourceId = await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Old Job", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        await archiveHandler.HandleAsync(
            new ArchiveIncomeSourceCommand(sourceId, "Left job", new DateOnly(2025, 6, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            incomeHandler.HandleAsync(
                new RecordIncomeCommand(5000m, "EGP", new DateOnly(2025, 7, 1), "Old Job", sourceId),
                _actorUserId, _deviceId, CancellationToken.None));

        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================
    // 10) CashLedgerRow_HasSourceId_WhenMatchedByName
    // ================================================================
    [Fact]
    public async Task CashLedgerRow_HasSourceId_WhenMatchedByName()
    {
        var defineHandler = new DefineIncomeSourceHandler(_eventStore);
        var incomeHandler = new RecordIncomeHandler(_eventStore);
        var cashLedgerHandler = new GetCashLedgerHandler(_eventStore);

        var sourceId = await defineHandler.HandleAsync(
            new DefineIncomeSourceCommand("Salary", IncomeSourceType.Salary, "EGP", true, new DateOnly(2025, 1, 1), null),
            _actorUserId, _deviceId, CancellationToken.None);

        await incomeHandler.HandleAsync(
            new RecordIncomeCommand(15000m, "EGP", new DateOnly(2025, 2, 1), "Salary"),
            _actorUserId, _deviceId, CancellationToken.None);

        var result = await cashLedgerHandler.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

        var incomeRow = result.Rows.FirstOrDefault(r => r.Direction == "In" && r.Category == "Income");
        Assert.NotNull(incomeRow);
        Assert.Equal(sourceId, incomeRow!.SourceId);
    }

    // ================================================================
    // Helper
    // ================================================================
    private async Task<Guid> CreateAccountAsync(string name, decimal opening)
    {
        var accountId = Guid.NewGuid();
        var ev = new AccountCreated(accountId, name, "Cash", "EGP", opening, new DateOnly(2025, 1, 1));
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
        return accountId;
    }
}
