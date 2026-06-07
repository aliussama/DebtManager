using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Sync;
using DebtManager.Sync.Transport;

namespace DebtManager.Integration.Tests;

public class SyncConvergenceInMemoryTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task TwoDevices_Converge_UsingInMemoryTransport()
    {
        var vaultId = "family1";

        var db1 = Path.Combine(Path.GetTempPath(), $"debtmanager_dev1_{Guid.NewGuid()}.db");
        var db2 = Path.Combine(Path.GetTempPath(), $"debtmanager_dev2_{Guid.NewGuid()}.db");

        var f1 = new SqliteConnectionFactory(db1, new TestKeyStore());
        var f2 = new SqliteConnectionFactory(db2, new TestKeyStore());

        var s1 = new SqliteEventStore(f1);
        var s2 = new SqliteEventStore(f2);

        var transport = new InMemorySyncTransport();

        var engine1 = new SyncEngine(s1, transport);
        var engine2 = new SyncEngine(s2, transport);

        var dev1 = Guid.NewGuid();
        var dev2 = Guid.NewGuid();

        // Device 1 writes: obligation
        var obligationId = Guid.NewGuid();
        await s1.AppendAsync(TestEnvelopes.ObligationCreated(obligationId, "Dev1 Obligation"), CancellationToken.None);

        // Device 2 writes: income + expense (default account stream)
        await s2.AppendAsync(TestEnvelopes.IncomeRecorded(AccountId, 5000m, "Salary", new DateOnly(2026, 1, 1)), CancellationToken.None);
        await s2.AppendAsync(TestEnvelopes.ExpenseRecorded(AccountId, 2000m, "Rent", new DateOnly(2026, 1, 2)), CancellationToken.None);

        // Sync rounds (eventual consistency)
        for (int i = 0; i < 6; i++)
        {
            await engine1.SyncOnceAsync(vaultId, dev1, CancellationToken.None);
            await engine2.SyncOnceAsync(vaultId, dev2, CancellationToken.None);
        }

        // Both DBs should now contain the same set of events (by event_id)
        var all1 = await s1.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);
        var all2 = await s2.ReadAllAsync(DateTimeOffset.MinValue, CancellationToken.None);

        var ids1 = all1.Select(e => e.EventId.Value).OrderBy(x => x).ToList();
        var ids2 = all2.Select(e => e.EventId.Value).OrderBy(x => x).ToList();

        Assert.Equal(ids1, ids2);

        // Verify hash chain for each stream in both DBs
        var streams = all1.Select(e => e.StreamId).Distinct().ToList();
        foreach (var sid in streams)
        {
            await s1.VerifyStreamAsync(sid, CancellationToken.None);
            await s2.VerifyStreamAsync(sid, CancellationToken.None);
        }

        await Cleanup(db1);
        await Cleanup(db2);
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

    private static class TestEnvelopes
    {
        public static EventEnvelope ObligationCreated(Guid obligationId, string name)
        {
            var oc = new DebtManager.Domain.Events.ObligationCreated(
                ObligationId: obligationId,
                Name: name,
                ObligationType: "Test",
                Principal: new DebtManager.Domain.ValueObjects.Money(1000m, DebtManager.Domain.ValueObjects.Currency.EGP),
                StartDate: new DateOnly(2026, 1, 1),
                CurrencyCode: "EGP"
            );

            return new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(obligationId),
                nameof(DebtManager.Domain.Events.ObligationCreated),
                DateTimeOffset.UtcNow,
                oc.EffectiveDate,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                1,
                System.Text.Json.JsonSerializer.Serialize(oc, DebtManager.Domain.ValueObjects.DomainJson.Options)
            );
        }

        public static EventEnvelope IncomeRecorded(Guid accountId, decimal amount, string source, DateOnly date)
        {
            var ev = new DebtManager.Domain.Events.IncomeRecorded(
                AccountId: accountId,
                Amount: new DebtManager.Domain.ValueObjects.Money(amount, DebtManager.Domain.ValueObjects.Currency.EGP),
                EffectiveDate: date,
                Source: source
            );

            return new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(accountId),
                nameof(DebtManager.Domain.Events.IncomeRecorded),
                DateTimeOffset.UtcNow,
                date,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                1,
                System.Text.Json.JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
            );
        }

        public static EventEnvelope ExpenseRecorded(Guid accountId, decimal amount, string category, DateOnly date)
        {
            var ev = new DebtManager.Domain.Events.ExpenseRecorded(
                AccountId: accountId,
                Amount: new DebtManager.Domain.ValueObjects.Money(amount, DebtManager.Domain.ValueObjects.Currency.EGP),
                EffectiveDate: date,
                Category: category,
                Notes: string.Empty
            );

            return new EventEnvelope(
                new EventId(Guid.NewGuid()),
                new StreamId(accountId),
                nameof(DebtManager.Domain.Events.ExpenseRecorded),
                DateTimeOffset.UtcNow,
                date,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                1,
                System.Text.Json.JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
            );
        }
    }
}
