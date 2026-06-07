using DebtManager.Domain.Events;
using DebtManager.Infrastructure.Persistence;
using DebtManager.Infrastructure.Sync;
using DebtManager.Sync.Contracts;
using DebtManager.Sync.Transport;

namespace DebtManager.Sync;

public sealed class SyncEngine
{
    private readonly ISyncStore _store;
    private readonly ISyncTransport _transport;
    public SyncEngine(ISyncStore store, ISyncTransport transport)
    {
        _store = store;
        _transport = transport;
    }

    public async Task SyncOnceAsync(string vaultId, Guid deviceId, CancellationToken ct)
    {
        // 1) Push unsent outbox
        var batch = await _store.ReadOutboxBatchAsync(200, ct);
        if (batch.Count > 0)
        {
            var envelopes = await _store.ReadEnvelopesByEventIdsAsync(batch.Select(b => b.EventId), ct);

            var dto = envelopes.Select(e => new SyncEventDto(
                EventId: e.EventId.Value,
                StreamId: e.StreamId.Value,
                EventType: e.EventType,
                OccurredAt: e.OccurredAt,
                EffectiveDate: e.EffectiveDate,
                ActorUserId: e.ActorUserId,
                DeviceId: e.DeviceId,
                CorrelationId: e.CorrelationId,
                CausationEventId: e.CausationEventId,
                PayloadSchemaVersion: e.PayloadSchemaVersion,
                PayloadJson: e.PayloadJson
            )).ToList();

            try
            {
                var resp = await _transport.PushAsync(vaultId, new PushBatchRequest(deviceId, dto), ct);
                await _store.MarkOutboxSentAsync(batch.Select(x => x.EventId), ct);
            }
            catch (Exception ex)
            {
                foreach (var item in batch)
                    await _store.MarkOutboxAttemptFailedAsync(item.EventId, ex.Message, ct);
            }
        }

        // 2) Pull remote since last cursor
        var since = await _store.GetSyncCursorAsync(vaultId, ct);
        var pull = await _transport.PullAsync(vaultId, since, 500, ct);

        if (pull.Events.Count > 0)
        {
            var envelopes = pull.Events.Select(e => new EventEnvelope(
                new EventId(e.EventId),
                new StreamId(e.StreamId),
                e.EventType,
                e.OccurredAt,
                e.EffectiveDate,
                e.ActorUserId,
                e.DeviceId,
                e.CorrelationId,
                e.CausationEventId,
                e.PayloadSchemaVersion,
                e.PayloadJson
            )).ToList();

            await _store.ApplyRemoteAsync(envelopes, originDeviceId: Guid.Empty, ct);
        }

        await _store.SetSyncCursorAsync(vaultId, pull.Cursor, ct);
    }
}
