using DebtManager.Domain.Events;

namespace DebtManager.Domain.Projections;

public sealed record ProjectedEvent(
    EventEnvelope Envelope,
    IDomainEvent Event
);
