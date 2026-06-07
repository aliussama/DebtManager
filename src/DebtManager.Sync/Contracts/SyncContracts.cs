namespace DebtManager.Sync.Contracts;

public sealed record SyncEventDto(
    Guid EventId,
    Guid StreamId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateOnly EffectiveDate,
    Guid ActorUserId,
    Guid DeviceId,
    Guid CorrelationId,
    Guid? CausationEventId,
    int PayloadSchemaVersion,
    string PayloadJson
);

public sealed record PushBatchRequest(Guid DeviceId, IReadOnlyList<SyncEventDto> Events);
public sealed record PushBatchResponse(int Accepted, int AlreadyPresent);

public sealed record PullResponse(string Cursor, IReadOnlyList<SyncEventDto> Events);
