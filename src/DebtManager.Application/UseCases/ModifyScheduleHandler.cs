using DebtManager.Domain.Events;
using DebtManager.Domain.Scheduling;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record ModifyScheduleCommand(
    Guid ObligationId,
    Guid OriginalScheduleId,
    ScheduleModificationType ModificationType,
    DateOnly EffectiveDate,
    string Reason,
    int? DaysDeferred = null,
    int? NewTermMonths = null,
    Money? NewInstallmentAmount = null,
    decimal? NewInterestRate = null,
    RecurrencePattern? NewPattern = null,
    DateOnly? NewEndDate = null
);

public sealed record ModifyScheduleResult(
    Guid NewScheduleId,
    ScheduleModificationType ModificationType,
    DateOnly EffectiveDate
);

/// <summary>
/// Use case: Modify an existing schedule (defer, restructure, change terms).
/// Emits ScheduleModified event — never overwrites the original schedule.
/// </summary>
public sealed class ModifyScheduleHandler
{
    private readonly IEventStore _eventStore;

    public ModifyScheduleHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<ModifyScheduleResult> HandleAsync(
        ModifyScheduleCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        // Validate obligation exists
        var streamId = new StreamId(cmd.ObligationId);
        var existingEvents = await _eventStore.ReadStreamAsync(streamId, ct: ct);

        if (!existingEvents.Any(e => e.EventType == nameof(ObligationCreated)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} not found.");

        // Validate original schedule exists
        var scheduleEvents = existingEvents.Where(e =>
            e.EventType == nameof(ScheduleDefined) ||
            e.EventType == nameof(ScheduleModified));

        var hasOriginalSchedule = scheduleEvents.Any(e =>
        {
            if (e.EventType == nameof(ScheduleDefined))
            {
                var payload = System.Text.Json.JsonSerializer.Deserialize<ScheduleDefinedPayload>(
                    e.PayloadJson, DomainJson.Options);
                return payload?.ScheduleId == cmd.OriginalScheduleId;
            }
            return false;
        });

        if (!hasOriginalSchedule)
            throw new InvalidOperationException($"Schedule {cmd.OriginalScheduleId} not found for obligation {cmd.ObligationId}.");

        // Generate new schedule ID
        var newScheduleId = Guid.NewGuid();

        var details = new ScheduleModificationDetails(
            DaysDeferred: cmd.DaysDeferred,
            NewTermMonths: cmd.NewTermMonths,
            NewInstallmentAmount: cmd.NewInstallmentAmount,
            NewInterestRate: cmd.NewInterestRate,
            NewPattern: cmd.NewPattern,
            NewEndDate: cmd.NewEndDate,
            AdditionalData: null
        );

        var @event = new ScheduleModified(
            EventId: Guid.NewGuid(),
            ObligationId: cmd.ObligationId,
            OriginalScheduleId: cmd.OriginalScheduleId,
            NewScheduleId: newScheduleId,
            ModificationType: cmd.ModificationType,
            EffectiveDate: cmd.EffectiveDate,
            Reason: cmd.Reason,
            Details: details,
            OccurredAt: DateTimeOffset.UtcNow,
            ActorUserId: actorUserId,
            DeviceId: deviceId
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            nameof(ScheduleModified),
            DateTimeOffset.UtcNow,
            cmd.EffectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(@event, DomainJson.Options)
        );

        await _eventStore.AppendAsync(envelope, ct);

        return new ModifyScheduleResult(
            NewScheduleId: newScheduleId,
            ModificationType: cmd.ModificationType,
            EffectiveDate: cmd.EffectiveDate
        );
    }

    // Helper record for deserialization
    private sealed record ScheduleDefinedPayload(Guid ScheduleId);
}