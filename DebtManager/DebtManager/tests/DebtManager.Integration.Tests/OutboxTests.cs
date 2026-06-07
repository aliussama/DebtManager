using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class OutboxTests
{
    [Fact]
    public async Task Append_WritesToOutbox()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_outbox_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var streamId = new StreamId(Guid.NewGuid());
        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            "TestEvent",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 1, 1),
            Guid.NewGuid(),   // ActorUserId
            Guid.NewGuid(),   // DeviceId
            Guid.NewGuid(),   // CorrelationId
            null,             // CausationEventId
            1,                // PayloadSchemaVersion
            """{"x":1}"""     // PayloadJson
        );

        await store.AppendAsync(env, CancellationToken.None);

        var unsent = await store.ReadOutboxUnsentAsync(10, CancellationToken.None);

        Assert.Contains(unsent, e => e.EventId.Value == env.EventId.Value);

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
