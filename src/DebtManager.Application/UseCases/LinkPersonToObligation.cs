using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record LinkPersonToObligationCommand(
    Guid PersonId,
    Guid ObligationId,
    ObligationRole Role,
    DateOnly EffectiveFrom
);

/// <summary>
/// Use case: Link a person to an obligation with a specific role.
/// </summary>
public sealed class LinkPersonToObligationHandler
{
    private readonly IEventStore _eventStore;

    public LinkPersonToObligationHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task HandleAsync(
        LinkPersonToObligationCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        // Validate person exists
        var personStreamId = new StreamId(cmd.PersonId);
        var personEvents = await _eventStore.ReadStreamAsync(personStreamId, ct: ct);

        if (!personEvents.Any(e => e.EventType == nameof(PersonCreated)))
            throw new InvalidOperationException($"Person {cmd.PersonId} not found.");

        // Validate obligation exists
        var obligationStreamId = new StreamId(cmd.ObligationId);
        var obligationEvents = await _eventStore.ReadStreamAsync(obligationStreamId, ct: ct);

        if (!obligationEvents.Any(e => e.EventType == nameof(ObligationCreated)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} not found.");

        // Check for duplicate link
        var existingLinks = personEvents.Where(e => e.EventType == nameof(PersonLinkedToObligation));
        foreach (var link in existingLinks)
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<PersonLinkedToObligation>(
                link.PayloadJson, DomainJson.Options);
            if (payload?.ObligationId == cmd.ObligationId && payload?.Role == cmd.Role)
            {
                throw new InvalidOperationException(
                    $"Person {cmd.PersonId} already has role {cmd.Role} on obligation {cmd.ObligationId}.");
            }
        }

        var @event = new PersonLinkedToObligation(
            PersonId: cmd.PersonId,
            ObligationId: cmd.ObligationId,
            Role: cmd.Role,
            EffectiveFrom: cmd.EffectiveFrom
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            personStreamId,
            nameof(PersonLinkedToObligation),
            DateTimeOffset.UtcNow,
            cmd.EffectiveFrom,
            actorUserId,
            deviceId,
            CorrelationId: Guid.NewGuid(),
            CausationEventId: null,
            PayloadSchemaVersion: 1,
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(@event, DomainJson.Options)
        );

        await _eventStore.AppendAsync(envelope, ct);
    }
}
