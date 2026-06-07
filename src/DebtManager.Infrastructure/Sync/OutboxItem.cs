namespace DebtManager.Infrastructure.Sync;

public sealed record OutboxItem(
    Guid EventId,
    Guid StreamId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateOnly EffectiveDate,
    int PayloadSchemaVersion,
    string PayloadJson,
    string? PrevHash,
    string Hash,
    Guid OriginDeviceId,
    int Attempts
);
