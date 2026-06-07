namespace FinanceApp.Application.UseCases;

using FinanceApp.Domain.Aggregates;
using FinanceApp.Domain.Events;
using FinanceApp.Application.Ports;

public sealed record CreatePersonCommand(
    string FullName,
    PersonRole Role,
    ContactInfo? Contact
);

public sealed record CreatePersonResult(Guid PersonId);

/// <summary>
/// Use case: Create a new person in the system.
/// </summary>
public sealed class CreatePersonHandler
{
    private readonly IEventStore _eventStore;
    private readonly TimeProvider _timeProvider;

    public CreatePersonHandler(IEventStore eventStore, TimeProvider timeProvider)
    {
        _eventStore = eventStore;
        _timeProvider = timeProvider;
    }

    public async Task<CreatePersonResult> HandleAsync(
        CreatePersonCommand command,
        CancellationToken ct = default)
    {
        var personId = Guid.NewGuid();
        var person = Person.Create(
            personId,
            command.FullName,
            command.Role,
            command.Contact,
            _timeProvider.GetUtcNow()
        );

        var streamId = $"person-{personId}";
        await _eventStore.AppendAsync(streamId, person.UncommittedEvents, ct);
        person.ClearUncommittedEvents();

        return new CreatePersonResult(personId);
    }
}