using DebtManager.Domain.Events;

namespace DebtManager.Infrastructure.Simulation;

public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<EventEnvelope> _events;

    public InMemoryEventStore(IEnumerable<EventEnvelope> baseline)
    {
        _events = baseline
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ToList();
    }

    public Task AppendAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // pure in-memory append
        _events.Add(envelope);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadStreamAsync(StreamId streamId, DateOnly? upTo = null, CancellationToken ct = default)
    {
        var result = _events
            .Where(e => e.StreamId.Value == streamId.Value)
            .Where(e => upTo is null || e.EffectiveDate <= upTo.Value)
            .OrderBy(e => e.EffectiveDate)
            .ThenBy(e => e.OccurredAt)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<EventEnvelope>>(result);
    }

    public Task<IReadOnlyList<EventEnvelope>> ReadAllAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        var result = _events
            .Where(e => e.OccurredAt >= since)
            .OrderBy(e => e.OccurredAt)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<EventEnvelope>>(result);
    }
}
