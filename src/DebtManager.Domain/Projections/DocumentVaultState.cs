using DebtManager.Domain.Documents;

namespace DebtManager.Domain.Projections;

public sealed class DocumentVaultState
{
    public Dictionary<Guid, DocumentRecord> Documents { get; set; } = new();
    public Dictionary<string, List<DocumentLinkRecord>> LinksByEntity { get; set; } = new();
    public Dictionary<Guid, List<DocumentLinkRecord>> LinksByDocument { get; set; } = new();

    public DocumentVaultSummary GetSummary()
    {
        var total = Documents.Count;
        var active = Documents.Values.Count(d => !d.IsArchived);
        var archived = Documents.Values.Count(d => d.IsArchived);
        var missingBlobs = Documents.Values.Count(d => d.IsBlobPurged);
        var totalSize = Documents.Values.Sum(d => d.SizeBytes);

        return new DocumentVaultSummary(total, active, archived, missingBlobs, totalSize);
    }
}
