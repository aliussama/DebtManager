namespace DebtManager.Domain.Projections.Snapshots;

public readonly record struct SnapshotId(Guid Value);

public sealed record ProjectionSnapshotEnvelope(
    SnapshotId SnapshotId,
    string ProjectionName,
    DateOnly AsOfDate,
    DateTimeOffset CreatedAt,
    Guid DeviceId,
    int SchemaVersion,
    string PayloadJson,
    Guid LastEventId,
    DateTimeOffset LastOccurredAt
);

public interface IProjectionSnapshotStore
{
    Task SaveAsync(ProjectionSnapshotEnvelope snapshot, CancellationToken ct);
    Task<ProjectionSnapshotEnvelope?> LoadLatestAsync(string projectionName, DateOnly asOfDate, CancellationToken ct);
    Task PruneAsync(string projectionName, int keepLastN, CancellationToken ct);
}
