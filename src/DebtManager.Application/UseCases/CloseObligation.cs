using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record CloseObligationCommand(
    Guid ObligationId,
    ObligationClosureType ClosureType,
    Money FinalBalance,
    string? Reason,
    string? Notes
);

public sealed record CloseObligationResult(
    Guid ObligationId,
    DateOnly ClosureDate,
    ObligationClosureType ClosureType
);

/// <summary>
/// Use case: Close an obligation (settle, pay off, write off).
/// The obligation is never deleted — this marks its end state.
/// </summary>
public sealed class CloseObligationHandler
{
    private readonly IEventStore _eventStore;

    public CloseObligationHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<CloseObligationResult> HandleAsync(
        CloseObligationCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        var streamId = new StreamId(cmd.ObligationId);
        var events = await _eventStore.ReadStreamAsync(streamId, ct: ct);

        // Validate obligation exists
        if (!events.Any(e => e.EventType == nameof(ObligationCreated)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} not found.");

        // Check if already closed
        if (events.Any(e => e.EventType == nameof(ObligationClosed)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} is already closed.");

        var closureDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var @event = new ObligationClosed(
            ObligationId: cmd.ObligationId,
            ClosureDate: closureDate,
            ClosureType: cmd.ClosureType,
            FinalBalance: cmd.FinalBalance,
            Reason: cmd.Reason,
            Notes: cmd.Notes
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            streamId,
            nameof(ObligationClosed),
            DateTimeOffset.UtcNow,
            closureDate,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(@event, DomainJson.Options)
        );

        await _eventStore.AppendAsync(envelope, ct);

        return new CloseObligationResult(
            ObligationId: cmd.ObligationId,
            ClosureDate: closureDate,
            ClosureType: cmd.ClosureType
        );
    }
}
