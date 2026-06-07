using System.Text.Json;
using DebtManager.Application.Simulation;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;

namespace DebtManager.Integration.Tests;

public class DelayedPaymentScenarioTests
{
    [Fact]
    public async Task DelayingPayment_CanIncreaseCharges_AndOverdue()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_delay_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var ruleEngine = new SqliteRuleEngine(repo, resolver);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // Obligation + schedule
        await new CreateObligationHandler(store).HandleAsync(
            new CreateObligationCommand(obligationId, "Tuition", "Education", 30000, "EGP", new DateOnly(2026, 9, 1)),
            actorUserId, deviceId, CancellationToken.None);

        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[] { new FixedDateItem(new DateOnly(2026, 9, 15), 10000) },
            new[] { "tuition" });

        await new DefineScheduleHandler(store).HandleAsync(
            new DefineScheduleCommand(scheduleId, obligationId, "fixed_dates", JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options), "Africa/Cairo", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Install and assign penalty rule pack (late penalty if overdue>0)
        var install = new InstallRulePackHandler(repo);
        await install.HandleAsync(new InstallRulePackCommand(
            RulePackId: "pack.tuition.late",
            Name: "Late Penalty Pack",
            Description: null,
            VersionLabel: "2026.01",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: null,
            Status: "active",
            RulesJson: """
{
  "rules": [
    {
      "key": "late_penalty_v1",
      "when": { "all": [ { "fact": "installment.days_overdue", "op": ">", "value": 0 } ] },
      "effect": { "add_charge": { "amount": 100, "label": "Late Penalty", "chargeType": "penalty" } }
    }
  ]
}
"""
        ), CancellationToken.None);

        await new AssignRulePackToObligationHandler(store).HandleAsync(
            new AssignRulePackToObligationCommand(obligationId, "pack.tuition.late", new DateOnly(2026, 1, 1)),
            actorUserId, deviceId, CancellationToken.None);

        // Real payment recorded on time (reference contains "ON_TIME")
        await new RecordPaymentHandler(store, ruleEngine).HandleAsync(
            new RecordPaymentCommand(obligationId, 10000, "EGP", new DateOnly(2026, 9, 14), "ON_TIME"),
            actorUserId, deviceId, CancellationToken.None);

        var sim = new SimulateScenarioHandler(store, ruleEngine);

        // Scenario: delay that ON_TIME payment to after due date (so penalty can appear)
        var result = await sim.HandleAsync(
            new SimulateScenarioCommand(
                ObligationId: obligationId,
                AsOfDate: new DateOnly(2026, 9, 16),
                HorizonEndDate: new DateOnly(2026, 12, 31),
                Hypotheses: new[]
                {
                    new Hypothesis(
                        Type: HypothesisType.DelayedPayment,
                        EffectiveDate: new DateOnly(2026, 9, 16), // reversal effective date
                        PaymentReferenceContains: "ON_TIME",
                        NewEffectiveDate: new DateOnly(2026, 9, 20),
                        Reference: "Delay payment"
                    )
                }
            ),
            actorUserId, deviceId, CancellationToken.None);

        // Baseline should have zero charges (paid before due)
        Assert.True(result.Baseline.Charges.Count == 0);

        // Scenario should have charges (overdue window exists)
        Assert.True(result.Scenario.Charges.Count > result.Baseline.Charges.Count);

        // Cleanup
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
