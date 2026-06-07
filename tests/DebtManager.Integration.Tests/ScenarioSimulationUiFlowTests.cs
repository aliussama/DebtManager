using DebtManager.Application.Simulation;
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
/// Integration tests for Scenario Simulation UI flow.
/// Verifies simulation does not modify real DB and returns meaningful diffs.
/// </summary>
public class ScenarioSimulationUiFlowTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteEventStore _eventStore;
    private readonly SqliteRulePackRepository _rulePackRepo;
    private readonly SqliteRulePackResolver _resolver;
    private readonly SqliteRuleEngine _ruleEngine;

    public ScenarioSimulationUiFlowTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"ScenarioSimulationTest_{Guid.NewGuid()}.db");
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
    public async Task SimulateScenario_DoesNotModifyRealDb()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with one installment
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 3, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Count events before simulation
        var streamBefore = await _eventStore.ReadStreamAsync(new StreamId(obligationId), upTo: new DateOnly(2030, 1, 1), CancellationToken.None);
        var countBefore = streamBefore.Count;

        // Act - Run simulation with ExtraPayment hypothesis
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 2, 1),
            HorizonEndDate: new DateOnly(2026, 6, 1),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.ExtraPayment,
                    EffectiveDate: new DateOnly(2026, 2, 15),
                    Amount: 5000m,
                    CurrencyCode: "EGP",
                    Reference: "Scenario ExtraPayment Test"
                )
            }
        );

        var result = await simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None);

        // Assert - Count events after simulation
        var streamAfter = await _eventStore.ReadStreamAsync(new StreamId(obligationId), upTo: new DateOnly(2030, 1, 1), CancellationToken.None);
        var countAfter = streamAfter.Count;

        // Event count should be unchanged (simulation does not write to real store)
        Assert.Equal(countBefore, countAfter);

        // Verify no PaymentMade with the hypothesis reference exists in real store
        var paymentEvents = streamAfter.Where(e => e.EventType == nameof(PaymentMade)).ToList();
        Assert.DoesNotContain(paymentEvents, e => e.PayloadJson.Contains("Scenario ExtraPayment Test"));

        // Simulation should have returned a result
        Assert.NotNull(result);
        Assert.NotNull(result.Baseline);
        Assert.NotNull(result.Scenario);
    }

    [Fact]
    public async Task SimulateScenario_ReturnsMeaningfulDiff_ExtraPayment()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with one installment due March 1st
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 3, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Simulate ExtraPayment with AsOfDate after installment due date
        // Hypothesis effective date should be before HorizonEndDate
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 4, 1), // After March 1st
            HorizonEndDate: new DateOnly(2026, 12, 31),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.ExtraPayment,
                    EffectiveDate: new DateOnly(2026, 2, 15), // Payment made before as-of
                    Amount: 5000m,
                    CurrencyCode: "EGP",
                    Reference: "Extra payment"
                )
            }
        );

        var result = await simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None);

        // Assert - Scenario should show higher total payments
        Assert.True(result.Scenario.TotalPayments.Amount > result.Baseline.TotalPayments.Amount,
            $"ScenarioTotalPayments ({result.Scenario.TotalPayments.Amount}) should be > BaselineTotalPayments ({result.Baseline.TotalPayments.Amount})");

        // The payment difference should be the extra payment amount
        var paymentDiff = result.Scenario.TotalPayments.Amount - result.Baseline.TotalPayments.Amount;
        Assert.Equal(5000m, paymentDiff);

        // Installment outstanding should be reduced in scenario
        var baselineInstallment = result.Baseline.Installments.First();
        var scenarioInstallment = result.Scenario.Installments.First();
        Assert.True(scenarioInstallment.Outstanding.Amount < baselineInstallment.Outstanding.Amount,
            $"Scenario outstanding ({scenarioInstallment.Outstanding.Amount}) should be < Baseline outstanding ({baselineInstallment.Outstanding.Amount})");
    }

    [Fact]
    public async Task SimulateScenario_MissPayment_ReducesTotalPayments()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var paymentHandler = new RecordPaymentHandler(_eventStore, _ruleEngine);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with installment due March 1st
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 3, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Record a real payment BEFORE the as-of date
        await paymentHandler.HandleAsync(
            new RecordPaymentCommand(obligationId, 3000m, "EGP", new DateOnly(2026, 2, 10), "Initial payment"),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Simulate missing the payment with AsOfDate after the payment
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 4, 1), // After March 1st and the payment
            HorizonEndDate: new DateOnly(2026, 12, 31),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.MissPayment,
                    EffectiveDate: new DateOnly(2026, 3, 15), // Reversal effective after original payment
                    PaymentReferenceContains: "Initial",
                    Reference: "Missed initial payment"
                )
            }
        );

        var result = await simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None);

        // Assert - Scenario should show lower total payments (the missed payment is reversed)
        Assert.True(result.Scenario.TotalPayments.Amount < result.Baseline.TotalPayments.Amount,
            $"ScenarioTotalPayments ({result.Scenario.TotalPayments.Amount}) should be < BaselineTotalPayments ({result.Baseline.TotalPayments.Amount})");

        // Baseline should have 3000m paid, scenario should have 0m (payment was reversed)
        Assert.Equal(3000m, result.Baseline.TotalPayments.Amount);
        Assert.Equal(0m, result.Scenario.TotalPayments.Amount);
    }

    [Fact]
    public async Task SimulateScenario_MultipleHypotheses_CombinesEffects()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 20000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Define schedule with two installments
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] 
        { 
            new FixedDateItem(new DateOnly(2026, 3, 1), 10000),
            new FixedDateItem(new DateOnly(2026, 4, 1), 10000)
        }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act - Simulate two extra payments with AsOfDate after installments
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 5, 1), // After both installment due dates
            HorizonEndDate: new DateOnly(2026, 12, 31),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.ExtraPayment,
                    EffectiveDate: new DateOnly(2026, 2, 15),
                    Amount: 5000m,
                    CurrencyCode: "EGP",
                    Reference: "First extra"
                ),
                new Hypothesis(
                    Type: HypothesisType.ExtraPayment,
                    EffectiveDate: new DateOnly(2026, 2, 20),
                    Amount: 3000m,
                    CurrencyCode: "EGP",
                    Reference: "Second extra"
                )
            }
        );

        var result = await simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None);

        // Assert - Total payments should reflect both extra payments
        var paymentDiff = result.Scenario.TotalPayments.Amount - result.Baseline.TotalPayments.Amount;
        Assert.Equal(8000m, paymentDiff); // 5000 + 3000

        // Total outstanding should be reduced by 8000 in scenario
        var baselineTotalOutstanding = result.Baseline.Installments.Sum(i => i.Outstanding.Amount);
        var scenarioTotalOutstanding = result.Scenario.Installments.Sum(i => i.Outstanding.Amount);
        Assert.Equal(20000m, baselineTotalOutstanding); // No payments in baseline
        Assert.Equal(12000m, scenarioTotalOutstanding); // 20000 - 8000 = 12000
    }

    [Fact]
    public async Task SimulateScenario_InvalidHypothesis_ThrowsMeaningfulError()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var scheduleHandler = new DefineScheduleHandler(_eventStore);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation and schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );
        var scheduleSpec = new FixedDatesScheduleSpec("EGP", new[] { new FixedDateItem(new DateOnly(2026, 3, 1), 10000) }, null);
        await scheduleHandler.HandleAsync(
            new DefineScheduleCommand(Guid.NewGuid(), obligationId, "fixed_dates", JsonSerializer.Serialize(scheduleSpec, DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act & Assert - MissPayment without PaymentReferenceContains should fail
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 2, 1),
            HorizonEndDate: new DateOnly(2026, 6, 1),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.MissPayment,
                    EffectiveDate: new DateOnly(2026, 2, 15),
                    PaymentReferenceContains: null // Missing required field
                )
            }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None)
        );

        Assert.Contains("PaymentReferenceContains", ex.Message);
    }

    [Fact]
    public async Task SimulateScenario_NoSchedule_ThrowsMeaningfulError()
    {
        // Arrange
        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();

        var createHandler = new CreateObligationHandler(_eventStore);
        var simulateHandler = new SimulateScenarioHandler(_eventStore, _ruleEngine);

        // Create obligation without schedule
        await createHandler.HandleAsync(
            new CreateObligationCommand(obligationId, "Test Loan", "Loan", 10000m, "EGP", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None
        );

        // Act & Assert - Simulation without schedule should fail gracefully
        var command = new SimulateScenarioCommand(
            ObligationId: obligationId,
            AsOfDate: new DateOnly(2026, 2, 1),
            HorizonEndDate: new DateOnly(2026, 6, 1),
            Hypotheses: new[]
            {
                new Hypothesis(
                    Type: HypothesisType.ExtraPayment,
                    EffectiveDate: new DateOnly(2026, 2, 15),
                    Amount: 5000m,
                    CurrencyCode: "EGP"
                )
            }
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => simulateHandler.HandleAsync(command, actorUserId, deviceId, CancellationToken.None)
        );

        Assert.Contains("schedule", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
