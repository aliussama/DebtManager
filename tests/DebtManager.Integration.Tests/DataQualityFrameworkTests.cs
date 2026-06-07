using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections;
using DebtManager.Domain.Quality;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public sealed class DataQualityFrameworkTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly Guid _deviceId = Guid.NewGuid();

    public DataQualityFrameworkTests()
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _dbPath = Path.Combine(Path.GetTempPath(), $"DQTests_{id}.db");
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

    [Fact]
    public async Task Scan_WritesOnlyScanEvent_AndNoFinancialMutations()
    {
        // Arrange: create an account to have some data
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 1000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopesBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var countBefore = envelopesBefore.Count;

        // Act
        var handler = new RunDataQualityScanHandler(_eventStore);
        var summary = await handler.HandleAsync(_actorUserId, _deviceId, CancellationToken.None);

        // Assert
        var envelopesAfter = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var newEvents = envelopesAfter.Skip(countBefore).ToList();

        // Only scan event should be added (no financial mutations)
        Assert.Single(newEvents);
        Assert.Equal(nameof(DataQualityScanRecorded), newEvents[0].EventType);
        Assert.True(summary.TotalIssues >= 0);
    }

    [Fact]
    public async Task Detects_CashOrphanAccount_Issue()
    {
        // Arrange: manually append an expense referencing a non-existent account
        var fakeAccountId = Guid.NewGuid();
        var ev = new ExpenseRecorded(fakeAccountId, new Money(100m, Currency.EGP),
            new DateOnly(2025, 1, 15), "Food", "Orphan test");
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()), new StreamId(fakeAccountId),
            nameof(ExpenseRecorded), DateTimeOffset.UtcNow, new DateOnly(2025, 1, 15),
            _actorUserId, _deviceId, Guid.NewGuid(), null, 1,
            JsonSerializer.Serialize(ev, DomainJson.Options));
        await _eventStore.AppendAsync(env, CancellationToken.None);

        // Act
        var scanHandler = new RunDataQualityScanHandler(_eventStore);
        var summary = await scanHandler.HandleAsync(_actorUserId, _deviceId, CancellationToken.None);

        var issuesHandler = new GetDataQualityIssuesHandler(_eventStore);
        var issues = await issuesHandler.HandleAsync(
            new DataQualityIssuesQuery(Area: DataQualityArea.Cash), CancellationToken.None);

        // Assert
        Assert.Contains(issues, i => i.Code == "DQ-CASH-002");
    }

    [Fact]
    public async Task Detects_BankDecisionConflict_Issue()
    {
        // Arrange: create an imported transaction with conflicting decisions
        var importedId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Start a batch
        await AppendEvent(new BankImportBatchStarted(batchId, profileId, accountId, "test.csv", "hash123", 1, new DateOnly(2025, 1, 1)));

        // Import a transaction
        await AppendEvent(new BankTransactionImported(importedId, batchId, accountId,
            new DateOnly(2025, 1, 5), 100m, "EGP", "Test", "REF", "Counter", "In", "{}", new DateOnly(2025, 1, 5)));

        // Apply it
        var appliedEventId = Guid.NewGuid();
        await AppendEvent(new BankTransactionApplied(importedId, appliedEventId, "Income", new DateOnly(2025, 1, 5)));

        // Also ignore it (creates a conflict)
        await AppendEvent(new BankTransactionIgnored(importedId, new DateOnly(2025, 1, 5), "duplicate"));

        // Act
        var issues = await new GetDataQualityIssuesHandler(_eventStore).HandleAsync(
            new DataQualityIssuesQuery(Area: DataQualityArea.BankImport), CancellationToken.None);

        // Assert
        Assert.Contains(issues, i => i.Code == "DQ-BANK-002");
    }

    [Fact]
    public async Task ApplyFix_RevertsBankDecision_AppendsRevertEvent_AndAutoFixApplied()
    {
        // Arrange: create conflict
        var importedId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        await AppendEvent(new BankImportBatchStarted(batchId, profileId, accountId, "test.csv", "hash", 1, new DateOnly(2025, 1, 1)));
        await AppendEvent(new BankTransactionImported(importedId, batchId, accountId,
            new DateOnly(2025, 1, 5), 200m, "EGP", "Conflict", "REF2", "Counter", "In", "{}", new DateOnly(2025, 1, 5)));
        await AppendEvent(new BankTransactionApplied(importedId, Guid.NewGuid(), "Income", new DateOnly(2025, 1, 5)));
        await AppendEvent(new BankTransactionIgnored(importedId, new DateOnly(2025, 1, 5), "dup"));

        // Find the issue
        var issues = await new GetDataQualityIssuesHandler(_eventStore).HandleAsync(
            new DataQualityIssuesQuery(Area: DataQualityArea.BankImport), CancellationToken.None);
        var conflict = issues.First(i => i.Code == "DQ-BANK-002");

        // Act
        var revertHandler = new RevertImportedDecisionHandler(_eventStore);
        var applyHandler = new ApplyFixHandler(_eventStore, revertHandler);
        await applyHandler.HandleAsync(
            new ApplyFixCommand(conflict.IssueId, "RevertBankDecision"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Assert
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(allEnvelopes, e => e.EventType == nameof(BankTransactionDecisionReverted));
        Assert.Contains(allEnvelopes, e => e.EventType == nameof(DataQualityAutoFixApplied));
        Assert.Contains(allEnvelopes, e => e.EventType == nameof(DataQualityIssueResolved));
    }

    [Fact]
    public async Task PreviewFix_DoesNotWriteEvents()
    {
        // Arrange
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Streaming", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        // Archive category
        await new ArchiveCategoryHandler(_eventStore).HandleAsync(
            new ArchiveCategoryCommand(catId, "removed"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Create recurring that references archived category
        await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId, 50m, "EGP", catId,
                null, "Netflix", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var envelopesBefore = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var countBefore = envelopesBefore.Count;

        // Act
        var issues = await new GetDataQualityIssuesHandler(_eventStore).HandleAsync(
            new DataQualityIssuesQuery(Area: DataQualityArea.Recurring), CancellationToken.None);
        var issue = issues.FirstOrDefault(i => i.Code == "DQ-REC-002");
        Assert.NotNull(issue);

        var previewHandler = new PreviewFixHandler(_eventStore);
        var result = await previewHandler.HandleAsync(
            new PreviewFixCommand(issue!.IssueId, "ArchiveRecurring"), CancellationToken.None);

        // Assert: no new events
        var envelopesAfter = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Equal(countBefore, envelopesAfter.Count);
        Assert.True(result.CanApply);
    }

    [Fact]
    public async Task Acknowledge_And_Resolve_WriteEvents_And_StateUpdates()
    {
        // Arrange: create data that triggers an issue (incomplete setup + data)
        await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 500m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var issues = await new GetDataQualityIssuesHandler(_eventStore).HandleAsync(
            new DataQualityIssuesQuery(), CancellationToken.None);
        // DQ-SETUP-001 should be detected (setup incomplete but account exists)
        var setupIssue = issues.FirstOrDefault(i => i.Code == "DQ-SETUP-001");
        Assert.NotNull(setupIssue);

        // Act: Acknowledge
        var ackHandler = new AcknowledgeIssueHandler(_eventStore);
        await ackHandler.HandleAsync(
            new AcknowledgeIssueCommand(setupIssue!.IssueId, "Will fix later"),
            _actorUserId, _deviceId, CancellationToken.None);

        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        Assert.Contains(allEnvelopes, e => e.EventType == nameof(DataQualityIssueAcknowledged));

        var dqState = DataQualityProjector.Project(allEnvelopes);
        Assert.Contains(setupIssue.IssueId, dqState.AcknowledgedIssueIds);

        // Act: Resolve
        var resolveHandler = new ResolveIssueHandler(_eventStore);
        await resolveHandler.HandleAsync(
            new ResolveIssueCommand(setupIssue.IssueId, "ManualResolve", "{}"),
            _actorUserId, _deviceId, CancellationToken.None);

        allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        dqState = DataQualityProjector.Project(allEnvelopes);
        Assert.Contains(setupIssue.IssueId, dqState.ResolvedIssueIds);
    }

    [Fact]
    public async Task DeterministicScan_SameEventsProduceSameIssueCodes()
    {
        // Arrange
        await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 500m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Act: run rules twice
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var asOf = DateOnly.FromDateTime(DateTime.Today);

        var cashState = CashLedgerProjector.Project(allEnvelopes);
        var bankState = BankImportProjector.Project(allEnvelopes);
        var recurringState = RecurringProjector.Project(allEnvelopes);
        var budgetState = BudgetProjector.Project(allEnvelopes);
        var categoryState = CategoryProjector.Project(allEnvelopes);
        var assetsState = AssetsProjector.Project(allEnvelopes);
        var portfolioState = PortfolioProjector.Project(allEnvelopes);
        var taxState = TaxProjector.Project(allEnvelopes);
        var goalsState = GoalsProjector.Project(allEnvelopes);
        var setupState = SetupProjector.Project(allEnvelopes);

        var issues1 = DataQualityRules.RunAll(allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOf);

        var issues2 = DataQualityRules.RunAll(allEnvelopes, cashState, bankState, recurringState,
            budgetState, categoryState, assetsState, portfolioState,
            taxState, goalsState, setupState, asOf);

        // Assert: same codes, same IDs
        Assert.Equal(issues1.Count, issues2.Count);
        for (int i = 0; i < issues1.Count; i++)
        {
            Assert.Equal(issues1[i].IssueId, issues2[i].IssueId);
            Assert.Equal(issues1[i].Code, issues2[i].Code);
        }
    }

    [Fact]
    public async Task ApplyFix_IsIdempotent_DoesNotDoubleApply()
    {
        // Arrange: create a recurring with archived category
        var accountId = await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Bank", "Bank", 5000m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        var catId = await new CreateCategoryHandler(_eventStore).HandleAsync(
            new CreateCategoryCommand(null, "Old", "expense", null),
            _actorUserId, _deviceId, CancellationToken.None);

        await new ArchiveCategoryHandler(_eventStore).HandleAsync(
            new ArchiveCategoryCommand(catId, "gone"),
            _actorUserId, _deviceId, CancellationToken.None);

        await new CreateRecurringHandler(_eventStore).HandleAsync(
            new CreateRecurringCommand(null, "expense", accountId, 50m, "EGP", catId,
                null, "Sub", "Monthly", 1, new DateOnly(2025, 1, 1), null, false),
            _actorUserId, _deviceId, CancellationToken.None);

        var issues = await new GetDataQualityIssuesHandler(_eventStore).HandleAsync(
            new DataQualityIssuesQuery(Area: DataQualityArea.Recurring), CancellationToken.None);
        var issue = issues.First(i => i.Code == "DQ-REC-002");

        var archiveHandler = new ArchiveRecurringHandler(_eventStore);
        var applyHandler = new ApplyFixHandler(_eventStore, archiveRecurringHandler: archiveHandler);

        // Act: apply twice
        await applyHandler.HandleAsync(
            new ApplyFixCommand(issue.IssueId, "ArchiveRecurring"),
            _actorUserId, _deviceId, CancellationToken.None);

        await applyHandler.HandleAsync(
            new ApplyFixCommand(issue.IssueId, "ArchiveRecurring"),
            _actorUserId, _deviceId, CancellationToken.None);

        // Assert: only one DataQualityAutoFixApplied event
        var allEnvelopes = await _eventStore.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var fixAppliedCount = allEnvelopes.Count(e => e.EventType == nameof(DataQualityAutoFixApplied));
        Assert.Equal(1, fixAppliedCount);
    }

    [Fact]
    public async Task DashboardQuery_ReturnsGroupedCountsAndLatestScan()
    {
        // Arrange
        await new CreateAccountHandler(_eventStore).HandleAsync(
            new CreateAccountCommand(null, "Wallet", "Cash", 500m, "EGP", new DateOnly(2025, 1, 1)),
            _actorUserId, _deviceId, CancellationToken.None);

        // Run scan to create a scan record
        var scanHandler = new RunDataQualityScanHandler(_eventStore);
        await scanHandler.HandleAsync(_actorUserId, _deviceId, CancellationToken.None);

        // Act
        var dashboardHandler = new GetDataQualityDashboardHandler(_eventStore);
        var result = await dashboardHandler.HandleAsync(CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.LastScanTime);
        Assert.True(result.ActiveIssues.Count >= 0);
        Assert.Equal(result.ActiveIssues.Count,
            result.CriticalCount + result.ErrorCount + result.WarningCount + result.InfoCount);
    }

    private async Task AppendEvent<T>(T ev) where T : DomainEvent
    {
        var streamId = Guid.NewGuid();

        // Try to extract a meaningful stream ID from the event
        var type = typeof(T);
        var batchProp = type.GetProperty("BatchId");
        var importedProp = type.GetProperty("ImportedId");
        if (batchProp != null)
            streamId = (Guid)batchProp.GetValue(ev)!;
        else if (importedProp != null)
            streamId = (Guid)importedProp.GetValue(ev)!;

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(streamId),
            type.Name,
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            _actorUserId,
            _deviceId,
            Guid.NewGuid(),
            null,
            1,
            JsonSerializer.Serialize(ev, DomainJson.Options));

        await _eventStore.AppendAsync(envelope, CancellationToken.None);
    }
}
