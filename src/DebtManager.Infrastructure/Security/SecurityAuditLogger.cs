namespace DebtManager.Infrastructure.Security;

/// <summary>
/// Security audit event types.
/// </summary>
public enum SecurityEventType
{
    // Authentication
    AuthenticationSuccess,
    AuthenticationFailure,
    ApiKeyValidated,
    ApiKeyRejected,

    // Authorization
    AccessGranted,
    AccessDenied,

    // Data Protection
    EncryptionKeyAccessed,
    EncryptionKeyRotated,
    DatabaseUnlocked,
    DatabaseLockFailed,

    // Integrity
    HashChainVerified,
    HashChainViolation,
    TamperDetected,

    // Sync
    SyncPushStarted,
    SyncPushCompleted,
    SyncPushFailed,
    SyncPullStarted,
    SyncPullCompleted,
    SyncPullFailed,

    // Sensitive Operations
    ObligationCreated,
    ObligationClosed,
    PaymentRecorded,
    ChargeWaived,
    RulePackInstalled,

    // System
    ApplicationStarted,
    ApplicationStopped,
    ConfigurationChanged
}

/// <summary>
/// Security audit log entry.
/// </summary>
public sealed record SecurityAuditEntry(
    Guid AuditId,
    DateTimeOffset Timestamp,
    SecurityEventType EventType,
    string Category,
    string Message,
    string? UserId,
    string? DeviceId,
    string? IpAddress,
    string? ResourceId,
    string? ResourceType,
    bool Success,
    Dictionary<string, string>? Metadata
);

/// <summary>
/// Interface for security audit logging.
/// </summary>
public interface ISecurityAuditLogger
{
    Task LogAsync(SecurityAuditEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityAuditEntry>> GetRecentAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<SecurityAuditEntry>> GetByTypeAsync(SecurityEventType type, DateTimeOffset since, CancellationToken ct = default);
}

/// <summary>
/// SQLite-based security audit logger.
/// </summary>
public sealed class SqliteSecurityAuditLogger : ISecurityAuditLogger
{
    private readonly Persistence.SqliteConnectionFactory _factory;

    public SqliteSecurityAuditLogger(Persistence.SqliteConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task LogAsync(SecurityAuditEntry entry, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        // Ensure audit table exists
        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = """
                CREATE TABLE IF NOT EXISTS security_audit (
                    audit_id TEXT PRIMARY KEY,
                    timestamp TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    category TEXT NOT NULL,
                    message TEXT NOT NULL,
                    user_id TEXT,
                    device_id TEXT,
                    ip_address TEXT,
                    resource_id TEXT,
                    resource_type TEXT,
                    success INTEGER NOT NULL,
                    metadata_json TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_security_audit_timestamp ON security_audit(timestamp);
                CREATE INDEX IF NOT EXISTS idx_security_audit_event_type ON security_audit(event_type);
            """;
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO security_audit (
                audit_id, timestamp, event_type, category, message,
                user_id, device_id, ip_address, resource_id, resource_type,
                success, metadata_json
            ) VALUES (
                $audit_id, $timestamp, $event_type, $category, $message,
                $user_id, $device_id, $ip_address, $resource_id, $resource_type,
                $success, $metadata_json
            );
        """;

        cmd.Parameters.AddWithValue("$audit_id", entry.AuditId.ToString());
        cmd.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToString("O"));
        cmd.Parameters.AddWithValue("$event_type", entry.EventType.ToString());
        cmd.Parameters.AddWithValue("$category", entry.Category);
        cmd.Parameters.AddWithValue("$message", entry.Message);
        cmd.Parameters.AddWithValue("$user_id", (object?)entry.UserId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$device_id", (object?)entry.DeviceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ip_address", (object?)entry.IpAddress ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$resource_id", (object?)entry.ResourceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$resource_type", (object?)entry.ResourceType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$success", entry.Success ? 1 : 0);
        cmd.Parameters.AddWithValue("$metadata_json",
            entry.Metadata != null
                ? System.Text.Json.JsonSerializer.Serialize(entry.Metadata)
                : DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SecurityAuditEntry>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM security_audit
            ORDER BY timestamp DESC
            LIMIT $count;
        """;
        cmd.Parameters.AddWithValue("$count", count);

        return await ReadEntriesAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<SecurityAuditEntry>> GetByTypeAsync(
        SecurityEventType type,
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        await using var conn = _factory.Create();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM security_audit
            WHERE event_type = $event_type AND timestamp >= $since
            ORDER BY timestamp DESC;
        """;
        cmd.Parameters.AddWithValue("$event_type", type.ToString());
        cmd.Parameters.AddWithValue("$since", since.ToString("O"));

        return await ReadEntriesAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<SecurityAuditEntry>> ReadEntriesAsync(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        CancellationToken ct)
    {
        var entries = new List<SecurityAuditEntry>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var metadataJson = reader["metadata_json"]?.ToString();
            Dictionary<string, string>? metadata = null;
            if (!string.IsNullOrEmpty(metadataJson))
            {
                metadata = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
            }

            entries.Add(new SecurityAuditEntry(
                AuditId: Guid.Parse(reader["audit_id"].ToString()!),
                Timestamp: DateTimeOffset.Parse(reader["timestamp"].ToString()!),
                EventType: Enum.Parse<SecurityEventType>(reader["event_type"].ToString()!),
                Category: reader["category"].ToString()!,
                Message: reader["message"].ToString()!,
                UserId: reader["user_id"]?.ToString(),
                DeviceId: reader["device_id"]?.ToString(),
                IpAddress: reader["ip_address"]?.ToString(),
                ResourceId: reader["resource_id"]?.ToString(),
                ResourceType: reader["resource_type"]?.ToString(),
                Success: Convert.ToInt32(reader["success"]) == 1,
                Metadata: metadata
            ));
        }

        return entries.AsReadOnly();
    }
}

/// <summary>
/// Helper for creating security audit entries.
/// </summary>
public static class SecurityAudit
{
    public static SecurityAuditEntry Create(
        SecurityEventType type,
        string message,
        bool success = true,
        string? userId = null,
        string? deviceId = null,
        string? resourceId = null,
        string? resourceType = null,
        Dictionary<string, string>? metadata = null)
    {
        return new SecurityAuditEntry(
            AuditId: Guid.NewGuid(),
            Timestamp: DateTimeOffset.UtcNow,
            EventType: type,
            Category: GetCategory(type),
            Message: message,
            UserId: userId,
            DeviceId: deviceId,
            IpAddress: null,
            ResourceId: resourceId,
            ResourceType: resourceType,
            Success: success,
            Metadata: metadata
        );
    }

    private static string GetCategory(SecurityEventType type)
    {
        return type switch
        {
            SecurityEventType.AuthenticationSuccess or
            SecurityEventType.AuthenticationFailure or
            SecurityEventType.ApiKeyValidated or
            SecurityEventType.ApiKeyRejected => "Authentication",

            SecurityEventType.AccessGranted or
            SecurityEventType.AccessDenied => "Authorization",

            SecurityEventType.EncryptionKeyAccessed or
            SecurityEventType.EncryptionKeyRotated or
            SecurityEventType.DatabaseUnlocked or
            SecurityEventType.DatabaseLockFailed => "DataProtection",

            SecurityEventType.HashChainVerified or
            SecurityEventType.HashChainViolation or
            SecurityEventType.TamperDetected => "Integrity",

            SecurityEventType.SyncPushStarted or
            SecurityEventType.SyncPushCompleted or
            SecurityEventType.SyncPushFailed or
            SecurityEventType.SyncPullStarted or
            SecurityEventType.SyncPullCompleted or
            SecurityEventType.SyncPullFailed => "Sync",

            SecurityEventType.ObligationCreated or
            SecurityEventType.ObligationClosed or
            SecurityEventType.PaymentRecorded or
            SecurityEventType.ChargeWaived or
            SecurityEventType.RulePackInstalled => "SensitiveOperation",

            _ => "System"
        };
    }
}
