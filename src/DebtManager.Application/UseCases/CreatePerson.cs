using DebtManager.Domain.Events;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.UseCases;

public sealed record CreatePersonCommand(
    string FullName,
    PersonRole PrimaryRole,
    ContactInfo? Contact
);

public sealed record CreatePersonResult(Guid PersonId);

/// <summary>
/// Use case: Create a new person in the system.
/// </summary>
public sealed class CreatePersonHandler
{
    private readonly IEventStore _eventStore;

    public CreatePersonHandler(IEventStore eventStore)
    {
        _eventStore = eventStore;
    }

    public async Task<CreatePersonResult> HandleAsync(
        CreatePersonCommand cmd,
        Guid actorUserId,
        Guid deviceId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.FullName))
            throw new ArgumentException("Full name is required.", nameof(cmd));

        var personId = Guid.NewGuid();
        var effectiveDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var @event = new PersonCreated(
            PersonId: personId,
            FullName: cmd.FullName.Trim(),
            PrimaryRole: cmd.PrimaryRole,
            Contact: cmd.Contact,
            EffectiveDate: effectiveDate
        );

        var envelope = new EventEnvelope(
            new EventId(Guid.NewGuid()),
            new StreamId(personId),
            nameof(PersonCreated),
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

        return new CreatePersonResult(personId);
    }
}
