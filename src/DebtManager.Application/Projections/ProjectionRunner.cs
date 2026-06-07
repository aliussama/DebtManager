using System.Text.Json;
using DebtManager.Domain.Events;
using DebtManager.Domain.Projections.Snapshots;
using DebtManager.Domain.ValueObjects;

namespace DebtManager.Application.Projections;

/// <summary>
/// Orchestrates projection execution with optional snapshot + in-memory cache support.
/// Guarantees: snapshot + tail replay == full replay (deterministic).
/// </summary>
public sealed class ProjectionRunner
{
    private readonly IEventStore _store;
    private readonly IProjectionSnapshotStore? _snapshotStore;
    private readonly ProjectionCache _cache;
    private readonly Guid _deviceId;
    private readonly bool _snapshotsEnabled;

    public ProjectionRunner(
        IEventStore store,
        IProjectionSnapshotStore? snapshotStore,
        ProjectionCache cache,
        Guid deviceId,
        bool snapshotsEnabled = true)
    {
        _store = store;
        _snapshotStore = snapshotStore;
        _cache = cache;
        _deviceId = deviceId;
        _snapshotsEnabled = snapshotsEnabled;
    }

    /// <summary>
    /// Run a projection, using snapshot + tail optimization when available.
    /// </summary>
    /// <typeparam name="T">The projection state type.</typeparam>
    /// <param name="projectionName">Stable name for the projection (used as cache/snapshot key).</param>
    /// <param name="projectAll">Full projector: given all events, returns state.</param>
    /// <param name="projectTail">Incremental projector: given existing state + tail events, returns updated state. Can be null to disable incremental.</param>
    /// <param name="asOfDate">Optional effective date filter.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<T> RunAsync<T>(
        string projectionName,
        Func<IEnumerable<EventEnvelope>, T> projectAll,
        Func<T, IEnumerable<EventEnvelope>, T>? projectTail = null,
        DateOnly? asOfDate = null,
        CancellationToken ct = default) where T : class
    {
        // 1) Read all events to determine watermark
        var allEnvelopes = await _store.ReadAllAsync(DateTimeOffset.MinValue, ct);

        if (allEnvelopes.Count == 0)
            return projectAll(allEnvelopes);

        var lastEnvelope = allEnvelopes[^1];
        var currentWatermark = lastEnvelope.OccurredAt;

        // 2) Try in-memory cache
        var cached = _cache.Get<T>(projectionName, currentWatermark);
        if (cached != null)
            return cached;

        // 3) Try snapshot store
        T? state = default;
        int tailCount = allEnvelopes.Count;
        bool usedSnapshot = false;

        if (_snapshotsEnabled && _snapshotStore != null && projectTail != null)
        {
            var snapshotDate = asOfDate ?? DateOnly.FromDateTime(DateTime.Today);
            var snapshot = await _snapshotStore.LoadLatestAsync(projectionName, snapshotDate, ct);

            if (snapshot != null)
            {
                var expectedVersion = ProjectionCachePolicies.GetSchemaVersion(projectionName);
                if (snapshot.SchemaVersion == expectedVersion)
                {
                    try
                    {
                        var deserialized = JsonSerializer.Deserialize<T>(snapshot.PayloadJson, DomainJson.Options);
                        if (deserialized != null)
                        {
                            // Find tail: events after the snapshot's last event
                            var tailEvents = allEnvelopes
                                .Where(e => e.OccurredAt > snapshot.LastOccurredAt)
                                .ToList();

                            state = projectTail(deserialized, tailEvents);
                            tailCount = tailEvents.Count;
                            usedSnapshot = true;
                        }
                    }
                    catch (JsonException)
                    {
                        // Schema mismatch or corrupt payload; ignore snapshot, rebuild
                    }
                }
            }
        }

        // 4) Full replay fallback
        if (state == null)
        {
            state = projectAll(allEnvelopes);
            tailCount = allEnvelopes.Count;
        }

        // 5) Update in-memory cache
        _cache.Set(projectionName, state, currentWatermark);

        // 6) Optionally write new snapshot (policy-based)
        if (_snapshotsEnabled && _snapshotStore != null)
        {
            await MaybeWriteSnapshotAsync(
                projectionName, state, lastEnvelope, tailCount, usedSnapshot, ct);
        }

        return state;
    }

    private async Task MaybeWriteSnapshotAsync<T>(
        string projectionName,
        T state,
        EventEnvelope lastEnvelope,
        int eventsSinceSnapshot,
        bool usedSnapshot,
        CancellationToken ct) where T : class
    {
        if (!ProjectionCachePolicies.SnapshottableProjections.Contains(projectionName))
            return;

        // Write snapshot if threshold exceeded
        var shouldSnapshot = eventsSinceSnapshot >= ProjectionCachePolicies.SnapshotEventThreshold;

        if (!shouldSnapshot && !usedSnapshot)
        {
            // First time or old snapshot — create one if we just did full replay
            // and there are enough events to warrant it
            shouldSnapshot = eventsSinceSnapshot >= ProjectionCachePolicies.SnapshotEventThreshold;
        }

        if (!shouldSnapshot)
            return;

        try
        {
            var payloadJson = JsonSerializer.Serialize(state, DomainJson.Options);
            var envelope = new ProjectionSnapshotEnvelope(
                new SnapshotId(Guid.NewGuid()),
                projectionName,
                lastEnvelope.EffectiveDate,
                DateTimeOffset.UtcNow,
                _deviceId,
                ProjectionCachePolicies.GetSchemaVersion(projectionName),
                payloadJson,
                lastEnvelope.EventId.Value,
                lastEnvelope.OccurredAt);

            await _snapshotStore!.SaveAsync(envelope, ct);
        }
        catch
        {
            // Snapshot save failure is non-fatal; next query will rebuild
        }
    }
}
