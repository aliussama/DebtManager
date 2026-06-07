namespace DebtManager.Domain.Events;

public readonly record struct EventId(Guid Value);
public readonly record struct StreamId(Guid Value);

public sealed record EventEnvelope(
    EventId EventId,
    StreamId StreamId,
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

public interface IEventStore
{
    Task AppendAsync(EventEnvelope envelope, CancellationToken ct);
    Task<IReadOnlyList<EventEnvelope>> ReadStreamAsync(StreamId streamId, DateOnly? upTo = null, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> ReadAllAsync(DateTimeOffset since, CancellationToken ct = default);
}
