using System.Text.Json;
using DebtManager.Application.UseCases;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class PortfolioTimelineChargesTests
{
    [Fact]
    public async Task Timeline_IncludesCharges_FromSnapshots()
    {
        // Arrange: local temp DB
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_timeline_charges_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // 1) Create obligation
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(
            new CreateObligationCommand(
                ObligationId: obligationId,
                Name: "Test Obligation",
                ObligationType: "Loan",
                PrincipalAmount: 1000,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 1, 1)
            ),
            actorUserId, deviceId, CancellationToken.None);

        // 2) Define schedule with at least ONE installment due before AsOfDate
        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 1, 2), 1000),
            },
            new[] { "test" }
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
            actorUserId, deviceId, CancellationToken.None);

        // 3) Snapshot handler using fake engine that always emits one charge effect
        // (Rules get evaluated because installments exist)
        var ruleEngine = new FakeRuleEngineOneCharge();
        var snapshots = new GetFinancialSnapshotHandler(store, ruleEngine);

        var snap = await snapshots.HandleAsync(obligationId, new DateOnly(2026, 1, 5), CancellationToken.None);

        foreach (var a in snap.Audit)
            System.Diagnostics.Debug.WriteLine($"AUDIT: {a.Category} | {a.Message}");

        Assert.True(snap.Charges.Count > 0);      // prove fake rule engine produced charges

        // 4) Build portfolio timeline (should include derived charges)
        var handler = new GetPortfolioTimelineHandler(store, snapshots);
        var result = await handler.HandleAsync(new DateOnly(2026, 1, 5), CancellationToken.None);

        // 5) Expect a charge delta of -50 to appear
        Assert.Contains(result.Items, x => x.Type == "Charge" && x.Amount.Amount == -50m);

        // Cleanup (delete wal/shm too)
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
