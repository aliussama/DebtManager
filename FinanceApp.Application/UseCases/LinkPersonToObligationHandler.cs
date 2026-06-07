namespace FinanceApp.Application.UseCases;

using FinanceApp.Domain.Aggregates;
using FinanceApp.Domain.Events;
using FinanceApp.Application.Ports;

public sealed record LinkPersonToObligationCommand(
    Guid PersonId,
    Guid ObligationId,
    ObligationRole Role,
    DateTimeOffset EffectiveFrom
);

/// <summary>
/// Use case: Link an existing person to an obligation with a role.
/// </summary>
public sealed class LinkPersonToObligationHandler
{
    private readonly IEventStore _eventStore;
    private readonly TimeProvider _timeProvider;

    public LinkPersonToObligationHandler(IEventStore eventStore, TimeProvider timeProvider)
    {
        _eventStore = eventStore;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(
        LinkPersonToObligationCommand command,
        CancellationToken ct = default)
    {
        var streamId = $"person-{command.PersonId}";
        var events = await _eventStore.LoadAsync(streamId, ct);

        if (!events.Any())
            throw new InvalidOperationException($"Person {command.PersonId} not found.");

        var person = Person.FromHistory(events);
        person.LinkToObligation(
            command.ObligationId,
            command.Role,
            command.EffectiveFrom,
            _timeProvider.GetUtcNow()
        );

        await _eventStore.AppendAsync(streamId, person.UncommittedEvents, ct);
        person.ClearUncommittedEvents();
    }
}