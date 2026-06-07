using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;

namespace DebtManager.Application.UseCases;

public sealed record DefineScheduleCommand(
    Guid ScheduleId,
    Guid ObligationId,
    string ScheduleType,
    string ScheduleSpecJson,
    string Timezone,
    DateOnly EffectiveDate
);

public sealed class DefineScheduleHandler
{
    private readonly IEventStore _store;

    public DefineScheduleHandler(IEventStore store)
    {
        _store = store;
    }

    public async Task HandleAsync(DefineScheduleCommand cmd, Guid actorUserId, Guid deviceId, CancellationToken ct)
    {
        var ev = new ScheduleDefined(
            ScheduleId: cmd.ScheduleId,
            ObligationId: cmd.ObligationId,
            ScheduleType: cmd.ScheduleType,
            ScheduleSpecJson: cmd.ScheduleSpecJson,
            Timezone: cmd.Timezone,
            EffectiveDate: cmd.EffectiveDate
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(cmd.ObligationId),
            nameof(ScheduleDefined),
            DateTimeOffset.UtcNow,
            ev.EffectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(ev, DebtManager.Domain.ValueObjects.DomainJson.Options)
        );

        await _store.AppendAsync(envelope, ct);
    }
}
