namespace FinanceApp.Application.UseCases;

using FinanceApp.Domain.Events;
using FinanceApp.Application.Ports;

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
    private readonly TimeProvider _timeProvider;

    public LinkObligationToInstitutionHandler(IEventStore eventStore, TimeProvider timeProvider)
    {
        _eventStore = eventStore;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(
        LinkObligationToInstitutionCommand command,
        CancellationToken ct = default)
    {
        // Verify institution exists
        var institutionStreamId = $"institution-{command.InstitutionId}";
        var institutionEvents = await _eventStore.LoadAsync(institutionStreamId, ct);
        if (!institutionEvents.Any())
            throw new InvalidOperationException($"Institution {command.InstitutionId} not found.");

        // Verify obligation exists
        var obligationStreamId = $"obligation-{command.ObligationId}";
        var obligationEvents = await _eventStore.LoadAsync(obligationStreamId, ct);
        if (!obligationEvents.Any())
            throw new InvalidOperationException($"Obligation {command.ObligationId} not found.");

        var @event = new ObligationLinkedToInstitution(
            EventId: Guid.NewGuid(),
            ObligationId: command.ObligationId,
            InstitutionId: command.InstitutionId,
            ProductCode: command.ProductCode,
            ContractReference: command.ContractReference,
            OccurredAt: _timeProvider.GetUtcNow()
        );

        // Append to obligation stream (primary location)
        await _eventStore.AppendAsync(obligationStreamId, new[] { @event }, ct);
    }
}