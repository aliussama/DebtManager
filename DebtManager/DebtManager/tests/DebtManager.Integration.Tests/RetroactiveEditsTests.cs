using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;

namespace DebtManager.Integration.Tests;

public class RetroactiveEditsTests
{
    [Fact]
    public async Task Retroactive_ScenarioChange_AltersProjection_WithoutDeletingHistory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_retro_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // 1) Create obligation
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(
            new CreateObligationCommand(
                ObligationId: obligationId,
                Name: "Retro Test",
                ObligationType: "Education",
                PrincipalAmount: 10000,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 1, 1)
            ),
            actor, device, CancellationToken.None);

        // 2) Define schedule: one installment due Jan 10
        var spec = new DebtManager.Domain.Scheduling.FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 1, 10), 10000),
                new FixedDateItem(new DateOnly(2026, 2, 10), 10000),
            },
            new[] { "retro" }
        );

        var define = new DefineScheduleHandler(store);
        await define.HandleAsync(
            new DefineScheduleCommand(
                ScheduleId: scheduleId,
                ObligationId: obligationId,
                ScheduleType: "fixed_dates",
                ScheduleSpecJson: JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options),
                Timezone: "Africa/Cairo",
                EffectiveDate: new DateOnly(2026, 1, 1)
            ),
            actor, device, CancellationToken.None);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var ruleEngine = new SqliteRuleEngine(repo, resolver);

        // 3) Record payment on Jan 9 (on time)
        var record = new RecordPaymentHandler(store, ruleEngine);
        await record.HandleAsync(
            new RecordPaymentCommand(
                ObligationId: obligationId,
                Amount: 10000,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 1, 9),
                Reference: "ON_TIME"
            ),
            actor, device, CancellationToken.None);

        // 4) Snapshot engine
        var engine = new SqliteRuleEngine(repo, resolver);
        var snapshot = new GetFinancialSnapshotHandler(store, engine);

        // Baseline snapshot as of Feb 15: first should be fully paid, second should not
        var asOf = new DateOnly(2026, 2, 15);
        var baseline = await snapshot.HandleAsync(obligationId, asOf, CancellationToken.None);
        var orderedBaseline = baseline.Installments.OrderBy(x => x.DueDate).ToList();

        // First installment (Jan 10) should be fully paid after the Jan 9 payment
        var firstBaseline = orderedBaseline.First(i => i.DueDate == new DateOnly(2026, 1, 10));
        Assert.True(firstBaseline.IsFullyPaid);
        Assert.Equal(0m, firstBaseline.Outstanding.Amount);

        // Second installment (Feb 10) should NOT be fully paid at Feb 15 baseline
        var secondBaseline = orderedBaseline.First(i => i.DueDate == new DateOnly(2026, 2, 10));
        Assert.False(secondBaseline.IsFullyPaid);
        Assert.True(secondBaseline.Outstanding.Amount > 0m);

        Assert.True(baseline.Installments.Count > 0, "No installments were generated in baseline snapshot.");

        // 5) Scenario: add a hypothetical extra payment BEFORE AsOfDate
        // This must alter projection, but MUST NOT alter DB history.
        var simulate = new SimulateScenarioHandler(store, engine);

        var result = await simulate.HandleAsync(
            new SimulateScenarioCommand(
                ObligationId: obligationId,
                AsOfDate: asOf,
                HorizonEndDate: new DateOnly(2026, 2, 28),
Hypotheses: new[]
{
    new Hypothesis(
        Type: HypothesisType.ExtraPayment,
        EffectiveDate: asOf,          // IMPORTANT: must be included in scenario projection
        Amount: 5000m,
        CurrencyCode: "EGP",
        Reference: "Retro extra payment"
    )
}
            ),
            actor, device, CancellationToken.None);

        var scenarioInst = result.Scenario.Installments.OrderBy(x => x.DueDate).First();

        // Since baseline is already fully paid, extra payment can't reduce outstanding below zero.
        // So the robust assertion here is: scenario != baseline in totals (payments) OR audit/charges.
        // Scenario must differ in a measurable financial way
        Assert.True(result.Scenario.TotalPayments.Amount > result.Baseline.TotalPayments.Amount);

        // Extra payment must reduce outstanding on the 2nd installment
        var b2 = result.Baseline.Installments.OrderBy(x => x.DueDate).Skip(1).First();
        var s2 = result.Scenario.Installments.OrderBy(x => x.DueDate).Skip(1).First();
        Assert.True(s2.Paid.Amount > b2.Paid.Amount);

        // 6) IMPORTANT invariant: real DB history wasn't deleted or rewritten by simulation
        var stream = await store.ReadStreamAsync(new DebtManager.Domain.Events.StreamId(obligationId), null, CancellationToken.None);
        Assert.Contains(stream, e => e.PayloadJson.Contains("ON_TIME"));

        var orderedScenario = result.Scenario.Installments.OrderBy(x => x.DueDate).ToList();

        var secondScenario = orderedScenario.First(i => i.DueDate == new DateOnly(2026, 2, 10));

        // Extra payment at asOf should increase paid on the 2nd installment
        Assert.True(secondScenario.Paid.Amount > secondBaseline.Paid.Amount);

        await Cleanup(dbPath);
    }

    private static async Task Cleanup(string dbPath)
    {
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
