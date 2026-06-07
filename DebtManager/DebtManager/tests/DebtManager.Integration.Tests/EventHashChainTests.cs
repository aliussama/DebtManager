using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DebtManager.Integration.Tests;

public class EventHashChainTests
{
    [Fact]
    public async Task HashChain_Verifies_AndDetectsTampering()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"debtmanager_hash_{Guid.NewGuid()}.db");
        var factory = new SqliteConnectionFactory(dbPath, new TestKeyStore());
        var store = new SqliteEventStore(factory);

        var streamId = new StreamId(Guid.NewGuid());
        var actor = Guid.NewGuid();
        var device = Guid.NewGuid();

        // append 3 envelopes
        for (int i = 0; i < 3; i++)
        {
            var env = new EventEnvelope(
                new EventId(Guid.NewGuid()),
                streamId,
                "TestEvent",
                DateTimeOffset.UtcNow.AddSeconds(i),
                new DateOnly(2026, 1, 1),
                actor,
                device,
                CorrelationId: Guid.NewGuid(),
                CausationEventId: null,
                PayloadSchemaVersion: 1,
                PayloadJson: $$"""{"i":{{i}}}"""
            );

            await store.AppendAsync(env, CancellationToken.None);
        }

        // should verify
        await store.VerifyStreamAsync(streamId, CancellationToken.None);

        // tamper payload_json of first event
        await using (var conn = factory.Create())
        {
            await conn.OpenAsync();

            // pick first event_id in this stream
            string firstEventId;
            await using (var pick = conn.CreateCommand())
            {
                pick.CommandText = """
        SELECT event_id FROM events
        WHERE stream_id = $sid
        ORDER BY occurred_at ASC
        LIMIT 1;
    """;
                pick.Parameters.AddWithValue("$sid", streamId.Value.ToString());

                firstEventId = (await pick.ExecuteScalarAsync())?.ToString()
                               ?? throw new InvalidOperationException("No event found to tamper.");
            }

            // tamper that exact row
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
        UPDATE events
        SET payload_json = '{"tampered":true}'
        WHERE event_id = $eid;
    """;
                cmd.Parameters.AddWithValue("$eid", firstEventId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // must fail
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.VerifyStreamAsync(streamId, CancellationToken.None));

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
