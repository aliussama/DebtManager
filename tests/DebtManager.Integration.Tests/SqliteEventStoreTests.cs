using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;

namespace DebtManager.Integration.Tests;

public class SqliteEventStoreTests
{
    [Fact]
    public async Task AppendAndReadStream_Works()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_test_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var streamId = new StreamId(Guid.NewGuid());
        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            "TestEvent",
            DateTimeOffset.UtcNow,
            new DateOnly(2026, 1, 1),
            ActorUserId: Guid.NewGuid(),
            DeviceId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: """{"hello":"world"}"""
        );

        await store.AppendAsync(envelope, CancellationToken.None);
        var events = await store.ReadStreamAsync(streamId, null, CancellationToken.None);

        Assert.Single(events);
        Assert.Equal(envelope.EventId.Value, events[0].EventId.Value);

        // Cleanup (Windows can keep SQLite file locked briefly)
        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (File.Exists(dbPath))
                    File.Delete(dbPath);
                break;
            }
            catch (IOException) when (i < 9)
            {
                await Task.Delay(50);
            }
        }

    }
}
