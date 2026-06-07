using System.IO;
using DebtManager.Domain.Events;
using Microsoft.Data.Sqlite;

namespace DebtManager.Infrastructure.Vault;

/// <summary>
/// A lightweight event store for the global registry (vault-level events only).
/// Uses an UNENCRYPTED SQLite DB at global.db (contains no financial data).
/// Same append-only envelope pattern as the per-vault event store.
/// </summary>
public sealed class GlobalEventStore
{
    private readonly string _dbPath;

    public GlobalEventStore(string? dbPath = null)
    {
        _dbPath = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DebtManager", "global.db");

        var dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public string DbPath => _dbPath;

    public async Task AppendAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await InitSchemaAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO global_events(
                event_id, stream_id, event_type, occurred_at, effective_date,
                actor_user_id, device_id, correlation_id, causation_event_id,
                payload_schema_version, payload_json
            )
            VALUES (
                $event_id, $stream_id, $event_type, $occurred_at, $effective_date,
                $actor_user_id, $device_id, $correlation_id, $causation_event_id,
                $payload_schema_version, $payload_json
            )
            ON CONFLICT(event_id) DO NOTHING;
        """;

        cmd.Parameters.AddWithValue("$event_id", envelope.EventId.Value.ToString());
        cmd.Parameters.AddWithValue("$stream_id", envelope.StreamId.Value.ToString());
        cmd.Parameters.AddWithValue("$event_type", envelope.EventType);
        cmd.Parameters.AddWithValue("$occurred_at", envelope.OccurredAt.ToString("O"));
        cmd.Parameters.AddWithValue("$effective_date", envelope.EffectiveDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$actor_user_id", envelope.ActorUserId.ToString());
        cmd.Parameters.AddWithValue("$device_id", envelope.DeviceId.ToString());
        cmd.Parameters.AddWithValue("$correlation_id", envelope.CorrelationId.ToString());
        cmd.Parameters.AddWithValue("$causation_event_id", (object?)envelope.CausationEventId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload_schema_version", envelope.PayloadSchemaVersion);
        cmd.Parameters.AddWithValue("$payload_json", envelope.PayloadJson);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadAllAsync(CancellationToken ct)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await InitSchemaAsync(conn, ct);

        var list = new List<EventEnvelope>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT event_id, stream_id, event_type, occurred_at, effective_date,
                   actor_user_id, device_id, correlation_id, causation_event_id,
                   payload_schema_version, payload_json
            FROM global_events
            ORDER BY occurred_at ASC;
        """;

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var causationStr = r["causation_event_id"]?.ToString();
            list.Add(new EventEnvelope(
                new EventId(Guid.Parse(r["event_id"].ToString()!)),
                new StreamId(Guid.Parse(r["stream_id"].ToString()!)),
                r["event_type"].ToString()!,
                DateTimeOffset.Parse(r["occurred_at"].ToString()!),
                DateOnly.Parse(r["effective_date"].ToString()!),
                Guid.Parse(r["actor_user_id"].ToString()!),
                Guid.Parse(r["device_id"].ToString()!),
                Guid.Parse(r["correlation_id"].ToString()!),
                string.IsNullOrWhiteSpace(causationStr) ? null : Guid.Parse(causationStr),
                Convert.ToInt32(r["payload_schema_version"]),
                r["payload_json"].ToString()!
            ));
        }

        return list.AsReadOnly();
    }

    private SqliteConnection CreateConnection()
    {
        return new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
    }

    private static async Task InitSchemaAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;

            CREATE TABLE IF NOT EXISTS global_events (
                event_id            TEXT PRIMARY KEY,
                stream_id           TEXT NOT NULL,
                event_type          TEXT NOT NULL,
                occurred_at         TEXT NOT NULL,
                effective_date      TEXT NOT NULL,
                actor_user_id       TEXT NOT NULL,
                device_id           TEXT NOT NULL,
                correlation_id      TEXT NOT NULL,
                causation_event_id  TEXT NULL,
                payload_schema_version INTEGER NOT NULL,
                payload_json        TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_global_events_type
                ON global_events(event_type);
        """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
