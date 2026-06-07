using System.Collections.Concurrent;

namespace DebtManager.Application.Projections;

/// <summary>
/// In-memory projection cache keyed by projection name.
/// Validates entries against a watermark (last event occurred-at) to ensure freshness.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class ProjectionCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    public T? Get<T>(string projectionName, DateTimeOffset currentWatermark) where T : class
    {
        if (!_entries.TryGetValue(projectionName, out var entry))
            return null;

        // Invalidate if watermark has advanced (new events arrived)
        if (entry.Watermark < currentWatermark)
        {
            _entries.TryRemove(projectionName, out _);
            return null;
        }

        return entry.State as T;
    }

    public void Set<T>(string projectionName, T state, DateTimeOffset watermark) where T : class
    {
        _entries[projectionName] = new CacheEntry(state, watermark);
    }

    public void Invalidate(string projectionName)
    {
        _entries.TryRemove(projectionName, out _);
    }

    public void InvalidateAll()
    {
        _entries.Clear();
    }

    private sealed record CacheEntry(object State, DateTimeOffset Watermark);
}
