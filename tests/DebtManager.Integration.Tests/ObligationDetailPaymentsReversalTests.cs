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
/// Integration tests for payment reversal from obligation detail view.
/// </summary>
public class ObligationDetailPaymentsReversalTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;
    private readonly SqliteRulePackResolver _resolver;
    private readonly SqliteRuleEngine _ruleEngine;

    public ObligationDetailPaymentsReversalTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ObligationDetailPaymentsTest_{Guid.NewGuid()}.db");
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
    public async Task ObligationDetail_PaymentsTab_ShowsPaymentsForObligationOnly()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId1 = Guid.NewGuid();
        var obligationId2 = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);

        // Create two obligations
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId1, "Loan A", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId2, "Loan B", "Loan", 20000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedules for both
        var scheduleSpec1 = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 2, 1), 10000) }, null);
        var scheduleSpec2 = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 2, 1), 20000) }, null);

        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId1, "fixed_dates", JsonSerializer.Serialize(scheduleSpec1, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId2, "fixed_dates", JsonSerializer.Serialize(scheduleSpec2, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payments for both obligations
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId1, 5000m, "EGP", new DateOnly(2026, 2, 10), "Payment A1"),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId1, 3000m, "EGP", new DateOnly(2026, 2, 15), "Payment A2"),
            actorUserId, deviceId, CancellationToken.None
        );
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId2, 10000m, "EGP", new DateOnly(2026, 2, 12), "Payment B1"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Get ledger filtered by obligation 1
        var ledger1 = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId1),
            CancellationToken.None
        );

        // Get ledger filtered by obligation 2
        var ledger2 = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId2),
            CancellationToken.None
        );

        // Assert
        // Obligation 1 should have 2 payments
        Assert.Equal(2, ledger1.Count);
        Assert.All(ledger1, p => Assert.Equal(obligationId1, p.ObligationId));
        Assert.Contains(ledger1, p => p.Reference == "Payment A1" && p.Amount == 5000m);
        Assert.Contains(ledger1, p => p.Reference == "Payment A2" && p.Amount == 3000m);

        // Obligation 2 should have 1 payment
        Assert.Single(ledger2);
        Assert.Equal(obligationId2, ledger2.First().ObligationId);
        Assert.Equal("Payment B1", ledger2.First().Reference);
        Assert.Equal(10000m, ledger2.First().Amount);
    }

    [Fact]
    public async Task Reversal_FromObligationDetail_UpdatesSnapshotAndLedger()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var reverseHandler = new ReversePaymentHandler(_eventStore);
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);
        var snapshotHandler = new GetFinancialSnapshotHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 2, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 2, 10), "Initial payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Get snapshot before reversal
        var snapshotBefore = await snapshotHandler.HandleAsync(obligationId, new DateOnly(2026, 2, 15), CancellationToken.None);
        var obligationBefore = snapshotBefore.Obligations[obligationId];
        Assert.Equal(5000m, obligationBefore.TotalPaid.Amount);

        // Get ledger before reversal to find the payment ID
        var ledgerBefore = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId),
            CancellationToken.None
        );
        var payment = ledgerBefore.First(p => !p.IsReversal);
        Assert.False(payment.IsReversed);

        // Act - Reverse the payment
        await reverseHandler.HandleAsync(
            new ReversePaymentCommand(
                ObligationId: obligationId,
                PaymentEventId: payment.PaymentEventId,
                EffectiveDate: new DateOnly(2026, 2, 16),
                Reason: "Reversed from obligation detail"
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Assert - Get snapshot after reversal (reversal is within the as-of date)
        var snapshotAfter = await snapshotHandler.HandleAsync(obligationId, new DateOnly(2026, 2, 20), CancellationToken.None);
        var obligationAfter = snapshotAfter.Obligations[obligationId];

        // Total paid should be reduced (reversal negates the payment)
        // The projection subtracts the reversed amount when PaymentReversed event is applied
        Assert.Equal(0m, obligationAfter.TotalPaid.Amount);

        // Outstanding should increase back to original
        var installmentAfter = snapshotAfter.Installments.First(i => i.ObligationId == obligationId);
        Assert.Equal(10000m, installmentAfter.Outstanding.Amount);

        // Ledger should now contain reversal row
        var ledgerAfter = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId),
            CancellationToken.None
        );

        // Should have original payment + reversal = 2 rows
        Assert.Equal(2, ledgerAfter.Count);

        // Original payment should be marked as reversed
        var originalPayment = ledgerAfter.First(p => !p.IsReversal);
        Assert.True(originalPayment.IsReversed);

        // Reversal row should exist
        var reversalRow = ledgerAfter.First(p => p.IsReversal);
        Assert.Equal(-5000m, reversalRow.Amount); // Negative for reversal
        Assert.Equal(payment.PaymentEventId, reversalRow.OriginalPaymentEventId);
        Assert.Equal("Reversed from obligation detail", reversalRow.Reason);
    }

    [Fact]
    public async Task Reversal_Twice_IsBlocked()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var reverseHandler = new ReversePaymentHandler(_eventStore);
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);

        // Create obligation and schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 2, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 2, 10), "Test payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Get the payment ID
        var ledger = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId),
            CancellationToken.None
        );
        var paymentId = ledger.First(p => !p.IsReversal).PaymentEventId;

        // Reverse once - should succeed
        await reverseHandler.HandleAsync(
            new ReversePaymentCommand(obligationId, paymentId, new DateOnly(2026, 2, 15), "First reversal"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act & Assert - Second reversal should be blocked
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reverseHandler.HandleAsync(
                new ReversePaymentCommand(obligationId, paymentId, new DateOnly(2026, 2, 16), "Second reversal attempt"),
                actorUserId, deviceId, CancellationToken.None
            )
        );

        Assert.Contains("already been reversed", ex.Message);
    }

    [Fact]
    public async Task ObligationDetail_AuditTrail_ShowsReversalEntry()
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
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);

        // Create obligation and schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 2, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record and reverse payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 5000m, "EGP", new DateOnly(2026, 2, 10), "Test payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        var ledger = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId),
            CancellationToken.None
        );
        var paymentId = ledger.First(p => !p.IsReversal).PaymentEventId;

        await reverseHandler.HandleAsync(
            new ReversePaymentCommand(obligationId, paymentId, new DateOnly(2026, 2, 15), "Test reversal"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Get snapshot which includes audit trail
        var snapshot = await snapshotHandler.HandleAsync(obligationId, new DateOnly(2026, 2, 20), CancellationToken.None);

        // Assert - Audit should contain reversal entries
        var auditEntries = snapshot.Audit.Where(a => a.ObligationId == obligationId).ToList();

        // Should have PaymentReversed entry (category is "Payment", message contains "reversed")
        Assert.Contains(auditEntries, a => 
            a.Category == "Payment" && 
            a.Message.Contains("reversed", StringComparison.OrdinalIgnoreCase));
    }
}
