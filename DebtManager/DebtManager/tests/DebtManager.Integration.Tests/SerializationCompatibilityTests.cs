using DebtManager.Application.UseCases;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using System.Text.Json;

namespace DebtManager.Integration.Tests;

public class SerializationCompatibilityTests
{
    [Fact]
    public async Task StoredPaymentMade_WrappedPayload_DeserializesAndKeepsCurrency()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_sercompat_{Guid.NewGuid()}.db");
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
                Name: "Compat Test",
                ObligationType: "Loan",
                PrincipalAmount: 10000m,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 1, 1)
            ),
            actor, device, CancellationToken.None);

        // 2) Define schedule (one installment)
        var spec = new DebtManager.Domain.Scheduling.FixedDatesScheduleSpec(
            "EGP",
            new[] { new DebtManager.Domain.Scheduling.FixedDateItem(new DateOnly(2026, 1, 10), 10000m) },
            new[] { "compat" }
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

        // 3) Record payment (this writes StoredPaymentMade wrapper)
        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var engine = new SqliteRuleEngine(repo, resolver);

        var record = new RecordPaymentHandler(store, engine);
        await record.HandleAsync(
            new RecordPaymentCommand(
                ObligationId: obligationId,
                Amount: 10000m,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 1, 9),
                Reference: "WRAPPED"
            ),
            actor, device, CancellationToken.None);

        // 4) Snapshot should replay without Money.Currency becoming null
        var snapshot = new GetFinancialSnapshotHandler(store, engine);
        var state = await snapshot.HandleAsync(obligationId, new DateOnly(2026, 1, 15), CancellationToken.None);

        Assert.Equal("EGP", state.TotalPayments.Currency.Code);
        Assert.Equal(10000m, state.TotalPayments.Amount);

        // Cleanup
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
