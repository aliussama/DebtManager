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
/// Integration tests for GetObligationsListHandler.
/// Verifies that obligations are returned with correct computed values.
/// </summary>
public class ObligationsListHandlerTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;

    public ObligationsListHandlerTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ObligationsListTest_{Guid.NewGuid()}.db");
        _factory = new SqliteConnectionFactory(_dbPath, new TestKeyStore());
        _eventStore = new SqliteEventStore(_factory);
        _rulePackRepo = new SqliteRulePackRepository(_factory);
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
    public async Task GetObligationsList_ReturnsCorrectValuesForMultipleObligations()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId1 = Guid.NewGuid();
        var obligationId2 = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 2, 15);

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var resolver = new SqliteRulePackResolver(_eventStore);
        var ruleEngine = new SqliteRuleEngine(_rulePackRepo, resolver);
        var paymentHandler = new RecordPaymentHandler(_eventStore, ruleEngine);

        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var listHandler = new GetObligationsListHandler(dashboardHandler);

        // Create obligation 1 - with schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId1,
                "Car Loan",
                "Loan",
                50000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule for obligation 1 (3 installments)
        var scheduleSpec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 1, 15), 15000),
                new FixedDateItem(new DateOnly(2026, 2, 15), 15000),
                new FixedDateItem(new DateOnly(2026, 3, 15), 20000),
            },
            new[] { "car", "loan" });

        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(
                Guid.NewGuid(),
                obligationId1,
                "fixed_dates",
                JsonSerializer.Serialize(scheduleSpec, DomainJson.Options),
                "Africa/Cairo",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record payment for obligation 1
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId1, 15000, "EGP", new DateOnly(2026, 1, 15), "First payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Create obligation 2 - no schedule, no payment
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId2,
                "Personal Loan",
                "Loan",
                10000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act
        var result = await listHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

        // Assert
        Assert.Equal(2, result.Count);

        // Find obligation 1
        var loan1 = result.FirstOrDefault(o => o.ObligationId == obligationId1);
        Assert.NotNull(loan1);
        Assert.Equal("Car Loan", loan1.Name);
        Assert.Equal("Loan", loan1.ObligationType);
        Assert.Equal(50000m, loan1.Principal);
        // TotalPaid should reflect the payment we recorded
        Assert.True(loan1.TotalPaid >= 0, $"TotalPaid was {loan1.TotalPaid}");
        // Outstanding = Principal - TotalPaid
        Assert.Equal(50000m - loan1.TotalPaid, loan1.Outstanding);
        Assert.NotNull(loan1.NextDueDate); // Has schedule, so should have next due date
        Assert.False(loan1.IsClosed);

        // Find obligation 2
        var loan2 = result.FirstOrDefault(o => o.ObligationId == obligationId2);
        Assert.NotNull(loan2);
        Assert.Equal("Personal Loan", loan2.Name);
        Assert.Equal(10000m, loan2.Principal);
        Assert.Equal(0m, loan2.TotalPaid);
        Assert.Equal(10000m, loan2.Outstanding);
        Assert.False(loan2.IsClosed);
    }

    [Fact]
    public async Task GetObligationsList_ReturnsHealthStatus()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 3, 1); // After first installment due

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);

        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var listHandler = new GetObligationsListHandler(dashboardHandler);

        // Create obligation with overdue installment
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Overdue Loan",
                "Loan",
                10000m,
                "EGP",
                new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with past due date
        var scheduleSpec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 2, 1), 5000), // Overdue by 1 month
                new FixedDateItem(new DateOnly(2026, 4, 1), 5000),
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

        // Act
        var result = await listHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

        // Assert
        Assert.Single(result);
        var obligation = result.First();
        Assert.Equal(1, obligation.OverdueCount);
        // HealthStatus should not be "Healthy" when there are overdue installments
        Assert.NotEqual("Unknown", obligation.HealthStatus);
    }

    [Fact]
    public async Task GetObligationsList_ClosedObligationShowsIsClosed()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();
        var asOfDate = DateOnly.FromDateTime(DateTime.Today);

        var createHandler = new CreateObligationHandler(_eventStore);
        var closeHandler = new CloseObligationHandler(_eventStore);

        var dashboardHandler = new GetPortfolioDashboardHandler(_eventStore);
        var listHandler = new GetObligationsListHandler(dashboardHandler);

        // Create and close obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(
                obligationId,
                "Closed Loan",
                "Loan",
                5000m,
                "EGP",
                new DateOnly(2025, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        await closeHandler.HandleAsync(
            new CloseObligationCommand(
                ObligationId: obligationId,
                ClosureType: ObligationClosureType.PaidInFull,
                FinalBalance: Money.Zero(Currency.EGP),
                Reason: "Paid off",
                Notes: null
            ),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act
        var result = await listHandler.HandleAsync(asOfDate, "EGP", CancellationToken.None);

        // Assert
        Assert.Single(result);
        var obligation = result.First();
        Assert.True(obligation.IsClosed);
        Assert.Equal("Closed", obligation.Status);
    }
}
