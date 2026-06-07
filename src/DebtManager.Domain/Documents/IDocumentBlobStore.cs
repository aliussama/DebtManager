namespace DebtManager.Domain.Documents;

public interface IDocumentBlobStore
{
    Task SaveAsync(string storageKey, byte[] plaintext, CancellationToken ct);
    Task<byte[]?> LoadAsync(string storageKey, CancellationToken ct);
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);
    Task PurgeAsync(string storageKey, CancellationToken ct);
}
