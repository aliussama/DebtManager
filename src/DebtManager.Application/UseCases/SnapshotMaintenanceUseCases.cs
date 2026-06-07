using DebtManager.Application.Projections;
using DebtManager.Domain.Projections.Snapshots;

namespace DebtManager.Application.UseCases;

public sealed class PruneSnapshotsHandler
{
    private readonly IProjectionSnapshotStore _snapshotStore;

    public PruneSnapshotsHandler(IProjectionSnapshotStore snapshotStore)
    {
        _snapshotStore = snapshotStore;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        foreach (var projection in ProjectionCachePolicies.SnapshottableProjections)
        {
            await _snapshotStore.PruneAsync(projection, ProjectionCachePolicies.PruneKeepLastN, ct);
        }
    }
}

public sealed class ClearProjectionCacheHandler
{
    private readonly ProjectionCache _cache;

    public ClearProjectionCacheHandler(ProjectionCache cache)
    {
        _cache = cache;
    }

    public Task HandleAsync(CancellationToken ct)
    {
        _cache.InvalidateAll();
        return Task.CompletedTask;
    }
}

public sealed class RebuildSnapshotsHandler
{
    private readonly PruneSnapshotsHandler _pruneHandler;
    private readonly ClearProjectionCacheHandler _cacheHandler;

    public RebuildSnapshotsHandler(
        PruneSnapshotsHandler pruneHandler,
        ClearProjectionCacheHandler cacheHandler)
    {
        _pruneHandler = pruneHandler;
        _cacheHandler = cacheHandler;
    }

    public async Task HandleAsync(CancellationToken ct)
    {
        await _cacheHandler.HandleAsync(ct);
        await _pruneHandler.HandleAsync(ct);
        // Snapshots will be recreated lazily on the next query
    }
}
