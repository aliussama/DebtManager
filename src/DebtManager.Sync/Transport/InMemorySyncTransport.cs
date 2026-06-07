using DebtManager.Sync.Contracts;

namespace DebtManager.Sync.Transport;

public sealed class InMemorySyncTransport : ISyncTransport
{
    private sealed class VaultState
    {
        public readonly Dictionary<Guid, SyncEventDto> EventsById = new();
        public readonly List<SyncEventDto> Ordered = new();
    }

    private readonly Dictionary<string, VaultState> _vaults = new();

    private VaultState GetVault(string vaultId)
    {
        if (!_vaults.TryGetValue(vaultId, out var v))
        {
            v = new VaultState();
            _vaults[vaultId] = v;
        }
        return v;
    }

    public Task<PushBatchResponse> PushAsync(string vaultId, PushBatchRequest req, CancellationToken ct)
    {
        var v = GetVault(vaultId);
        var accepted = 0;
        var already = 0;

        foreach (var e in req.Events)
        {
            if (v.EventsById.ContainsKey(e.EventId))
            {
                already++;
                continue;
            }

            v.EventsById[e.EventId] = e;
            v.Ordered.Add(e);
            accepted++;
        }

        // deterministic ordering for pull
        v.Ordered.Sort((a, b) =>
        {
            var c = a.OccurredAt.CompareTo(b.OccurredAt);
            if (c != 0) return c;
            return a.EventId.CompareTo(b.EventId);
        });

        return Task.FromResult(new PushBatchResponse(accepted, already));
    }

    public Task<PullResponse> PullAsync(string vaultId, string? sinceCursor, int limit, CancellationToken ct)
    {
        var v = GetVault(vaultId);

        // Cursor is just an integer offset (string)
        var start = 0;
        if (!string.IsNullOrWhiteSpace(sinceCursor) && int.TryParse(sinceCursor, out var parsed))
            start = Math.Max(0, parsed);

        var page = v.Ordered.Skip(start).Take(limit).ToList();
        var newCursor = (start + page.Count).ToString();

        return Task.FromResult(new PullResponse(newCursor, page));
    }
}
