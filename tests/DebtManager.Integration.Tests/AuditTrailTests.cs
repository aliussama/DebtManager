using DebtManager.Application.UseCases;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;
using Xunit;

namespace DebtManager.Integration.Tests;

/// <summary>
/// Integration tests for Audit Trail functionality.
/// </summary>
public class AuditTrailTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;
    private readonly SqliteRulePackResolver _resolver;
    private readonly SqliteRuleEngine _ruleEngine;

    public AuditTrailTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"AuditTrailTest_{Guid.NewGuid()}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _rulePackRepo = new SqliteRulePackRepository(_factory);
        _resolver = new SqliteRulePackResolver(_eventStore);
        _ruleEngine = new SqliteRuleEngine(_rulePackRepo, _resolver);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            if (File.Exists(_dbPath + "-wal")) File.Delete(_dbPath + "-wal");
            if (File.Exists(_dbPath + "-shm")) File.Delete(_dbPath + "-shm");
        }
        catch { }
    }

    [Fact]
    public async Task AuditTrail_ReturnsEntries_ForObligation()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var reverseHandler = new ReversePaymentHandler(_eventStore);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsListHandler = new GetObligationsListHandler(dashboardHandler);
        var auditHandler = new GetAuditTrailHandler(snapshotHandler, obligationsListHandler);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 3, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 2, 15), "Test payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Get ledger to find payment event ID for reversal
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);
        var ledger = await ledgerHandler.HandleAsync(new GetPaymentsLedgerQuery(obligationId), CancellationToken.None);
        var paymentEventId = ledger.First(p => !p.IsReversal).PaymentEventId;

        // Reverse payment
        await reverseHandler.HandleAsync(
            new ReversePaymentCommand(obligationId, paymentEventId, new DateOnly(2026, 2, 20), "Test reversal"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act
        var query = new GetAuditTrailQuery(
            ObligationId: obligationId,
            ToDate: new DateOnly(2026, 12, 31)
        );

        var auditEntries = await auditHandler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotEmpty(auditEntries);

        // Should contain payment entries
        Assert.Contains(auditEntries, e => 
            e.Category == "Payment" && 
            e.Message.Contains("recorded", StringComparison.OrdinalIgnoreCase));

        // Should contain reversal entries
        Assert.Contains(auditEntries, e => 
            e.Category == "Payment" && 
            e.Message.Contains("reversed", StringComparison.OrdinalIgnoreCase));

        // All entries should be for this obligation
        Assert.All(auditEntries, e => Assert.Equal(obligationId, e.ObligationId));
    }

    [Fact]
    public async Task AuditTrail_FilterByDateRange_Works()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsListHandler = new GetObligationsListHandler(dashboardHandler);
        var auditHandler = new GetAuditTrailHandler(snapshotHandler, obligationsListHandler);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 6, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payments on different dates
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 2000m, "EGP", new DateOnly(2026, 2, 1), "February payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 3000m, "EGP", new DateOnly(2026, 4, 1), "April payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Get all entries first
        var allQuery = new GetAuditTrailQuery(
            ObligationId: obligationId,
            ToDate: new DateOnly(2026, 12, 31)
        );
        var allEntries = await auditHandler.HandleAsync(allQuery, CancellationToken.None);

        // Act - Filter by date range (March to May, should only include April payment)
        var filteredQuery = new GetAuditTrailQuery(
            ObligationId: obligationId,
            FromDate: new DateOnly(2026, 3, 1),
            ToDate: new DateOnly(2026, 5, 31)
        );
        var filteredEntries = await auditHandler.HandleAsync(filteredQuery, CancellationToken.None);

        // Assert
        Assert.True(allEntries.Count > filteredEntries.Count,
            $"Filtered count ({filteredEntries.Count}) should be less than all ({allEntries.Count})");

        // April payment should be in filtered results
        Assert.Contains(filteredEntries, e =>
            e.Message.Contains("April", StringComparison.OrdinalIgnoreCase) ||
            e.EffectiveDate == new DateOnly(2026, 4, 1));

        // February payment should NOT be in filtered results  
        Assert.DoesNotContain(filteredEntries, e =>
            e.EffectiveDate == new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task AuditTrail_PortfolioScope_ReturnsMultipleObligations()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId1 = Guid.NewGuid();
        var obligationId2 = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsListHandler = new GetObligationsListHandler(dashboardHandler);
        var auditHandler = new GetAuditTrailHandler(snapshotHandler, obligationsListHandler);

        // Create first obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId1, "Loan A", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        var scheduleSpec1 = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 6, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId1, "fixed_dates", JsonSerializer.Serialize(scheduleSpec1, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId1, 5000m, "EGP", new DateOnly(2026, 3, 1), "Payment A"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Create second obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId2, "Loan B", "Loan", 20000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        var scheduleSpec2 = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 6, 1), 20000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId2, "fixed_dates", JsonSerializer.Serialize(scheduleSpec2, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId2, 10000m, "EGP", new DateOnly(2026, 3, 15), "Payment B"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Query portfolio scope (no obligation filter)
        var query = new GetAuditTrailQuery(
            ObligationId: null, // All obligations
            ToDate: new DateOnly(2026, 12, 31)
        );
        var auditEntries = await auditHandler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotEmpty(auditEntries);

        // Should contain entries from both obligations
        var obligationIdsInResults = auditEntries
            .Where(e => e.ObligationId.HasValue)
            .Select(e => e.ObligationId!.Value)
            .Distinct()
            .ToList();

        Assert.Contains(obligationId1, obligationIdsInResults);
        Assert.Contains(obligationId2, obligationIdsInResults);

        // Should have payment entries for both
        Assert.Contains(auditEntries, e => 
            e.ObligationId == obligationId1 && 
            e.Category == "Payment");
        Assert.Contains(auditEntries, e => 
            e.ObligationId == obligationId2 && 
            e.Category == "Payment");
    }

    [Fact]
    public async Task AuditTrail_EntriesAreSortedByDateDescending()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsListHandler = new GetObligationsListHandler(dashboardHandler);
        var auditHandler = new GetAuditTrailHandler(snapshotHandler, obligationsListHandler);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 30000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 12, 1), 30000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payments on different dates (not in order)
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 3, 1), "March payment"),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 5, 1), "May payment"),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 2, 1), "February payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act
        var query = new GetAuditTrailQuery(
            ObligationId: obligationId,
            ToDate: new DateOnly(2026, 12, 31)
        );
        var auditEntries = await auditHandler.HandleAsync(query, CancellationToken.None);

        // Assert - Entries should be sorted by EffectiveDate descending
        var effectiveDates = auditEntries.Select(e => e.EffectiveDate).ToList();
        var sortedDates = effectiveDates.OrderByDescending(d => d).ToList();

        Assert.Equal(sortedDates, effectiveDates);
    }

    [Fact]
    public async Task AuditTrail_IncludesObligationName()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();
        var obligationName = "My Test Loan XYZ";

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);
        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var obligationsListHandler = new GetObligationsListHandler(dashboardHandler);
        var auditHandler = new GetAuditTrailHandler(snapshotHandler, obligationsListHandler);

        // Create obligation with specific name
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, obligationName, "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 6, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 3, 1), "Test payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act
        var query = new GetAuditTrailQuery(
            ObligationId: obligationId,
            ToDate: new DateOnly(2026, 12, 31)
        );
        var auditEntries = await auditHandler.HandleAsync(query, CancellationToken.None);

        // Assert
        Assert.NotEmpty(auditEntries);
        Assert.All(auditEntries, e => Assert.Equal(obligationName, e.ObligationName));
    }
}
