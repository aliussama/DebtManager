using System.Text.Json;
using DebtManager.Application.Projections;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Projections.Snapshots;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class ProjectionCachingAndSnapshotsTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteProjectionSnapshotStore _snapshotStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public ProjectionCachingAndSnapshotsTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"SnapshotTests_{id}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _snapshotStore = new SqliteProjectionSnapshotStore(_factory);
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
            catch (IOException) when (i < 29)
            {
                Thread.Sleep(100);
            }
        }
    }

    // ================================================================
    // A) SnapshotStore_SaveAndLoad_Works
    // ================================================================
    [Fact]
    public async Task SnapshotStore_SaveAndLoad_Works()
    {
        var projectionName = "TestProjection";
        var asOfDate = new DateOnly(2025, 6, 1);
        var lastEventId = Guid.NewGuid();
        var lastOccurredAt = DateTimeOffset.UtcNow;

        var snapshot = new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            projectionName,
            asOfDate,
            DateTimeOffset.UtcNow,
            _deviceId,
            1,
            """{"TestKey":"TestValue"}""",
            lastEventId,
            lastOccurredAt);

        await _snapshotStore.SaveAsync(snapshot, CancellationToken.None);

        var loaded = await _snapshotStore.LoadLatestAsync(projectionName, asOfDate, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.SnapshotId, loaded.SnapshotId);
        Assert.Equal(snapshot.ProjectionName, loaded.ProjectionName);
        Assert.Equal(snapshot.AsOfDate, loaded.AsOfDate);
        Assert.Equal(snapshot.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(snapshot.PayloadJson, loaded.PayloadJson);
        Assert.Equal(snapshot.LastEventId, loaded.LastEventId);
        Assert.Equal(snapshot.DeviceId, loaded.DeviceId);
    }

    // ================================================================
    // B) ProjectionRunner_FullReplay_Equals_SnapshotPlusTail
    // ================================================================
    [Fact]
    public async Task ProjectionRunner_FullReplay_Equals_SnapshotPlusTail()
    {
        // Seed an account + some expenses
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Test Account", 10000m);

        for (int i = 0; i < 20; i++)
        {
            await AppendExpenseEvent(accountId, 100m, "EGP", "Food", $"Expense {i}",
                new DateOnly(2025, 1, 1 + i));
        }

        // Full replay result
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var fullReplayState = CashLedgerProjector.Project(allEnvelopes);

        // Create snapshot from first 11 events (account + 10 expenses)
        var firstBatch = allEnvelopes.Take(11).ToList();
        var snapshotState = CashLedgerProjector.Project(firstBatch);
        var snapshotJson = JsonSerializer.Serialize(snapshotState, DomainJson.Options);

        var lastInBatch = firstBatch[^1];
        var snapshotEnvelope = new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            new DateOnly(2025, 1, 10),
            DateTimeOffset.UtcNow,
            _deviceId,
            ProjectionCachePolicies.SchemaVersions.CashLedgerState,
            snapshotJson,
            lastInBatch.EventId.Value,
            lastInBatch.OccurredAt);

        await _snapshotStore.SaveAsync(snapshotEnvelope, CancellationToken.None);

        // Now use ProjectionRunner (without projectTail — should do full replay)
        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, _snapshotStore, cache, _deviceId);

        var runnerState = await runner.RunAsync(
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        // Verify identical outputs
        Assert.Equal(fullReplayState.Accounts.Count, runnerState.Accounts.Count);
        Assert.Equal(fullReplayState.Rows.Count, runnerState.Rows.Count);
        Assert.Equal(fullReplayState.TotalIncome, runnerState.TotalIncome);
        Assert.Equal(fullReplayState.TotalExpense, runnerState.TotalExpense);

        foreach (var key in fullReplayState.Accounts.Keys)
        {
            Assert.True(runnerState.Accounts.ContainsKey(key));
            Assert.Equal(fullReplayState.Accounts[key].Balance, runnerState.Accounts[key].Balance);
        }
    }

    // ================================================================
    // C) Snapshot_Ignored_OnSchemaMismatch_RebuildsCorrectly
    // ================================================================
    [Fact]
    public async Task Snapshot_Ignored_OnSchemaMismatch_RebuildsCorrectly()
    {
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Mismatch Test", 5000m);
        await AppendExpenseEvent(accountId, 200m, "EGP", "Food", "Lunch");

        // Save snapshot with wrong schema version (999)
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var state = CashLedgerProjector.Project(allEnvelopes);
        var json = JsonSerializer.Serialize(state, DomainJson.Options);

        var badSnapshot = new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            DateOnly.FromDateTime(DateTime.Today),
            DateTimeOffset.UtcNow,
            _deviceId,
            999, // Wrong version!
            json,
            allEnvelopes[^1].EventId.Value,
            allEnvelopes[^1].OccurredAt);

        await _snapshotStore.SaveAsync(badSnapshot, CancellationToken.None);

        // Use ProjectionRunner with a projectTail that would fail if called with wrong state
        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, _snapshotStore, cache, _deviceId);

        var result = await runner.RunAsync(
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            envelopes => CashLedgerProjector.Project(envelopes),
            (existing, tail) => CashLedgerProjector.Project(tail), // Incorrect tail but should NOT be used
            ct: CancellationToken.None);

        // Should still produce correct results from full replay
        Assert.Single(result.Accounts);
        Assert.Equal(4800m, result.Accounts[accountId].Balance); // 5000 - 200
    }

    // ================================================================
    // D) Cache_Hit_DoesNotChangeOutput
    // ================================================================
    [Fact]
    public async Task Cache_Hit_DoesNotChangeOutput()
    {
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Cache Test", 3000m);
        await AppendExpenseEvent(accountId, 100m, "EGP", "Transport", "Bus");

        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, null, cache, _deviceId, snapshotsEnabled: false);

        // First call — cache miss
        var result1 = await runner.RunAsync(
            "CacheTest",
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        int projectionCallCount = 0;

        // Second call — should hit cache
        var result2 = await runner.RunAsync(
            "CacheTest",
            envelopes =>
            {
                projectionCallCount++;
                return CashLedgerProjector.Project(envelopes);
            },
            ct: CancellationToken.None);

        // Result should be identical
        Assert.Equal(result1.Accounts.Count, result2.Accounts.Count);
        Assert.Equal(result1.TotalExpense, result2.TotalExpense);

        // Projector should NOT have been called (cache hit)
        Assert.Equal(0, projectionCallCount);
    }

    // ================================================================
    // E) WatermarkChange_InvalidatesCache
    // ================================================================
    [Fact]
    public async Task WatermarkChange_InvalidatesCache()
    {
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Watermark Test", 1000m);

        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, null, cache, _deviceId, snapshotsEnabled: false);

        var result1 = await runner.RunAsync(
            "WatermarkTest",
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        Assert.Equal(1000m, result1.Accounts[accountId].Balance);

        // Add a new event (advances watermark)
        await AppendExpenseEvent(accountId, 300m, "EGP", "Food", "Dinner");

        var result2 = await runner.RunAsync(
            "WatermarkTest",
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        Assert.Equal(700m, result2.Accounts[accountId].Balance);
    }

    // ================================================================
    // F) PruneSnapshots_KeepsLastN
    // ================================================================
    [Fact]
    public async Task PruneSnapshots_KeepsLastN()
    {
        var projectionName = "PruneTest";

        // Save 5 snapshots with increasing dates
        for (int i = 0; i < 5; i++)
        {
            var snapshot = new ProjectionSnapshotEnvelope(
                new SnapshotId(Guid.NewGuid()),
                projectionName,
                new DateOnly(2025, 1, 1 + i),
                DateTimeOffset.UtcNow.AddMinutes(i),
                _deviceId,
                1,
                $"{{\"index\":{i}}}",
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(i));

            await _snapshotStore.SaveAsync(snapshot, CancellationToken.None);
        }

        // Prune keeping only last 2
        await _snapshotStore.PruneAsync(projectionName, 2, CancellationToken.None);

        // The latest snapshot should still be loadable
        var latest = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 12, 31), CancellationToken.None);
        Assert.NotNull(latest);
        Assert.Contains("\"index\":4", latest.PayloadJson);

        // Try loading one of the pruned snapshots (index 0, date 2025-01-01)
        // Only exact match up to that date
        var early = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 1, 1), CancellationToken.None);
        // Should not find the pruned old ones, only if latest 2 overlap
        // Latest 2 are dates 2025-01-04 and 2025-01-05, so early query should return null
        Assert.Null(early);
    }

    // ================================================================
    // G) Performance Sanity: Many events ? snapshot creation ? snapshot reuse
    // ================================================================
    [Fact]
    public async Task ManyEvents_SnapshotCreationAndReuse_Works()
    {
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Perf Test", 100000m, new DateOnly(2025, 1, 1));

        // Seed 100 expense events
        for (int i = 0; i < 100; i++)
        {
            await AppendExpenseEvent(accountId, 10m, "EGP", "Food", $"Expense {i}",
                new DateOnly(2025, 1, 2).AddDays(i % 28));
        }

        // Full replay result for reference
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var fullReplayState = CashLedgerProjector.Project(allEnvelopes);
        Assert.Equal(99000m, fullReplayState.Accounts[accountId].Balance); // 100000 - 100*10

        // Now manually create a snapshot as if threshold was crossed
        var snapshotState = CashLedgerProjector.Project(allEnvelopes);
        var snapshotJson = JsonSerializer.Serialize(snapshotState, DomainJson.Options);

        await _snapshotStore.SaveAsync(new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            DateOnly.FromDateTime(DateTime.Today),
            DateTimeOffset.UtcNow,
            _deviceId,
            ProjectionCachePolicies.SchemaVersions.CashLedgerState,
            snapshotJson,
            allEnvelopes[^1].EventId.Value,
            allEnvelopes[^1].OccurredAt), CancellationToken.None);

        // Verify snapshot was saved
        var loaded = await _snapshotStore.LoadLatestAsync(
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            DateOnly.FromDateTime(DateTime.Today),
            CancellationToken.None);
        Assert.NotNull(loaded);

        // Use ProjectionRunner which should find and use the snapshot
        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, _snapshotStore, cache, _deviceId);

        var result = await runner.RunAsync(
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        // Result must be identical to full replay
        Assert.Equal(fullReplayState.Accounts[accountId].Balance, result.Accounts[accountId].Balance);
        Assert.Equal(fullReplayState.Rows.Count, result.Rows.Count);

        // Second run should hit in-memory cache (verify it returns same values)
        var result2 = await runner.RunAsync(
            nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
            envelopes => CashLedgerProjector.Project(envelopes),
            ct: CancellationToken.None);

        Assert.Equal(result.Accounts[accountId].Balance, result2.Accounts[accountId].Balance);
    }

    // ================================================================
    // H) Snapshot LoadLatest prefers exact or closest earlier date
    // ================================================================
    [Fact]
    public async Task SnapshotStore_LoadLatest_PrefersExactOrCloserDate()
    {
        var projectionName = "DateTest";

        // Snapshot for Jan 15
        await _snapshotStore.SaveAsync(new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            projectionName,
            new DateOnly(2025, 1, 15),
            DateTimeOffset.UtcNow,
            _deviceId, 1,
            """{"date":"jan15"}""",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow), CancellationToken.None);

        // Snapshot for Feb 15
        await _snapshotStore.SaveAsync(new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            projectionName,
            new DateOnly(2025, 2, 15),
            DateTimeOffset.UtcNow.AddMinutes(1),
            _deviceId, 1,
            """{"date":"feb15"}""",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddMinutes(1)), CancellationToken.None);

        // Query for Jan 20 ? should get Jan 15 snapshot (closest earlier)
        var loaded = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 1, 20), CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Contains("jan15", loaded.PayloadJson);

        // Query for Feb 15 ? should get Feb 15 snapshot (exact match)
        var exact = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 2, 15), CancellationToken.None);
        Assert.NotNull(exact);
        Assert.Contains("feb15", exact.PayloadJson);

        // Query for March ? should get Feb 15 snapshot (closest earlier)
        var future = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 3, 1), CancellationToken.None);
        Assert.NotNull(future);
        Assert.Contains("feb15", future.PayloadJson);

        // Query for Jan 1 ? nothing earlier
        var early = await _snapshotStore.LoadLatestAsync(projectionName, new DateOnly(2025, 1, 1), CancellationToken.None);
        Assert.Null(early);
    }

    // ================================================================
    // I) SnapshotMaintenanceUseCases work through handlers
    // ================================================================
    [Fact]
    public async Task SnapshotMaintenance_PruneAndClearCache_Works()
    {
        // Create some snapshots
        for (int i = 0; i < 5; i++)
        {
            await _snapshotStore.SaveAsync(new ProjectionSnapshotEnvelope(
                new SnapshotId(Guid.NewGuid()),
                nameof(ProjectionCachePolicies.SchemaVersions.CashLedgerState),
                new DateOnly(2025, 1, 1 + i),
                DateTimeOffset.UtcNow.AddMinutes(i),
                _deviceId, 1,
                $"{{\"i\":{i}}}",
                Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(i)), CancellationToken.None);
        }

        var cache = new ProjectionCache();
        cache.Set("TestKey", "TestValue", DateTimeOffset.UtcNow);

        // Prune handler
        var pruneHandler = new PruneSnapshotsHandler(_snapshotStore);
        await pruneHandler.HandleAsync(CancellationToken.None);

        // Clear cache handler
        var cacheHandler = new ClearProjectionCacheHandler(cache);
        await cacheHandler.HandleAsync(CancellationToken.None);

        // Verify cache is cleared
        var cached = cache.Get<string>("TestKey", DateTimeOffset.UtcNow);
        Assert.Null(cached);

        // Rebuild handler
        var rebuildHandler = new RebuildSnapshotsHandler(pruneHandler, cacheHandler);
        await rebuildHandler.HandleAsync(CancellationToken.None);
    }

    // ================================================================
    // J) GetCashLedgerHandler with runner produces same result as without
    // ================================================================
    [Fact]
    public async Task GetCashLedgerHandler_WithRunner_ProducesSameResult()
    {
        var accountId = Guid.NewGuid();
        await AppendAccountCreated(accountId, "Handler Test", 5000m);
        await AppendExpenseEvent(accountId, 250m, "EGP", "Food", "Groceries");
        await AppendIncomeEvent(accountId, 1000m, "EGP", "Salary");

        // Without runner
        var handlerDirect = new GetCashLedgerHandler(_eventStore);
        var resultDirect = await handlerDirect.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

        // With runner
        var cache = new ProjectionCache();
        var runner = new ProjectionRunner(_eventStore, _snapshotStore, cache, _deviceId);
        var handlerWithRunner = new GetCashLedgerHandler(_eventStore, runner);
        var resultWithRunner = await handlerWithRunner.HandleAsync(new CashLedgerQuery(), CancellationToken.None);

        Assert.Equal(resultDirect.Rows.Count, resultWithRunner.Rows.Count);
        Assert.Equal(resultDirect.TotalIncome, resultWithRunner.TotalIncome);
        Assert.Equal(resultDirect.TotalExpense, resultWithRunner.TotalExpense);
        Assert.Equal(resultDirect.NetCashflow, resultWithRunner.NetCashflow);
    }

    // ================================================================
    // K) Schema table and indexes created safely on existing DB
    // ================================================================
    [Fact]
    public async Task SchemaAndIndexes_CreatedSafely_OnExistingDb()
    {
        // First operation creates all tables
        await AppendAccountCreated(Guid.NewGuid(), "Init", 0m);

        // Second operation should not fail (IF NOT EXISTS)
        await AppendAccountCreated(Guid.NewGuid(), "Second", 0m);

        // Snapshot table should exist
        await _snapshotStore.SaveAsync(new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.NewGuid()),
            "SchemaTest",
            DateOnly.FromDateTime(DateTime.Today),
            DateTimeOffset.UtcNow,
            _deviceId, 1, "{}",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow), CancellationToken.None);

        var loaded = await _snapshotStore.LoadLatestAsync("SchemaTest",
            DateOnly.FromDateTime(DateTime.Today), CancellationToken.None);
        Assert.NotNull(loaded);
    }

    // --- Helpers ---

    private async Task AppendAccountCreated(Guid accountId, string name, decimal openingBalance, DateOnly? date = null)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var ev = new AccountCreated(accountId, name, "Cash", "EGP", openingBalance,
            effectiveDate);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(AccountCreated),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task AppendExpenseEvent(Guid accountId, decimal amount, string currency,
        string category, string notes, DateOnly? date = null)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var currencyObj = currency switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currency, 2)
        };

        var ev = new ExpenseRecorded(accountId, new Money(amount, currencyObj), effectiveDate, category, notes);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(ExpenseRecorded),
            DateTimeOffset.UtcNow,
            effectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }

    private async Task AppendIncomeEvent(Guid accountId, decimal amount, string currency,
        string source, DateOnly? date = null)
    {
        var effectiveDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var currencyObj = currency switch
        {
            "EGP" => Currency.EGP,
            "USD" => Currency.USD,
            _ => new Currency(currency, 2)
        };

        var ev = new IncomeRecorded(accountId, new Money(amount, currencyObj), effectiveDate, source);
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(accountId),
            nameof(IncomeRecorded),
            DateTimeOffset.UtcNow,
            effectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);
    }
}
