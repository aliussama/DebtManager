using Azure;
using Azure.Data.Tables;

namespace DebtManager.Cloud.Storage;

public sealed class EventEntity : ITableEntity
{
    public string PartitionKey { get; set; } = default!; // vaultId
    public string RowKey { get; set; } = default!;       // {ticks}_{eventIdN}

    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // envelope fields
    public string EventId { get; set; } = default!;
    public string StreamId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public string OccurredAt { get; set; } = default!;
    public string EffectiveDate { get; set; } = default!;
    public string ActorUserId { get; set; } = default!;
    public string DeviceId { get; set; } = default!;
    public string CorrelationId { get; set; } = default!;
    public string? CausationEventId { get; set; }

    public int PayloadSchemaVersion { get; set; }
    public string PayloadJson { get; set; } = default!;

    // integrity (optional now; required later)
    public string? PrevHash { get; set; }
    public string? Hash { get; set; }
}
