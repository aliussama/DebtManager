using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record LinkObligationToInstitutionCommand(
    Guid ObligationId,
    Guid InstitutionId,
    string? ProductCode,
    string? ContractReference
);

/// <summary>
/// Use case: Link an obligation to a financial institution.
/// This determines which rule packs apply to the obligation.
/// </summary>
public sealed class LinkObligationToInstitutionHandler
{
    private readonly IEventStore _eventStore;

    public LinkObligationToInstitutionHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task HandleAsync(
        LinkObligationToInstitutionCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        // Validate institution exists
        var institutionStreamId = new StreamId(cmd.InstitutionId);
        var institutionEvents = await _eventStore.ReadStreamAsync(institutionStreamId, ct: ct);

        if (!institutionEvents.Any(e => e.EventType == nameof(FinancialInstitutionRegistered)))
            throw new InvalidOperationException($"Institution {cmd.InstitutionId} not found.");

        // Validate obligation exists
        var obligationStreamId = new StreamId(cmd.ObligationId);
        var obligationEvents = await _eventStore.ReadStreamAsync(obligationStreamId, ct: ct);

        if (!obligationEvents.Any(e => e.EventType == nameof(ObligationCreated)))
            throw new InvalidOperationException($"Obligation {cmd.ObligationId} not found.");

        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var @event = new ObligationLinkedToInstitution(
            ObligationId: cmd.ObligationId,
            InstitutionId: cmd.InstitutionId,
            ProductCode: cmd.ProductCode,
            ContractReference: cmd.ContractReference,
            EffectiveDate: effectiveDate
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            obligationStreamId,
            nameof(ObligationLinkedToInstitution),
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
    }
}
