using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using DebtManager.Domain.ValueObjects;
using System.Text.Json;

namespace DebtManager.Integration.Tests;

public class FuzzSchedulePaymentTests
{
    [Fact]
    public async Task Fuzz_SchedulesAndPayments_PreserveInstallmentInvariants()
    {
        const int seed = 1337;
        var rng = new Random(seed);

        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_fuzz_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // Create obligation
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(
            new CreateObligationCommand(
                ObligationId: obligationId,
                Name: "Fuzz Obligation",
                ObligationType: "Test",
                PrincipalAmount: 100000,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 1, 1)
            ),
            actor, device, CancellationToken.None);

        // Random fixed-dates schedule: 12 installments in 2026 (irregular dates)
        var items = new List<DebtManager.Domain.Scheduling.FixedDateItem>();
        for (int i = 0; i < 12; i++)
        {
            var month = i + 1;
            var day = rng.Next(1, 28);
            var due = new DateOnly(2026, month, day);
            var amt = rng.Next(1000, 15000);
            items.Add(new DebtManager.Domain.Scheduling.FixedDateItem(due, amt));
        }

        var spec = new DebtManager.Domain.Scheduling.FixedDatesScheduleSpec(
            "EGP",
            items.ToArray(),
            new[] { "fuzz" }
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

        // Random payments: 30 payments across 2026 (some before due, some after)
        var record = new RecordPaymentHandler(store, ruleEngine);
        for (int p = 0; p < 30; p++)
        {
            var month = rng.Next(1, 13);
            var day = rng.Next(1, 28);
            var date = new DateOnly(2026, month, day);
            var amt = rng.Next(500, 8000);

            await record.HandleAsync(
                new RecordPaymentCommand(
                    ObligationId: obligationId,
                    Amount: amt,
                    CurrencyCode: "EGP",
                    EffectiveDate: date,
                    Reference: $"P{p}"
                ),
                actor, device, CancellationToken.None);
        }

        // Snapshot with real rule engine (ok even if no rule packs installed; charges can be empty)
        var engine = new SqliteRuleEngine(repo, resolver);

        var snapshot = new GetFinancialSnapshotHandler(store, engine);
        var state = await snapshot.HandleAsync(obligationId, new DateOnly(2026, 12, 31), CancellationToken.None);

        // Invariants: no negative outstanding; Paid+Outstanding==Expected
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