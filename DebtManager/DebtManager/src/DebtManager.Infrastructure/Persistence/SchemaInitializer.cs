using Microsoft.Data.Sqlite;

namespace DebtManager.Infrastructure.Persistence;

public sealed class SchemaInitializer
{
    public async Task InitializeAsync(SqliteConnection connection, CancellationToken ct)
    {
        // Pragmas
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", ct);
        await ExecuteAsync(connection, "PRAGMA foreign_keys=ON;", ct);

        // 1) Create base tables FIRST (must exist before migrations)
        var sqlEvents = """
        CREATE TABLE IF NOT EXISTS events (
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

        CREATE INDEX IF NOT EXISTS idx_events_stream_effective
            ON events(stream_id, effective_date);

        CREATE INDEX IF NOT EXISTS idx_events_occurred
            ON events(occurred_at);
        """;

        await ExecuteAsync(connection, sqlEvents, ct);

        // 2) Migration-safe schema upgrades (now events exists)
        await AddColumnIfMissingAsync(connection, "events", "prev_hash",
            "ALTER TABLE events ADD COLUMN prev_hash TEXT NULL;", ct);

        await AddColumnIfMissingAsync(connection, "events", "hash",
            "ALTER TABLE events ADD COLUMN hash TEXT NULL;", ct);

        // 3) Rule tables (safe to run anytime)
        var sqlRules = """
        CREATE TABLE IF NOT EXISTS rule_packs (
          rule_pack_id TEXT PRIMARY KEY,
          name TEXT NOT NULL,
          description TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS rule_pack_versions (
          rule_pack_version_id TEXT PRIMARY KEY,
          rule_pack_id TEXT NOT NULL,
          version_label TEXT NOT NULL,
          effective_from TEXT NOT NULL,
          effective_to TEXT NULL,
          status TEXT NOT NULL,
          rules_json TEXT NOT NULL,
          FOREIGN KEY(rule_pack_id) REFERENCES rule_packs(rule_pack_id)
        );

        CREATE INDEX IF NOT EXISTS idx_rule_versions_by_pack
          ON rule_pack_versions(rule_pack_id, effective_from);
        """;

        await ExecuteAsync(connection, sqlRules, ct);

        var sqlSync = """
        CREATE TABLE IF NOT EXISTS sync_outbox (
            outbox_id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_id TEXT NOT NULL UNIQUE,
            stream_id TEXT NOT NULL,
            occurred_at TEXT NOT NULL,
            effective_date TEXT NOT NULL,
            event_type TEXT NOT NULL,
            payload_schema_version INTEGER NOT NULL,
            payload_json TEXT NOT NULL,
            prev_hash TEXT NULL,
            hash TEXT NULL,
            origin_device_id TEXT NOT NULL,
            created_at TEXT NOT NULL,
            sent_at TEXT NULL,
            attempts INTEGER NOT NULL DEFAULT 0,
            last_error TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_outbox_unsent
            ON sync_outbox(sent_at, created_at);

        CREATE TABLE IF NOT EXISTS sync_inbox (
            inbox_id INTEGER PRIMARY KEY AUTOINCREMENT,
            event_id TEXT NOT NULL UNIQUE,
            origin_device_id TEXT NOT NULL,
            received_at TEXT NOT NULL,
            applied_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS sync_state (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

        await ExecuteAsync(connection, sqlSync, ct);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection connection, string table, string column, string ddl, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var exists = false;
        while (await r.ReadAsync(ct))
        {
            var name = r["name"]?.ToString();
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
            await ExecuteAsync(connection, ddl, ct);
    }
}

