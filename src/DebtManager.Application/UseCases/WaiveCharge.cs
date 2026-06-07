using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record WaiveChargeCommand(
    Guid ObligationId,
    Guid? InstallmentKey,
    string ChargeType,
    Money WaivedAmount,
    string Reason
);

public sealed record WaiveChargeResult(
    Guid WaiverId,
    Money WaivedAmount,
    DateOnly EffectiveDate
);

/// <summary>
/// Use case: Waive (forgive) a charge on an obligation.
/// Creates an auditable record of the waiver.
/// </summary>
public sealed class WaiveChargeHandler
{
    private readonly IEventStore _eventStore;

    public WaiveChargeHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<WaiveChargeResult> HandleAsync(
        WaiveChargeCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason))
            throw new ArgumentException("Waiver reason is required.", nameof(cmd));

        if (cmd.WaivedAmount.Amount <= 0)
            throw new ArgumentException("Waived amount must be positive.", nameof(cmd));

        var streamId = new StreamId(cmd.ObligationId);
        var events = await _eventStore.ReadStreamAsync(streamId, ct: ct);

        // Validate obligation exists and is not closed
        if (!events.Any(e => e.EventType == nameof(ObligationCreated)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} not found.");

        if (events.Any(e => e.EventType == nameof(ObligationClosed)))
            throw new InvalidOperationException($"Cannot waive charges on closed obligation {cmd.ObligationId}.");

        var waiverId = Guid.NewGuid();
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var @event = new ChargeWaived(
            WaiverId: waiverId,
            ObligationId: cmd.ObligationId,
            InstallmentKey: cmd.InstallmentKey,
            ChargeType: cmd.ChargeType,
            WaivedAmount: cmd.WaivedAmount,
            Reason: cmd.Reason,
            ApprovedBy: actorUserId,
            EffectiveDate: effectiveDate
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            nameof(ChargeWaived),
            DateTimeOffset.UtcNow,
            effectiveDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(@event, DomainJson.Options)
        );

        await _eventStore.AppendAsync(envelope, ct);

        return new WaiveChargeResult(
            WaiverId: waiverId,
            WaivedAmount: cmd.WaivedAmount,
            EffectiveDate: effectiveDate
        );
    }
}
