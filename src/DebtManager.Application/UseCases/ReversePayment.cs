using DebtManager.Application.Internal;
using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;
using System.Text.Json;

namespace DebtManager.Application.UseCases;

/// <summary>
/// Command to reverse a payment.
/// </summary>
public sealed record ReversePaymentCommand(
    Guid ObligationId,
    Guid PaymentEventId,
    DateOnly EffectiveDate,
    string Reason
);

/// <summary>
/// Result of reversing a payment.
/// </summary>
public sealed record ReversePaymentResult(
    Guid ReversalEventId,
    int AllocationsReversed
);

/// <summary>
/// Handler to reverse a payment and its allocations.
/// Creates PaymentReversed + PaymentAllocationReversed events.
/// </summary>
public sealed class ReversePaymentHandler
{
    private readonly IEventStore _eventStore;

    public ReversePaymentHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<ReversePaymentResult> HandleAsync(
        ReversePaymentCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        var streamId = new StreamId(cmd.ObligationId);
        var events = await _eventStore.ReadStreamAsync(streamId, ct: ct);

        // Find the payment to reverse
        StoredPaymentMade? targetPayment = null;
        foreach (var envelope in events.Where(e => e.EventType == nameof(PaymentMade)))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<StoredPaymentMade>(
                    envelope.PayloadJson, DomainJson.Options);
                if (stored?.PaymentEventId == cmd.PaymentEventId)
                {
                    targetPayment = stored;
                    break;
                }
            }
            catch { }
        }

        if (targetPayment == null)
        {
            throw new InvalidOperationException($"Payment {cmd.PaymentEventId} not found in obligation {cmd.ObligationId}.");
        }

        // Check if already reversed
        var alreadyReversed = events.Any(e =>
        {
            if (e.EventType != nameof(PaymentReversed))
                return false;

            try
            {
                var rev = JsonSerializer.Deserialize<PaymentReversed>(
                    e.PayloadJson, DomainJson.Options);
                return rev?.OriginalPaymentEventId == cmd.PaymentEventId;
            }
            catch
            {
                return false;
            }
        });

        if (alreadyReversed)
        {
            throw new InvalidOperationException($"Payment {cmd.PaymentEventId} has already been reversed.");
        }

        var correlationId = Guid.NewGuid();
        var reversalEventId = Guid.NewGuid();

        // Create PaymentReversed event
        var reversedEvent = new PaymentReversed(
            OriginalPaymentEventId: cmd.PaymentEventId,
            ObligationId: cmd.ObligationId,
            Amount: targetPayment.Payment.Amount,
            EffectiveDate: cmd.EffectiveDate,
            Reason: cmd.Reason
        );

        var reversedEnvelope = new EventEnvelope(
            EventId: new EventId(reversalEventId),
            StreamId: streamId,
            EventType: nameof(PaymentReversed),
            OccurredAt: DateTimeOffset.UtcNow,
            EffectiveDate: cmd.EffectiveDate,
            ActorUserId: actorUserId,
            DeviceId: deviceId,
            CorrelationId: correlationId,
            CausationEventId: cmd.PaymentEventId,
            PayloadSchemaVersion: 1,
            PayloadJson: JsonSerializer.Serialize(reversedEvent, DomainJson.Options)
        );

        await _eventStore.AppendAsync(reversedEnvelope, ct);

        // Find and reverse all allocations linked to this payment
        var allocationsReversed = 0;
        foreach (var envelope in events.Where(e => e.EventType == nameof(PaymentAllocated)))
        {
            try
            {
                var allocated = JsonSerializer.Deserialize<PaymentAllocated>(
                    envelope.PayloadJson, DomainJson.Options);
                
                if (allocated?.PaymentEventId != cmd.PaymentEventId)
                    continue;

                var allocationReversed = new PaymentAllocationReversed(
                    OriginalPaymentEventId: cmd.PaymentEventId,
                    ObligationId: cmd.ObligationId,
                    InstallmentKey: allocated.InstallmentKey,
                    Amount: allocated.Amount,
                    EffectiveDate: cmd.EffectiveDate,
                    Reason: cmd.Reason
                );

                var allocReversedEnvelope = new EventEnvelope(
                    EventId: new EventId(Guid.NewGuid()),
                    StreamId: streamId,
                    EventType: nameof(PaymentAllocationReversed),
                    OccurredAt: DateTimeOffset.UtcNow,
                    EffectiveDate: cmd.EffectiveDate,
                    ActorUserId: actorUserId,
                    DeviceId: deviceId,
                    CorrelationId: correlationId,
                    CausationEventId: cmd.PaymentEventId,
                    PayloadSchemaVersion: 1,
                    PayloadJson: JsonSerializer.Serialize(allocationReversed, DomainJson.Options)
                );

                await _eventStore.AppendAsync(allocReversedEnvelope, ct);
                allocationsReversed++;
            }
            catch { }
        }

        return new ReversePaymentResult(
            ReversalEventId: reversalEventId,
            AllocationsReversed: allocationsReversed
        );
    }
}
