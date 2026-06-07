using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class InboxApplyTests
{
    [Fact]
    public async Task ApplyRemote_IsIdempotent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_inbox_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var originDevice = Guid.NewGuid();
        var streamId = new StreamId(Guid.NewGuid());

        var env = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            "RemoteEvent",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 1, 1),
            Guid.NewGuid(),
            originDevice,
            Guid.NewGuid(),
            null,
            1,
            """{"remote":true}"""
        );

        // Apply same event twice
        await store.ApplyRemoteAsync(new[] { env }, originDevice, CancellationToken.None);
        await store.ApplyRemoteAsync(new[] { env }, originDevice, CancellationToken.None);

        // Read stream should contain event once
        var events = await store.ReadStreamAsync(streamId, null, CancellationToken.None);
        Assert.Single(events.Where(e => e.EventId.Value == env.EventId.Value));

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
