using DebtManager.Application.UseCases;
using DebtManager.Domain.Allocation;
using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Rules;
using DebtManager.Domain.Rules;
using System.Text.Json;

namespace DebtManager.Integration.Tests;

public class EndToEndAppFlowTests
{
    [Fact]
    public async Task TuitionFlow_CreateSchedulePaySnapshot_Works()
    {
        // Arrange: local temp DB
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_e2e_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var actorUserId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var obligationId = Guid.NewGuid();
        var scheduleId = Guid.NewGuid();

        // 1) Create obligation (Tuition)
        var create = new CreateObligationHandler(store);
        await create.HandleAsync(
            new CreateObligationCommand(
                ObligationId: obligationId,
                Name: "Tuition",
                ObligationType: "Education",
                PrincipalAmount: 30000,
                CurrencyCode: "EGP",
                StartDate: new DateOnly(2026, 9, 1)
            ),
            actorUserId, deviceId, CancellationToken.None);

        // 2) Define schedule (fixed dates: Sep/Nov/Feb)
        var spec = new FixedDatesScheduleSpec(
            "EGP",
            new[]
            {
                new FixedDateItem(new DateOnly(2026, 9, 15), 10000),
                new FixedDateItem(new DateOnly(2026, 11, 30), 10000),
                new FixedDateItem(new DateOnly(2027, 2, 28), 10000),
            },
            new[] { "tuition", "education" }
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

        // 3) Expand expected installments (for payment allocation)
        // In v1, the RecordPaymentHandler expects expectedInstallments + existingAllocations provided by caller.
        // We build them using the same expander used by snapshot.
        var scheduleDef = new ScheduleDefinition(
            scheduleId,
            obligationId,
            "fixed_dates",
            JsonSerializer.Serialize(spec, DebtManager.Domain.ValueObjects.DomainJson.Options),
            "Africa/Cairo"
        );

        var repo = new SqliteRulePackRepository(factory);
        var resolver = new SqliteRulePackResolver(store);
        var ruleEngine = new SqliteRuleEngine(repo, resolver);

        // 4) Record payment of 12k on Sep 20, 2026 (should pay first 10k + 2k of second)
        var record = new RecordPaymentHandler(store, ruleEngine);
        await record.HandleAsync(
            new RecordPaymentCommand(
                ObligationId: obligationId,
                Amount: 12000,
                CurrencyCode: "EGP",
                EffectiveDate: new DateOnly(2026, 9, 20),
                Reference: "Payment 12k"
            ),
            actorUserId,
            deviceId,
            CancellationToken.None);

        // 5) Snapshot as of end of 2026

        var snapshot = new GetFinancialSnapshotHandler(store, ruleEngine);
        var state = await snapshot.HandleAsync(obligationId, new DateOnly(2026, 12, 31), CancellationToken.None);

        // Assert: totals
        Assert.Equal(12000m, state.TotalPayments.Amount);

        // Assert: installment states
        var ordered = state.Installments.OrderBy(x => x.DueDate).ToList();
        Assert.True(ordered.Count >= 2);

        var first = ordered[0];
        var second = ordered[1];

        Assert.Equal(new DateOnly(2026, 9, 15), first.DueDate);
        Assert.True(first.IsFullyPaid);
        Assert.Equal(10000m, first.Paid.Amount);

        Assert.Equal(new DateOnly(2026, 11, 30), second.DueDate);
        Assert.False(second.IsFullyPaid);
        Assert.Equal(2000m, second.Paid.Amount);
        Assert.Equal(8000m, second.Outstanding.Amount);

        // Cleanup (pooling disabled already; still delete wal/shm too)
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
