namespace FinanceApp.Domain.Aggregates;

using FinanceApp.Domain.Events;

/// <summary>
/// Aggregate root for a Financial Institution.
/// Institutions don't just hold money — they define behavior (rules).
/// </summary>
public sealed class FinancialInstitution
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public InstitutionType Type { get; private set; }
    public string CountryCode { get; private set; } = string.Empty;
    public InstitutionMetadata? Metadata { get; private set; }
    public DateTimeOffset RegisteredAt { get; private set; }

    private readonly List<Guid> _linkedObligationIds = new();
    public IReadOnlyList<Guid> LinkedObligationIds => _linkedObligationIds.AsReadOnly();

    private readonly List<IDomainEvent> _uncommittedEvents = new();
    public IReadOnlyList<IDomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    private FinancialInstitution() { }

    /// <summary>
    /// Factory method: Register a new financial institution.
    /// </summary>
    public static FinancialInstitution Register(
        Guid institutionId,
        string name,
        InstitutionType type,
        string countryCode,
        InstitutionMetadata? metadata,
        DateTimeOffset occurredAt)
    {
        if (institutionId == Guid.Empty)
            throw new ArgumentException("Institution ID cannot be empty.", nameof(institutionId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Institution name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("Country code must be a 2-letter ISO code.", nameof(countryCode));

        var institution = new FinancialInstitution();
        var @event = new FinancialInstitutionRegistered(
            EventId: Guid.NewGuid(),
            InstitutionId: institutionId,
            Name: name.Trim(),
            Type: type,
            CountryCode: countryCode.ToUpperInvariant(),
            Metadata: metadata,
            OccurredAt: occurredAt
        );

        institution.Apply(@event);
        institution._uncommittedEvents.Add(@event);
        return institution;
    }

    /// <summary>
    /// Reconstitute aggregate from event history.
    /// </summary>
    public static FinancialInstitution FromHistory(IEnumerable<IDomainEvent> events)
    {
        var institution = new FinancialInstitution();
        foreach (var @event in events)
        {
            institution.Apply(@event);
        }
        return institution;
    }

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    internal void TrackLinkedObligation(Guid obligationId)
    {
        if (!_linkedObligationIds.Contains(obligationId))
        {
            _linkedObligationIds.Add(obligationId);
        }
    }

    private void Apply(IDomainEvent @event)
    {
        switch (@event)
        {
            case FinancialInstitutionRegistered e:
                Id = e.InstitutionId;
                Name = e.Name;
                Type = e.Type;
                CountryCode = e.CountryCode;
                Metadata = e.Metadata;
                RegisteredAt = e.OccurredAt;
                break;

            case ObligationLinkedToInstitution e:
                TrackLinkedObligation(e.ObligationId);
                break;
        }
    }
}