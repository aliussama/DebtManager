namespace FinanceApp.Domain.Events;

/// <summary>
/// Marker interface for all domain events.
/// Events are immutable facts that happened at a specific moment.
/// History is sacred — events are never deleted or modified.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Unique identifier for this event instance.</summary>
    Guid EventId { get; }

    /// <summary>Type discriminator for serialization/deserialization.</summary>
    string EventType { get; }

    /// <summary>When this event was recorded.</summary>
    DateTimeOffset OccurredAt { get; }
}