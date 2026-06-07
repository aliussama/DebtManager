using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Sync;

namespace DebtManager.Infrastructure.Sync;

public interface ISyncStore
{
    Task<IReadOnlyList<OutboxItem>> ReadOutboxBatchAsync(int max, CancellationToken ct = default);
    Task<IReadOnlyList<EventEnvelope>> ReadEnvelopesByEventIdsAsync(IEnumerable<Guid> eventIds, CancellationToken ct = default);
    Task MarkOutboxSentAsync(IEnumerable<Guid> eventIds, CancellationToken ct = default);
    Task MarkOutboxAttemptFailedAsync(Guid eventId, string error, CancellationToken ct = default);

    Task ApplyRemoteAsync(IReadOnlyList<EventEnvelope> remoteEnvelopes, Guid originDeviceId, CancellationToken ct = default);

    Task<string?> GetSyncCursorAsync(string vaultId, CancellationToken ct = default);
    Task SetSyncCursorAsync(string vaultId, string cursor, CancellationToken ct = default);
}
