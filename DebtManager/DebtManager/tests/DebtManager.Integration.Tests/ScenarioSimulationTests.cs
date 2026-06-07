using System.Text.Json;
using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;

namespace DebtManager.Integration.Tests;

public class ScenarioSimulationTests
{
    [Fact]
    public async Task Scenario_ExtraPayment_DoesNotModifyRealDb_ButChangesProjection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_sim_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var ruleEngine = new SqliteRuleEngine(repo, resolver);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // create obligation
        await new CreateObligationHandler(store).HandleAsync(
            new CreateObligationCommand(obligationId, "Tuition", "Education", 30000, "EGP", new DateOnly(2026, 9, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // define schedule
        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 9, 15), 10000),
                new FixedDateItem(new DateOnly(2026, 11, 30), 10000),
            },
            new[] { "tuition" });

        await new DefineScheduleHandler(store).HandleAsync(
            new DefineScheduleCommand(scheduleId, obligationId, "fixed_dates", JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // baseline payment = 0, as-of is after first due date
        var asOf = new DateOnly(2026, 10, 1);

        var sim = new SimulateScenarioHandler(store, ruleEngine);

        var result = await sim.HandleAsync(
            new SimulateScenarioCommand(
                ObligationId: obligationId,
                AsOfDate: asOf,
                HorizonEndDate: new DateOnly(2027, 12, 31),
                Hypotheses: new[]
                {
            new Hypothesis(
                Type: HypothesisType.ExtraPayment,
                EffectiveDate: new DateOnly(2026, 9, 20),
                Amount: 10000,
                CurrencyCode: "EGP",
                Reference: "pay first")
                }
            ),
            actorUserId,
            deviceId,
            CancellationToken.None);

        // Scenario must have higher payments than baseline
        Assert.True(result.Scenario.TotalPayments.Amount > result.Baseline.TotalPayments.Amount);

        // Real DB must not contain the simulated payment
        var realStream = await store.ReadStreamAsync(new DebtManager.Domain.Events.StreamId(obligationId), upTo: asOf, CancellationToken.None);
        var hasSimPayment = realStream.Any(e => e.EventType == nameof(DebtManager.Domain.Events.PaymentMade) &&
                                               e.PayloadJson.Contains("pay first"));
        Assert.False(hasSimPayment);

        // cleanup
        var wal = dbPath + "-wal";
        var shm = dbPath + "-shm";
        for (int i = 0; i < 30; i++)
        {
            try
            {
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
                if (File.Exists(dbPath)) File.Delete(dbPath);
                break;
            }
            catch (IOException) when (i < 29)
            {
                await Task.Delay(100);
            }
        }
    }
}
