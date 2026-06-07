using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class OutboxStateTests
{
    [Fact]
    public async Task Outbox_MarkSent_And_Attempts_Work()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_outboxstate_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(Guid.NewGuid()),
            "TestEvent",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 1, 1),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            1,
            """{"x":1}"""
        );

        await store.AppendAsync(env, CancellationToken.None);

        // batch has 1
        var batch = await store.ReadOutboxBatchAsync(10, CancellationToken.None);
        Assert.Single(batch);

        // fail attempt increments
        await store.MarkOutboxAttemptFailedAsync(env.EventId.Value, "network error", CancellationToken.None);
        var batch2 = await store.ReadOutboxBatchAsync(10, CancellationToken.None);
        Assert.Equal(1, batch2[0].Attempts);

        // mark sent removes from unsent batch
        await store.MarkOutboxSentAsync(new[] { env.EventId.Value }, CancellationToken.None);
        var batch3 = await store.ReadOutboxBatchAsync(10, CancellationToken.None);
        Assert.Empty(batch3);

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
