using DebtManager.Domain.Projections.Snapshots;
using Microsoft.Data.Sqlite;

namespace DebtManager.Infrastructure.Persistence;

public sealed class SqliteProjectionSnapshotStore : IProjectionSnapshotStore
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteProjectionSnapshotStore(SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task SaveAsync(ProjectionSnapshotEnvelope snapshot, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO projection_snapshots(
                snapshot_id, projection_name, as_of_date, created_at,
                device_id, schema_version, payload_json,
                last_event_id, last_occurred_at
            )
            VALUES (
                $snapshot_id, $projection_name, $as_of_date, $created_at,
                $device_id, $schema_version, $payload_json,
                $last_event_id, $last_occurred_at
            );
        """;

        cmd.Parameters.AddWithValue("$snapshot_id", snapshot.SnapshotId.Value.ToString());
        cmd.Parameters.AddWithValue("$projection_name", snapshot.ProjectionName);
        cmd.Parameters.AddWithValue("$as_of_date", snapshot.AsOfDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$created_at", snapshot.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$device_id", snapshot.DeviceId.ToString());
        cmd.Parameters.AddWithValue("$schema_version", snapshot.SchemaVersion);
        cmd.Parameters.AddWithValue("$payload_json", snapshot.PayloadJson);
        cmd.Parameters.AddWithValue("$last_event_id", snapshot.LastEventId.ToString());
        cmd.Parameters.AddWithValue("$last_occurred_at", snapshot.LastOccurredAt.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProjectionSnapshotEnvelope?> LoadLatestAsync(
        string projectionName, DateOnly asOfDate, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT snapshot_id, projection_name, as_of_date, created_at,
                   device_id, schema_version, payload_json,
                   last_event_id, last_occurred_at
            FROM projection_snapshots
            WHERE projection_name = $name AND as_of_date <= $as_of_date
            ORDER BY as_of_date DESC, created_at DESC
            LIMIT 1;
        """;

        cmd.Parameters.AddWithValue("$name", projectionName);
        cmd.Parameters.AddWithValue("$as_of_date", asOfDate.ToString("yyyy-MM-dd"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new ProjectionSnapshotEnvelope(
            new SnapshotId(Guid.Parse(reader["snapshot_id"].ToString()!)),
            reader["projection_name"].ToString()!,
            DateOnly.Parse(reader["as_of_date"].ToString()!),
            DateTimeOffset.Parse(reader["created_at"].ToString()!),
            Guid.Parse(reader["device_id"].ToString()!),
            Convert.ToInt32(reader["schema_version"]),
            reader["payload_json"].ToString()!,
            Guid.Parse(reader["last_event_id"].ToString()!),
            DateTimeOffset.Parse(reader["last_occurred_at"].ToString()!)
        );
    }

    public async Task PruneAsync(string projectionName, int keepLastN, CancellationToken ct)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);
        await new SchemaInitializer().InitializeAsync(conn, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM projection_snapshots
            WHERE projection_name = $name
              AND snapshot_id NOT IN (
                SELECT snapshot_id
                FROM projection_snapshots
                WHERE projection_name = $name
                ORDER BY created_at DESC
                LIMIT $keep
              );
        """;

        cmd.Parameters.AddWithValue("$name", projectionName);
        cmd.Parameters.AddWithValue("$keep", keepLastN);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
