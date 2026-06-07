using DebtManager.Domain.Events;
using Microsoft.Data.Sqlite;

namespace DebtManager.Infrastructure.Persistence;

public sealed class SqliteEventStore : IEventStore, DebtManager.Infrastructure.Sync.ISyncStore
{
    private readonly SqliteConnectionFactory _factory;
    public SqliteEventStore(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }
    public async Task AppendAsync(EventEnvelope envelope, CancellationToken ct)
    {
        await using var conn = _factory.Create(); // SqliteConnection
        await conn.OpenAsync(ct);

        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

        // 1) Read last hash for stream inside same tx/conn
        string? prevHash = null;
        await using (var lastCmd = conn.CreateCommand())
        {
            lastCmd.Transaction = tx;
            lastCmd.CommandText = """
            SELECT hash FROM events
            WHERE stream_id = $stream_id
            ORDER BY occurred_at DESC
            LIMIT 1;
        """;
            lastCmd.Parameters.AddWithValue("$stream_id", envelope.StreamId.Value.ToString());

            var obj = await lastCmd.ExecuteScalarAsync(ct);
            prevHash = obj?.ToString();
        }

        var hash = EventHashing.ComputeHashHex(prevHash, envelope);

        // 2) Insert into events
        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
        INSERT INTO events(
          event_id, stream_id, event_type, occurred_at, effective_date,
          actor_user_id, device_id, correlation_id, causation_event_id,
          payload_schema_version, payload_json,
          prev_hash, hash
        )
        VALUES (
          $event_id, $stream_id, $event_type, $occurred_at, $effective_date,
          $actor_user_id, $device_id, $correlation_id, $causation_event_id,
          $payload_schema_version, $payload_json,
          $prev_hash, $hash
        );
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
            cmd.Parameters.AddWithValue("$prev_hash", (object?)prevHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", hash);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        // 3) Insert into outbox (idempotent)
        await using (var outbox = conn.CreateCommand())
        {
            outbox.Transaction = tx;
            outbox.CommandText = """
        INSERT INTO sync_outbox(
          event_id, stream_id, occurred_at, effective_date, event_type,
          payload_schema_version, payload_json, prev_hash, hash,
          origin_device_id, created_at
        )
        VALUES (
          $event_id, $stream_id, $occurred_at, $effective_date, $event_type,
          $payload_schema_version, $payload_json, $prev_hash, $hash,
          $origin_device_id, $created_at
        )
        ON CONFLICT(event_id) DO NOTHING;
        """;

            outbox.Parameters.AddWithValue("$event_id", envelope.EventId.Value.ToString());
            outbox.Parameters.AddWithValue("$stream_id", envelope.StreamId.Value.ToString());
            outbox.Parameters.AddWithValue("$occurred_at", envelope.OccurredAt.ToString("O"));
            outbox.Parameters.AddWithValue("$effective_date", envelope.EffectiveDate.ToString("yyyy-MM-dd"));
            outbox.Parameters.AddWithValue("$event_type", envelope.EventType);
            outbox.Parameters.AddWithValue("$payload_schema_version", envelope.PayloadSchemaVersion);
            outbox.Parameters.AddWithValue("$payload_json", envelope.PayloadJson);
            outbox.Parameters.AddWithValue("$prev_hash", (object?)prevHash ?? DBNull.Value);
            outbox.Parameters.AddWithValue("$hash", hash);
            outbox.Parameters.AddWithValue("$origin_device_id", envelope.DeviceId.ToString());
            outbox.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));

            await outbox.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }
    public async Task VerifyStreamAsync(StreamId streamId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        var rows = new List<(EventEnvelope Env, string? PrevHash, string Hash)>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
            SELECT event_id, stream_id, event_type, occurred_at, effective_date,
                   actor_user_id, device_id, correlation_id, causation_event_id,
                   payload_schema_version, payload_json, prev_hash, hash
            FROM events
            WHERE stream_id = $stream_id
            ORDER BY occurred_at ASC;
        """;
            cmd.Parameters.AddWithValue("$stream_id", streamId.Value.ToString());

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var env = ReadEnvelope(r); // your existing helper
                var prev = r["prev_hash"]?.ToString();
                var hash = r["hash"]?.ToString() ?? "";

                rows.Add((env, string.IsNullOrWhiteSpace(prev) ? null : prev, hash));
            }
        }

        string? expectedPrev = null;

        foreach (var (env, prev, hash) in rows)
        {
            // prev must match what we computed so far
            var prevToCheck = expectedPrev;
            if (!Equals(prev, prevToCheck))
                throw new InvalidOperationException("Event hash chain broken: prev_hash mismatch.");

            var expectedHash = EventHashing.ComputeHashHex(prevToCheck, env);

            if (!EventHashing.SlowEqualsHex(hash, expectedHash))
                throw new InvalidOperationException("Event hash chain broken: hash mismatch.");

            expectedPrev = hash;
        }
    }
    public async Task<IReadOnlyList<EventEnvelope>> ReadOutboxUnsentAsync(int max, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        var list = new List<EventEnvelope>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
      SELECT event_id, stream_id, event_type, occurred_at, effective_date,
             origin_device_id as device_id,
             origin_device_id as correlation_id, -- placeholder (we won’t use this)
             NULL as causation_event_id,
             payload_schema_version, payload_json,
             prev_hash, hash
      FROM sync_outbox
      WHERE sent_at IS NULL
      ORDER BY created_at ASC
      LIMIT $max;
    """;
        cmd.Parameters.AddWithValue("$max", max);

        // We can’t reuse ReadEnvelope directly because column names differ.
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var env = new EventEnvelope(
                new EventId(Guid.Parse(r["event_id"].ToString()!)),
                new StreamId(Guid.Parse(r["stream_id"].ToString()!)),
                r["event_type"].ToString()!,
                DateTimeOffset.Parse(r["occurred_at"].ToString()!),
                DateOnly.Parse(r["effective_date"].ToString()!),
                ActorUserId: Guid.Empty,
                DeviceId: Guid.Parse(r["device_id"].ToString()!),
                CorrelationId: Guid.Empty,
                CausationEventId: null,
                PayloadSchemaVersion: Convert.ToInt32(r["payload_schema_version"]),
                PayloadJson: r["payload_json"].ToString()!
            );

            list.Add(env);
        }

        return list.AsReadOnly();
    }
    public async Task<IReadOnlyList<EventEnvelope>> ReadStreamAsync(
        StreamId streamId,
        DateOnly? upTo = null,
        CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();

        cmd.CommandText = upTo is null
            ? """
          SELECT * FROM events
          WHERE stream_id = $stream_id
          ORDER BY effective_date ASC, occurred_at ASC;
          """
            : """
          SELECT * FROM events
          WHERE stream_id = $stream_id AND effective_date <= $up_to
          ORDER BY effective_date ASC, occurred_at ASC;
          """;

        cmd.Parameters.AddWithValue("$stream_id", streamId.Value.ToString());

        if (upTo is not null)
            cmd.Parameters.AddWithValue("$up_to", upTo.Value.ToString("yyyy-MM-dd"));

        var list = new List<EventEnvelope>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(ReadEnvelope((Microsoft.Data.Sqlite.SqliteDataReader)reader));

        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<EventEnvelope>> ReadAllAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
          SELECT * FROM events
          WHERE occurred_at >= $since
          ORDER BY occurred_at ASC;
        """;
        cmd.Parameters.AddWithValue("$since", since.ToString("O"));

        var list = new List<EventEnvelope>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(ReadEnvelope(reader));
        }

        return list.AsReadOnly();
    }

    private static EventEnvelope ReadEnvelope(SqliteDataReader reader)
    {
        var eventId = new EventId(Guid.Parse(reader["event_id"].ToString()!));
        var streamId = new StreamId(Guid.Parse(reader["stream_id"].ToString()!));

        var occurredAt = DateTimeOffset.Parse(reader["occurred_at"].ToString()!);
        var effectiveDate = DateOnly.Parse(reader["effective_date"].ToString()!);

        var actorUserId = Guid.Parse(reader["actor_user_id"].ToString()!);
        var deviceId = Guid.Parse(reader["device_id"].ToString()!);
        var correlationId = Guid.Parse(reader["correlation_id"].ToString()!);

        var causationStr = reader["causation_event_id"]?.ToString();
        Guid? causation = string.IsNullOrWhiteSpace(causationStr) ? null : Guid.Parse(causationStr);

        var payloadSchemaVersion = Convert.ToInt32(reader["payload_schema_version"]);
        var payloadJson = reader["payload_json"].ToString()!;

        var eventType = reader["event_type"].ToString()!;

        return new EventEnvelope(
            eventId,
            streamId,
            eventType,
            occurredAt,
            effectiveDate,
            actorUserId,
            deviceId,
            correlationId,
            causation,
            payloadSchemaVersion,
            payloadJson
        );
    }
    public async Task<IReadOnlyList<DebtManager.Infrastructure.Sync.OutboxItem>> ReadOutboxBatchAsync(int max, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        var list = new List<DebtManager.Infrastructure.Sync.OutboxItem>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT event_id, stream_id, event_type, occurred_at, effective_date,
               payload_schema_version, payload_json, prev_hash, hash,
               origin_device_id, attempts
        FROM sync_outbox
        WHERE sent_at IS NULL
        ORDER BY created_at ASC
        LIMIT $max;
    """;
        cmd.Parameters.AddWithValue("$max", max);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new DebtManager.Infrastructure.Sync.OutboxItem(
                EventId: Guid.Parse(r["event_id"].ToString()!),
                StreamId: Guid.Parse(r["stream_id"].ToString()!),
                EventType: r["event_type"].ToString()!,
                OccurredAt: DateTimeOffset.Parse(r["occurred_at"].ToString()!),
                EffectiveDate: DateOnly.Parse(r["effective_date"].ToString()!),
                PayloadSchemaVersion: Convert.ToInt32(r["payload_schema_version"]),
                PayloadJson: r["payload_json"].ToString()!,
                PrevHash: r["prev_hash"]?.ToString(),
                Hash: r["hash"]?.ToString() ?? "",
                OriginDeviceId: Guid.Parse(r["origin_device_id"].ToString()!),
                Attempts: Convert.ToInt32(r["attempts"])
            ));
        }

        return list.AsReadOnly();
    }
    public async Task MarkOutboxAttemptFailedAsync(Guid eventId, string error, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        UPDATE sync_outbox
        SET attempts = attempts + 1,
            last_error = $err
        WHERE event_id = $eid;
    """;
        cmd.Parameters.AddWithValue("$eid", eventId.ToString());
        cmd.Parameters.AddWithValue("$err", error);

        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task MarkOutboxSentAsync(IEnumerable<Guid> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.Select(x => x.ToString()).ToList();
        if (ids.Count == 0) return;

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct);

        foreach (var id in ids)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
            UPDATE sync_outbox
            SET sent_at = $sent_at
            WHERE event_id = $eid;
        """;
            cmd.Parameters.AddWithValue("$eid", id);
            cmd.Parameters.AddWithValue("$sent_at", DateTimeOffset.UtcNow.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }
    public async Task ApplyRemoteAsync(
    IReadOnlyList<EventEnvelope> remoteEnvelopes,
    Guid originDeviceId,
    CancellationToken ct = default)
    {
        if (remoteEnvelopes.Count == 0) return;

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(ct);

        // ensure deterministic order for chain verification and inserts
        var ordered = remoteEnvelopes
            .OrderBy(e => e.StreamId.Value)
            .ThenBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ThenBy(e => e.EventId.Value)
            .ToList();

        foreach (var env in ordered)
        {
            // 1) idempotency check: if already in inbox, skip
            if (await InboxHasAsync(conn, tx, env.EventId.Value, ct))
                continue;

            // 2) idempotent insert into events (DO NOTHING on conflict)
            await InsertEventIfMissingAsync(conn, tx, env, ct);

            // 3) record in inbox
            await InsertInboxAsync(conn, tx, env.EventId.Value, originDeviceId, ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task<bool> InboxHasAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        Guid eventId,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT 1 FROM sync_inbox WHERE event_id = $eid LIMIT 1;";
        cmd.Parameters.AddWithValue("$eid", eventId.ToString());

        var r = await cmd.ExecuteScalarAsync(ct);
        return r is not null;
    }

    private static async Task InsertInboxAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        Guid eventId,
        Guid originDeviceId,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        INSERT INTO sync_inbox(event_id, origin_device_id, received_at, applied_at)
        VALUES ($eid, $odid, $recv, $applied)
        ON CONFLICT(event_id) DO NOTHING;
    """;
        cmd.Parameters.AddWithValue("$eid", eventId.ToString());
        cmd.Parameters.AddWithValue("$odid", originDeviceId.ToString());
        cmd.Parameters.AddWithValue("$recv", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$applied", DateTimeOffset.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertEventIfMissingAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        EventEnvelope env,
        CancellationToken ct)
    {
        // get last hash for THIS stream from events table
        string? prevHash = null;

        await using (var lastCmd = conn.CreateCommand())
        {
            lastCmd.Transaction = tx;
            lastCmd.CommandText = """
            SELECT hash FROM events
            WHERE stream_id = $sid
            ORDER BY occurred_at DESC
            LIMIT 1;
        """;
            lastCmd.Parameters.AddWithValue("$sid", env.StreamId.Value.ToString());

            var obj = await lastCmd.ExecuteScalarAsync(ct);
            prevHash = obj?.ToString();
        }

        // compute expected hash using our chain rule
        var computedHash = EventHashing.ComputeHashHex(prevHash, env);

        // Remote must match computed hash chain
        // If remote env is missing hashes, we still accept but store computed.
        // If remote provides hashes, we enforce match.
        // (For v1, we assume remote didn't send hash; later we will.)
        var storePrev = prevHash;
        var storeHash = computedHash;

        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
        INSERT INTO events(
          event_id, stream_id, event_type, occurred_at, effective_date,
          actor_user_id, device_id, correlation_id, causation_event_id,
          payload_schema_version, payload_json,
          prev_hash, hash
        )
        VALUES (
          $event_id, $stream_id, $event_type, $occurred_at, $effective_date,
          $actor_user_id, $device_id, $correlation_id, $causation_event_id,
          $payload_schema_version, $payload_json,
          $prev_hash, $hash
        )
        ON CONFLICT(event_id) DO NOTHING;
    """;

        cmd.Parameters.AddWithValue("$event_id", env.EventId.Value.ToString());
        cmd.Parameters.AddWithValue("$stream_id", env.StreamId.Value.ToString());
        cmd.Parameters.AddWithValue("$event_type", env.EventType);
        cmd.Parameters.AddWithValue("$occurred_at", env.OccurredAt.ToString("O"));
        cmd.Parameters.AddWithValue("$effective_date", env.EffectiveDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$actor_user_id", env.ActorUserId.ToString());
        cmd.Parameters.AddWithValue("$device_id", env.DeviceId.ToString());
        cmd.Parameters.AddWithValue("$correlation_id", env.CorrelationId.ToString());
        cmd.Parameters.AddWithValue("$causation_event_id", (object?)env.CausationEventId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload_schema_version", env.PayloadSchemaVersion);
        cmd.Parameters.AddWithValue("$payload_json", env.PayloadJson);
        cmd.Parameters.AddWithValue("$prev_hash", (object?)storePrev ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", storeHash);

        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<string?> GetSyncCursorAsync(string vaultId, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM sync_state WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", $"cursor:{vaultId}");

        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj?.ToString();
    }

    public async Task SetSyncCursorAsync(string vaultId, string cursor, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO sync_state(key, value)
        VALUES($k, $v)
        ON CONFLICT(key) DO UPDATE SET value = excluded.value;
    """;
        cmd.Parameters.AddWithValue("$k", $"cursor:{vaultId}");
        cmd.Parameters.AddWithValue("$v", cursor);

        await cmd.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<EventEnvelope>> ReadEnvelopesByEventIdsAsync(IEnumerable<Guid> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.Distinct().Select(x => x.ToString()).ToList();

        if (ids.Count == 0) return Array.Empty<EventEnvelope>();

        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        // Build parameterized IN (...)
        var paramNames = ids.Select((_, i) => $"$id{i}").ToList();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
        SELECT * FROM events
        WHERE event_id IN ({string.Join(", ", paramNames)});
    """;

        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

        var list = new List<EventEnvelope>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(ReadEnvelope((Microsoft.Data.Sqlite.SqliteDataReader)r));

        return list.AsReadOnly();
    }
}
