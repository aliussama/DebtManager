namespace FinanceApp.Domain.Aggregates;

using FinanceApp.Domain.Events;

/// <summary>
/// Aggregate root for a Person entity.
/// A person is a real individual whose finances are managed.
/// State is derived from event replay — never mutated directly.
/// </summary>
public sealed class Person
{
    public Guid Id { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public PersonRole PrimaryRole { get; private set; }
    public ContactInfo? Contact { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<ObligationLink> _obligationLinks = new();
    public IReadOnlyList<ObligationLink> ObligationLinks => _obligationLinks.AsReadOnly();

    private readonly List<IDomainEvent> _uncommittedEvents = new();
    public IReadOnlyList<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    private Person() { }

    /// <summary>
    /// Factory method: Create a new person (emits PersonCreated event).
    /// </summary>
    public static Person Create(
        Guid personId,
        string fullName,
        PersonRole role,
        ContactInfo? contact,
        DateTimeOffset occurredAt)
    {
        if (personId == Guid.Empty)
            throw new ArgumentException("Person ID cannot be empty.", nameof(personId));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));

        var person = new Person();
        var @event = new PersonCreated(
            EventId: Guid.NewGuid(),
            PersonId: personId,
            FullName: fullName.Trim(),
            Role: role,
            Contact: contact,
            OccurredAt: occurredAt
        );

        person.Apply(@event);
        person._uncommittedEvents.Add(@event);
        return person;
    }

    /// <summary>
    /// Link this person to an obligation with a specific role.
    /// </summary>
    public void LinkToObligation(
        Guid obligationId,
        ObligationRole role,
        DateTimeOffset effectiveFrom,
        DateTimeOffset occurredAt)
    {
        if (obligationId == Guid.Empty)
            throw new ArgumentException("Obligation ID cannot be empty.", nameof(obligationId));

        // Validate: same person cannot have duplicate role on same obligation
        if (_obligationLinks.Any(l => l.ObligationId == obligationId && l.Role == role))
            throw new InvalidOperationException(
                $"Person {Id} already has role {role} on obligation {obligationId}.");

        var @event = new PersonLinkedToObligation(
            EventId: Guid.NewGuid(),
            PersonId: Id,
            ObligationId: obligationId,
            Role: role,
            EffectiveFrom: effectiveFrom,
            OccurredAt: occurredAt
        );

        Apply(@event);
        _uncommittedEvents.Add(@event);
    }

    /// <summary>
    /// Reconstitute aggregate from event history.
    /// </summary>
    public static Person FromHistory(IEnumerable<IDomainEvent> events)
    {
        var person = new Person();
        foreach (var @event in events)
        {
            person.Apply(@event);
        }
        return person;
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    private void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case PersonCreated e:
                Id = e.PersonId;
                FullName = e.FullName;
                PrimaryRole = e.Role;
                Contact = e.Contact;
                CreatedAt = e.OccurredAt;
                break;

            case PersonLinkedToObligation e:
                _obligationLinks.Add(new ObligationLink(
                    e.ObligationId,
                    e.Role,
                    e.EffectiveFrom
                ));
                break;
        }
    }
}

/// <summary>
/// Value object representing a person's link to an obligation.
/// </summary>
public sealed record ObligationLink(
    Guid ObligationId,
    ObligationRole Role,
    DateTimeOffset EffectiveFrom
);