using DebtManager.Domain.Events;
using DebtManager.Domain.Rules;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;
using Xunit;

namespace DebtManager.Integration.Tests;

public class JsonRulePackPenaltyTests
{
    [Fact]
    public async Task JsonPack_PenaltyRule_Fires_WhenOverdueBeyondGrace()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_jsonpack_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store); // assumes your existing resolver
        var engine = new SqliteRuleEngine(repo, resolver);

        var obligationId = Guid.NewGuid();

        var json = """
{
  "rules": [
    {
      "key": "penalty.after_grace",
      "when": {
        "all": [
          { "fact": "days_overdue", "op": ">", "value": 10 },
          { "fact": "outstanding_amount", "op": ">", "value": 0 }
        ]
      },
      "effect": {
        "add_charge": {
          "amount": 50,
          "label": "Late penalty (>10d grace)",
          "chargeType": "penalty"
        }
      }
    }
  ]
}
""";

        var packId = "test_penalty_pack";

        // 1) ensure pack exists
        await repo.UpsertPackAsync(packId, "Test Penalty Pack", "Integration test pack", CancellationToken.None);

        // 2) add an ACTIVE version that covers asOfDate
        var versionRow = new RulePackVersionRow(
            RulePackVersionId: Guid.NewGuid(),
            RulePackId: packId,
            VersionLabel: "Penalty v1",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            EffectiveTo: null,
            Status: "active",
            RulesJson: json
        );

        await repo.AddVersionAsync(versionRow, CancellationToken.None);

        var assign = new RulePackAssignedToObligation(
    ObligationId: obligationId,
    RulePackId: packId,
    EffectiveDate: new DateOnly(2026, 1, 1)
);

        await store.AppendAsync(
            new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(obligationId),
                nameof(RulePackAssignedToObligation),
                DateTimeOffset.UtcNow,
                assign.EffectiveDate,
                Guid.NewGuid(),              // ActorUserId
                Guid.NewGuid(),              // DeviceId
                Guid.NewGuid(),              // CorrelationId
                null,                        // CausationEventId
                1,                           // PayloadSchemaVersion
                JsonSerializer.Serialize(assign, DebtManager.Domain.ValueObjects.DomainJson.Options)
            ),
            CancellationToken.None);

        // 4) Evaluate
        var ctx = new RuleEvaluationContext(
            EvaluationDate: new DateOnly(2026, 1, 22),
            ObligationId: obligationId,
            InstallmentKey: Guid.NewGuid(),
            CurrencyCode: "EGP",
            Facts: new Dictionary<string, object>
            {
                { "days_overdue", 11 },
                { "outstanding_amount", 1000m },
                { "outstanding_currency", "EGP" }
            }
        );

        var (effects, trace) = await engine.EvaluateAsync(ctx, CancellationToken.None);

        Assert.Single(effects);
        Assert.Contains(trace.FiredRuleKeys, k => k == "penalty.after_grace");

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
