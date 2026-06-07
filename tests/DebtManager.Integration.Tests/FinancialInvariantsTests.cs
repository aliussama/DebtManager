using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;

namespace DebtManager.Integration.Tests;

public class FinancialInvariantsTests
{
    [Fact]
    public async Task Snapshot_InstallmentInvariants_Hold()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_invariants_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // obligation
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(
            new CreateObligationCommand(
                ObligationId: obligationId,
                Name: "Invariant Obligation",
                ObligationType: "Test",
                PrincipalAmount: 30000,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 1, 1)
            ),
            actor, device, CancellationToken.None);

        // schedule (3 fixed installments)
        var spec = new DebtManager.Domain.Scheduling.FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new DebtManager.Domain.Scheduling.FixedDateItem(new DateOnly(2026, 2, 1), 10000),
                new DebtManager.Domain.Scheduling.FixedDateItem(new DateOnly(2026, 3, 1), 10000),
                new DebtManager.Domain.Scheduling.FixedDateItem(new DateOnly(2026, 4, 1), 10000),
            },
            new[] { "invariants" }
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

        // payment: partial + over time
        var record = new RecordPaymentHandler(store, ruleEngine);
        await record.HandleAsync(
            new RecordPaymentCommand(
                ObligationId: obligationId,
                Amount: 12000,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 2, 10),
                Reference: "P1"
            ),
            actor, device, CancellationToken.None);

        // rules engine (real)

        var snapshot = new GetFinancialSnapshotHandler(store, ruleEngine);
        var state = await snapshot.HandleAsync(obligationId, new DateOnly(2026, 12, 31), CancellationToken.None);

        // Invariants per installment
        foreach (var i in state.Installments)
        {
            Assert.True(i.Outstanding.Amount >= 0m);

            var sum = i.Paid.Amount + i.Outstanding.Amount;
            Assert.Equal(i.Expected.Amount, sum);
        }

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
