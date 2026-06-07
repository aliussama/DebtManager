using DebtManager.Sync.Contracts;

namespace DebtManager.Sync.Transport;

public interface ISyncTransport
{
    Task<PushBatchResponse> PushAsync(string vaultId, PushBatchRequest req, CancellationToken ct);
    Task<PullResponse> PullAsync(string vaultId, string? sinceCursor, int limit, CancellationToken ct);
}
