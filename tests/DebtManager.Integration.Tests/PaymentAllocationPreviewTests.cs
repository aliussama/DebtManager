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
/// Integration tests for PreviewPaymentAllocationHandler.
/// </summary>
public class PaymentAllocationPreviewTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;
    private readonly SqliteRulePackResolver _resolver;
    private readonly SqliteRuleEngine _ruleEngine;

    public PaymentAllocationPreviewTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"AllocationPreviewTest_{Guid.NewGuid()}.db");
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
    public async Task Preview_MatchesRecordedAllocations()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var previewHandler = new PreviewPaymentAllocationHandler(_eventStore, _ruleEngine);
        var ledgerHandler = new GetPaymentsLedgerHandler(_eventStore);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Test Loan",
                "Loan",
                20000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with 2 installments
        var scheduleSpec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 2, 1), 10000),
                new FixedDateItem(new DateOnly(2026, 3, 1), 10000),
            },
            null);

        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(
                Guid.NewGuid(),
                obligationId,
                "fixed_dates",
                JsonSerializer.Serialize(scheduleSpec, DomainJson.Options),
                "Africa/Cairo",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Preview a payment that spans both installments (12000 EGP)
        var preview = await previewHandler.HandleAsync(
            new PreviewPaymentAllocationCommand(
                ObligationId: obligationId,
                Amount: 12000m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 15),
                AsOfDate: new DateOnly(2026, 2, 15)
            ),
            CancellationToken.None
        );

        // Assert preview
        Assert.Null(preview.ErrorMessage);
        Assert.True(preview.HasSchedule);
        Assert.Equal(12000m, preview.InputAmount);
        Assert.Equal(0m, preview.UnappliedAmount);

        // Should have allocations to installments
        Assert.Equal(2, preview.InstallmentAllocations.Count);

        // First installment should get 10000 (full)
        var first = preview.InstallmentAllocations.First();
        Assert.Equal(10000m, first.AllocatedNow);
        Assert.Equal("Paid", first.Status);

        // Second installment should get 2000 (partial)
        var second = preview.InstallmentAllocations.Skip(1).First();
        Assert.Equal(2000m, second.AllocatedNow);
        Assert.Equal("Partial", second.Status);

        // Now record the payment
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 12000m, "EGP", new DateOnly(2026, 2, 15), "Test payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Get the ledger to see actual allocations
        var ledger = await ledgerHandler.HandleAsync(
            new GetPaymentsLedgerQuery(ObligationId: obligationId),
            CancellationToken.None
        );

        Assert.Single(ledger);
        var payment = ledger.First();

        // The allocations should match the preview
        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal(10000m, payment.Allocations.First().Amount);
        Assert.Equal(2000m, payment.Allocations.Skip(1).First().Amount);
    }

    [Fact]
    public async Task Preview_ShowsUnapplied_WhenOverpay()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var previewHandler = new PreviewPaymentAllocationHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Small Loan",
                "Loan",
                5000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with single installment
        var scheduleSpec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 2, 1), 5000),
            },
            null);

        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(
                Guid.NewGuid(),
                obligationId,
                "fixed_dates",
                JsonSerializer.Serialize(scheduleSpec, DomainJson.Options),
                "Africa/Cairo",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Preview a payment larger than outstanding (7000 > 5000)
        var preview = await previewHandler.HandleAsync(
            new PreviewPaymentAllocationCommand(
                ObligationId: obligationId,
                Amount: 7000m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 15),
                AsOfDate: new DateOnly(2026, 2, 15)
            ),
            CancellationToken.None
        );

        // Assert
        Assert.Null(preview.ErrorMessage);
        Assert.True(preview.HasSchedule);
        Assert.Equal(7000m, preview.InputAmount);
        
        // Should have unapplied amount (2000)
        Assert.Equal(2000m, preview.UnappliedAmount);

        // Installment should be fully paid
        Assert.Single(preview.InstallmentAllocations);
        Assert.Equal(5000m, preview.InstallmentAllocations.First().AllocatedNow);
        Assert.Equal("Paid", preview.InstallmentAllocations.First().Status);
    }

    [Fact]
    public async Task Preview_NoSchedule_ReturnsUnapplied()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var previewHandler = new PreviewPaymentAllocationHandler(_eventStore, _ruleEngine);

        // Create obligation WITHOUT schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "No Schedule Loan",
                "Loan",
                10000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Preview a payment
        var preview = await previewHandler.HandleAsync(
            new PreviewPaymentAllocationCommand(
                ObligationId: obligationId,
                Amount: 5000m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 15),
                AsOfDate: new DateOnly(2026, 2, 15)
            ),
            CancellationToken.None
        );

        // Assert - no schedule means all goes to unapplied
        Assert.Null(preview.ErrorMessage);
        Assert.False(preview.HasSchedule);
        Assert.Equal(5000m, preview.UnappliedAmount);
        Assert.Empty(preview.InstallmentAllocations);
    }

    [Fact]
    public async Task Preview_InvalidAmount_ReturnsError()
    {
        // Arrange
        var previewHandler = new PreviewPaymentAllocationHandler(_eventStore, _ruleEngine);

        // Preview with zero amount
        var preview = await previewHandler.HandleAsync(
            new PreviewPaymentAllocationCommand(
                ObligationId: Guid.NewGuid(),
                Amount: 0m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 15),
                AsOfDate: new DateOnly(2026, 2, 15)
            ),
            CancellationToken.None
        );

        // Assert
        Assert.NotNull(preview.ErrorMessage);
        Assert.Contains("greater than zero", preview.ErrorMessage);
    }

    [Fact]
    public async Task Preview_PartialPayment_ShowsCorrectStatus()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var previewHandler = new PreviewPaymentAllocationHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Test Loan",
                "Loan",
                10000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule
        var scheduleSpec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 2, 1), 10000),
            },
            null);

        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(
                Guid.NewGuid(),
                obligationId,
                "fixed_dates",
                JsonSerializer.Serialize(scheduleSpec, DomainJson.Options),
                "Africa/Cairo",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Preview a partial payment (3000 < 10000)
        var preview = await previewHandler.HandleAsync(
            new PreviewPaymentAllocationCommand(
                ObligationId: obligationId,
                Amount: 3000m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 15),
                AsOfDate: new DateOnly(2026, 2, 15)
            ),
            CancellationToken.None
        );

        // Assert
        Assert.Null(preview.ErrorMessage);
        Assert.Single(preview.InstallmentAllocations);
        
        var inst = preview.InstallmentAllocations.First();
        Assert.Equal(10000m, inst.InstallmentAmount);
        Assert.Equal(3000m, inst.AllocatedNow);
        Assert.Equal(7000m, inst.OutstandingAfter);
        Assert.Equal("Partial", inst.Status);
    }
}
